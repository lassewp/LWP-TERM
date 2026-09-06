using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using static LwpTerm.Connections.Local.ConPtyNative;

namespace LwpTerm.Connections.Local;

/// <summary>
/// Owns a ConPTY instance plus the child process attached to it. Exposes the
/// child's stdin as <see cref="InputStream"/> (write here) and its stdout/stderr
/// as <see cref="OutputStream"/> (read here). Not thread-safe for concurrent
/// writes; the caller serialises them.
/// </summary>
internal sealed class ConPtySession : IDisposable
{
    private readonly object _sync = new();

    private IntPtr _hpc = IntPtr.Zero;
    private IntPtr _attributeList = IntPtr.Zero;
    private PROCESS_INFORMATION _pi;
    private SafeFileHandle? _inputWrite;
    private SafeFileHandle? _outputRead;
    private SafeFileHandle? _inputPtySide;
    private SafeFileHandle? _outputPtySide;
    private Thread? _exitWatcher;
    private bool _disposed;

    public FileStream InputStream { get; private set; } = null!;

    public FileStream OutputStream { get; private set; } = null!;

    public int ProcessId => _pi.dwProcessId;

    /// <summary>Raised once when the child process exits. Argument is its exit code.</summary>
    public event EventHandler<int>? Exited;

    public static ConPtySession Start(string commandLine, string? workingDirectory, short columns, short rows)
    {
        var session = new ConPtySession();
        try
        {
            session.Initialise(commandLine, workingDirectory, columns, rows);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private void Initialise(string commandLine, string? workingDirectory, short columns, short rows)
    {
        // Pipe naming: ConPTY reads inputPtySide and writes outputPtySide;
        // we write _inputWrite and read _outputRead.
        if (!CreatePipe(out var inputPtySide, out var inputWrite, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (input) failed.");
        }

        if (!CreatePipe(out var outputRead, out var outputPtySide, IntPtr.Zero, 0))
        {
            inputPtySide.Dispose();
            inputWrite.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (output) failed.");
        }

        _inputWrite = inputWrite;
        _outputRead = outputRead;
        _inputPtySide = inputPtySide;
        _outputPtySide = outputPtySide;

        var size = new COORD(Math.Max((short)1, columns), Math.Max((short)1, rows));
        var hr = CreatePseudoConsole(
            size,
            inputPtySide.DangerousGetHandle(),
            outputPtySide.DangerousGetHandle(),
            0,
            out _hpc);
        if (hr != S_OK)
        {
            throw Marshal.GetExceptionForHR(hr) ?? new Win32Exception(hr, "CreatePseudoConsole failed.");
        }

        StartChildProcess(commandLine, workingDirectory);

        InputStream = new FileStream(_inputWrite, FileAccess.Write, bufferSize: 4096, isAsync: false);
        OutputStream = new FileStream(_outputRead, FileAccess.Read, bufferSize: 4096, isAsync: false);
    }

    private void StartChildProcess(string commandLine, string? workingDirectory)
    {
        var lpSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
        _attributeList = Marshal.AllocHGlobal(lpSize);

        if (!InitializeProcThreadAttributeList(_attributeList, 1, 0, ref lpSize))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");
        }

        if (!UpdateProcThreadAttribute(
                _attributeList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hpc,
                (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");
        }

        var startupInfo = new STARTUPINFOEX
        {
            StartupInfo =
            {
                cb = Marshal.SizeOf<STARTUPINFOEX>(),
                dwFlags = STARTF_USESTDHANDLES
            },
            lpAttributeList = _attributeList
        };

        var environmentBlock = Marshal.StringToHGlobalUni(BuildEnvironmentBlock());
        try
        {
            if (!CreateProcess(
                    null, commandLine, IntPtr.Zero, IntPtr.Zero, bInheritHandles: false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, environmentBlock,
                    string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                    ref startupInfo, out _pi))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateProcess failed for: {commandLine}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(environmentBlock);
        }

        var processHandle = _pi.hProcess;
        _exitWatcher = new Thread(() => WatchForExit(processHandle))
        {
            IsBackground = true,
            Name = $"conpty-exit-{_pi.dwProcessId}"
        };
        _exitWatcher.Start();
    }

    private static string BuildEnvironmentBlock()
    {
        // Inherit the current environment and guarantee TERM is set for shells that check it.
        var block = new System.Text.StringBuilder();
        var haveTerm = false;
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            var key = kv.Key?.ToString();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (string.Equals(key, "TERM", StringComparison.OrdinalIgnoreCase))
            {
                haveTerm = true;
            }

            block.Append(key).Append('=').Append(kv.Value).Append('\0');
        }

        if (!haveTerm)
        {
            block.Append("TERM=xterm-256color\0");
        }

        block.Append('\0');
        return block.ToString();
    }

    private void WatchForExit(IntPtr processHandle)
    {
        WaitForSingleObject(processHandle, 0xFFFFFFFF); // INFINITE
        GetExitCodeProcess(processHandle, out var exitCode);
        Exited?.Invoke(this, exitCode);
    }

    public void Resize(short columns, short rows)
    {
        lock (_sync)
        {
            if (_disposed || _hpc == IntPtr.Zero)
            {
                return;
            }

            ResizePseudoConsole(_hpc, new COORD(Math.Max((short)1, columns), Math.Max((short)1, rows)));
        }
    }

    /// <summary>
    /// Closes the pseudo console so ConPTY flushes and then EOFs the output pipe,
    /// letting the reader drain cleanly. Safe to call more than once.
    /// </summary>
    public void CloseConsole()
    {
        lock (_sync)
        {
            if (_hpc == IntPtr.Zero)
            {
                return;
            }

            ClosePseudoConsole(_hpc);
            _hpc = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        CloseConsole();

        try { OutputStream?.Dispose(); } catch { /* ignore */ }
        try { InputStream?.Dispose(); } catch { /* ignore */ }
        try { _inputPtySide?.Dispose(); } catch { /* ignore */ }
        try { _outputPtySide?.Dispose(); } catch { /* ignore */ }

        if (_pi.hProcess != IntPtr.Zero)
        {
            if (GetExitCodeProcess(_pi.hProcess, out var code) && code == STILL_ACTIVE)
            {
                TerminateProcess(_pi.hProcess, 0);
            }

            CloseHandle(_pi.hThread);
            CloseHandle(_pi.hProcess);
            _pi = default;
        }

        if (_attributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = IntPtr.Zero;
        }
    }
}
