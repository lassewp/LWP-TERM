using System;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;
using LwpTerm.Core;
using LwpTerm.Core.Terminal;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.Tabs;

public sealed record TerminalConfig(string FontFamily, int FontSize, int Scrollback, bool ColorizeOutput = true)
{
    public static readonly TerminalConfig Default = new("Cascadia Mono, Consolas, monospace", 14, 5000);
}

/// <summary>
/// A terminal document tab. The xterm.js/WebView2 surface lives in
/// <see cref="Host"/> (owned here) so docking / floating the tab does not lose
/// the session or its scrollback.
/// </summary>
public sealed partial class TerminalTabViewModel : SessionTabViewModel
{
    private static readonly string Esc = char.ConvertFromUtf32(0x1B);

    private readonly Func<ITerminalConnection> _connectionFactory;
    private readonly ILogger _log;
    private readonly AppPaths _paths;

    private ITerminalConnection _connection;
    private bool _connectRequested;
    private FileStream? _sessionLog;
    private int _cols = 80;
    private int _rows = 24;

    public TerminalTabViewModel(string title, Func<ITerminalConnection> connectionFactory, ILogger log, AppPaths paths, TerminalConfig? config = null)
        : base(title)
    {
        _connectionFactory = connectionFactory;
        _log = log;
        _paths = paths;
        ToolTip = title;
        StatusText = "Not connected";

        Host = new TerminalSessionHost(paths);
        Host.SetConfig(config ?? TerminalConfig.Default);
        Host.Ready += OnTerminalReady;
        Host.InputReceived += OnTerminalInput;
        Host.Resized += OnTerminalResize;
        Host.Bell += () => { try { System.Media.SystemSounds.Beep.Play(); } catch { /* ignore */ } };

        _connection = _connectionFactory();
        WireConnection();
    }

    public TerminalSessionHost Host { get; }

    public override bool CanReconnect => true;

    private void WireConnection()
    {
        _connection.DataReceived += OnConnectionData;
        _connection.Closed += OnConnectionClosed;
    }

    private void UnwireConnection()
    {
        _connection.DataReceived -= OnConnectionData;
        _connection.Closed -= OnConnectionClosed;
    }

    public bool IsLogging => _sessionLog is not null;

    private static string Dim(string text) => $"\r\n{Esc}[90m{text}{Esc}[0m\r\n";

    private static string ErrLine(string text) => $"\r\n{Esc}[31m{text}{Esc}[0m\r\n";

    // ---- Host events -------------------------------------------------

    private async void OnTerminalReady(int columns, int rows)
    {
        _cols = columns;
        _rows = rows;

        if (_connectRequested)
        {
            // Page reloaded (e.g. re-parent recovery): the buffer already replayed;
            // just re-sync the size.
            await SafeResize(columns, rows).ConfigureAwait(true);
            return;
        }

        await StartSessionAsync(columns, rows).ConfigureAwait(true);
    }

    private async System.Threading.Tasks.Task StartSessionAsync(int columns, int rows)
    {
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
            Host.SendNotice(ErrLine($"[failed to start: {ex.Message}]"));
        }
    }

    private async void OnTerminalInput(byte[] data)
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

    private async void OnTerminalResize(int columns, int rows)
    {
        _cols = columns;
        _rows = rows;
        await SafeResize(columns, rows).ConfigureAwait(true);
    }

    private async System.Threading.Tasks.Task SafeResize(int columns, int rows)
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

    // ---- Connection events ----------------------------------------

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

        Dispatch(() => Host.SendOutput(copy));
    }

    private void OnConnectionClosed(object? sender, Exception? error)
    {
        Dispatch(() =>
        {
            State = error is null ? SessionTabState.Disconnected : SessionTabState.Faulted;
            StatusText = error is null ? "Session ended" : "Session ended: " + error.Message;
            Host.SendNotice(error is null
                ? Dim("[session ended]")
                : ErrLine($"[session ended: {error.Message}]"));
        });
    }

    // ---- Commands -----------------------------------------------

    [RelayCommand]
    private void ToggleLogging()
    {
        if (_sessionLog is not null)
        {
            _sessionLog.Flush();
            _sessionLog.Dispose();
            _sessionLog = null;
            OnPropertyChanged(nameof(IsLogging));
            Host.SendNotice(Dim("[logging stopped]"));
            return;
        }

        var safeName = string.Join("_", Title.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(_paths.LogsDirectory, $"session-{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _sessionLog = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        OnPropertyChanged(nameof(IsLogging));
        _log.LogInformation("Session logging started: {Path}", path);
        Host.SendNotice(Dim($"[logging to {path}]"));
    }

    protected override void OnReconnectRequested()
    {
        if (State is SessionTabState.Connecting or SessionTabState.Connected)
        {
            return;
        }

        var old = _connection;
        UnwireConnection();
        _connection = _connectionFactory();
        WireConnection();
        _ = old.DisposeAsync();

        Host.SendNotice(Dim("[reconnecting…]"));
        _ = StartSessionAsync(_cols, _rows);
    }

    private static void Dispatch(Action action)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            app.Dispatcher.BeginInvoke(action);
        }
    }

    protected override void DisposeCore()
    {
        UnwireConnection();

        _sessionLog?.Dispose();
        _sessionLog = null;

        Host.Dispose();
        _ = _connection.DisposeAsync();
    }
}
