using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.Core;
using LwpTerm.Core.Terminal;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.Tabs;

/// <summary>
/// A document tab that hosts a terminal surface (xterm.js in WebView2) driven by
/// an <see cref="ITerminalConnection"/>. The view pushes user input / resize /
/// ready notifications in; the VM raises <see cref="Output"/> for bytes to render.
/// </summary>
public sealed partial class TerminalTabViewModel : SessionTabViewModel
{
    private static readonly string Esc = char.ConvertFromUtf32(0x1B);

    private readonly ITerminalConnection _connection;
    private readonly ILogger _log;
    private readonly AppPaths _paths;

    private bool _connectRequested;
    private FileStream? _sessionLog;

    public TerminalTabViewModel(string title, ITerminalConnection connection, ILogger log, AppPaths paths)
        : base(title)
    {
        _connection = connection;
        _log = log;
        _paths = paths;
        ToolTip = title;
        StatusText = "Not connected";

        _connection.DataReceived += OnConnectionData;
        _connection.Closed += OnConnectionClosed;
    }

    /// <summary>Raised on the UI thread with bytes the terminal should render.</summary>
    public event Action<byte[]>? Output;

    /// <summary>Raised when the VM wants the view to write a short status line into the terminal.</summary>
    public event Action<string>? Notice;

    public bool IsLogging => _sessionLog is not null;

    private static string Dim(string text) => $"\r\n{Esc}[90m{text}{Esc}[0m\r\n";

    private static string ErrLine(string text) => $"\r\n{Esc}[31m{text}{Esc}[0m\r\n";

    // ---- Called by the view --------------------------------------------

    public async void OnTerminalReady(int columns, int rows)
    {
        if (_connectRequested)
        {
            return;
        }

        _connectRequested = true;
        State = SessionTabState.Connecting;
        StatusText = "Connecting…";

        try
        {
            await _connection.ConnectAsync(columns, rows).ConfigureAwait(true);
            State = SessionTabState.Connected;
            StatusText = "Connected";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start terminal session '{Title}'", Title);
            State = SessionTabState.Faulted;
            StatusText = "Failed: " + ex.Message;
            Notice?.Invoke(ErrLine($"[failed to start: {ex.Message}]"));
        }
    }

    public async void OnTerminalInput(byte[] data)
    {
        try
        {
            await _connection.WriteAsync(data).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Terminal write failed");
        }
    }

    public async void OnTerminalResize(int columns, int rows)
    {
        try
        {
            await _connection.ResizeAsync(columns, rows).ConfigureAwait(true);
            if (State == SessionTabState.Connected)
            {
                StatusText = $"Connected — {columns}×{rows}";
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Terminal resize failed");
        }
    }

    // ---- Connection events -------------------------------------------

    private void OnConnectionData(object? sender, ReadOnlyMemory<byte> data)
    {
        var copy = data.ToArray();

        try
        {
            _sessionLog?.Write(copy, 0, copy.Length);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Session log write failed");
        }

        var app = Application.Current;
        if (app is null)
        {
            Output?.Invoke(copy);
            return;
        }

        app.Dispatcher.BeginInvoke(() => Output?.Invoke(copy));
    }

    private void OnConnectionClosed(object? sender, Exception? error)
    {
        void Finish()
        {
            State = error is null ? SessionTabState.Disconnected : SessionTabState.Faulted;
            StatusText = error is null ? "Session ended" : "Session ended: " + error.Message;
            Notice?.Invoke(error is null
                ? Dim("[session ended]")
                : ErrLine($"[session ended: {error.Message}]"));
        }

        var app = Application.Current;
        if (app is null)
        {
            Finish();
        }
        else
        {
            app.Dispatcher.BeginInvoke(Finish);
        }
    }

    // ---- Commands --------------------------------------------------

    [RelayCommand]
    private void ToggleLogging()
    {
        if (_sessionLog is not null)
        {
            _sessionLog.Flush();
            _sessionLog.Dispose();
            _sessionLog = null;
            OnPropertyChanged(nameof(IsLogging));
            Notice?.Invoke(Dim("[logging stopped]"));
            return;
        }

        var safeName = string.Join("_", Title.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(_paths.LogsDirectory, $"session-{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _sessionLog = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        OnPropertyChanged(nameof(IsLogging));
        _log.LogInformation("Session logging started: {Path}", path);
        Notice?.Invoke(Dim($"[logging to {path}]"));
    }

    [RelayCommand]
    private void Reconnect() => Notice?.Invoke(Dim("[reconnect is available from M3]"));

    protected override void DisposeCore()
    {
        _connection.DataReceived -= OnConnectionData;
        _connection.Closed -= OnConnectionClosed;

        _sessionLog?.Dispose();
        _sessionLog = null;

        _ = _connection.DisposeAsync();
    }
}
