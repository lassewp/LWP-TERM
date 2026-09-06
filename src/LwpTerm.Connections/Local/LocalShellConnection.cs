using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Terminal;
using Microsoft.Extensions.Logging;

namespace LwpTerm.Connections.Local;

/// <summary>
/// <see cref="ITerminalConnection"/> backed by a Windows Pseudo Console running a
/// local shell (PowerShell / pwsh / CMD / WSL).
/// </summary>
public sealed class LocalShellConnection : ITerminalConnection
{
    private readonly LocalShellConnectionSettings _settings;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private ConPtySession? _pty;
    private Thread? _reader;
    private volatile bool _closed;

    public LocalShellConnection(LocalShellConnectionSettings settings, ILogger log)
    {
        _settings = settings;
        _log = log;
    }

    public event EventHandler<ReadOnlyMemory<byte>>? DataReceived;
    public event EventHandler<Exception?>? Closed;

    public bool IsConnected => _pty is not null && !_closed;

    public Task ConnectAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        if (_pty is not null)
        {
            throw new InvalidOperationException("The shell is already running.");
        }

        var commandLine = BuildCommandLine();
        _log.LogInformation("Starting local shell: {CommandLine}", commandLine);

        _pty = ConPtySession.Start(
            commandLine,
            NormaliseDirectory(_settings.StartingDirectory),
            (short)Math.Clamp(columns, 1, short.MaxValue),
            (short)Math.Clamp(rows, 1, short.MaxValue));

        _pty.Exited += OnPtyExited;

        _reader = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = $"conpty-reader-{_pty.ProcessId}"
        };
        _reader.Start();

        return Task.CompletedTask;
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var pty = _pty;
        if (pty is null || _closed)
        {
            return;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await pty.InputStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
            await pty.InputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            MarkClosed(ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        _pty?.Resize(
            (short)Math.Clamp(columns, 1, short.MaxValue),
            (short)Math.Clamp(rows, 1, short.MaxValue));
        return Task.CompletedTask;
    }

    private void ReadLoop()
    {
        var buffer = new byte[8192];
        var stream = _pty!.OutputStream;

        try
        {
            // Drain to EOF. EOF only happens once the pseudo console is closed
            // (on child exit we close it explicitly), so pending output is never lost.
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                DataReceived?.Invoke(this, new ReadOnlyMemory<byte>(buffer, 0, read));
            }

            MarkClosed(null);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            MarkClosed(null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "ConPTY reader loop faulted");
            MarkClosed(ex);
        }
    }

    private void OnPtyExited(object? sender, int exitCode)
    {
        _log.LogInformation("Local shell exited with code {ExitCode}", exitCode);

        // Let ConPTY flush the child's last output, then close the console so the
        // reader hits EOF and raises Closed after draining.
        _ = Task.Run(async () =>
        {
            await Task.Delay(60).ConfigureAwait(false);
            _pty?.CloseConsole();
        });
    }

    private void MarkClosed(Exception? error)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Closed?.Invoke(this, error);
    }

    private string BuildCommandLine()
    {
        var system32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System));

        var (exe, baseArgs) = _settings.ShellKind switch
        {
            LocalShellKind.PowerShell => (Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe"), "-NoLogo"),
            LocalShellKind.Pwsh => ("pwsh.exe", "-NoLogo"),
            LocalShellKind.Cmd => (Path.Combine(system32, "cmd.exe"), ""),
            LocalShellKind.Wsl => (Path.Combine(system32, "wsl.exe"),
                string.IsNullOrWhiteSpace(_settings.WslDistro) ? "" : $"-d {_settings.WslDistro}"),
            _ => (Path.Combine(system32, "cmd.exe"), "")
        };

        if (!File.Exists(exe) && Path.IsPathRooted(exe))
        {
            // Fall back to a bare name and let CreateProcess search PATH.
            exe = Path.GetFileName(exe);
        }

        var sb = new StringBuilder();
        sb.Append(Quote(exe));
        AppendArg(sb, baseArgs);
        AppendArg(sb, _settings.Arguments);
        return sb.ToString();
    }

    private static void AppendArg(StringBuilder sb, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.Append(' ').Append(value.Trim());
        }
    }

    private static string Quote(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private static string? NormaliseDirectory(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(dir);
        return Directory.Exists(expanded) ? expanded : null;
    }

    public async ValueTask DisposeAsync()
    {
        _closed = true;

        var pty = _pty;
        _pty = null;
        if (pty is not null)
        {
            pty.Exited -= OnPtyExited;
            pty.Dispose();
        }

        if (_reader is { IsAlive: true })
        {
            await Task.Run(() => _reader.Join(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        }

        _writeGate.Dispose();
    }
}
