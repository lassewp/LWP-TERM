using System;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.Tabs;

/// <summary>Hosts an embedded VNC session; the surface lives in <see cref="Host"/> and survives docking / floating.</summary>
public sealed partial class VncTabViewModel : SessionTabViewModel
{
    private readonly ILogger _log;

    public VncTabViewModel(string title, VncConnectionSettings settings, string? password, ILogger log)
        : base(title)
    {
        Settings = settings;
        _log = log;
        ToolTip = $"{settings.Host}:{settings.Port}";
        StatusText = "Not connected";

        Host = new VncSessionHost(settings, password);
        Host.Connected += () => Dispatch(() => { State = SessionTabState.Connected; StatusText = "Connected"; });
        Host.ConnectionLost += reason => Dispatch(() =>
        {
            State = SessionTabState.Disconnected;
            StatusText = string.IsNullOrWhiteSpace(reason) ? "Connection lost" : "Connection lost: " + reason;
        });
    }

    public VncConnectionSettings Settings { get; }

    public VncSessionHost Host { get; }

    public override bool CanReconnect => true;

    protected override void OnReconnectRequested()
    {
        State = SessionTabState.Connecting;
        StatusText = "Reconnecting…";
        Host.Reconnect();
    }

    public void NotifyConnecting()
    {
        if (State is SessionTabState.Idle or SessionTabState.Disconnected)
        {
            State = SessionTabState.Connecting;
            StatusText = "Connecting…";
        }
    }

    [RelayCommand]
    private void SendCtrlAltDel() => Host.SendCtrlAltDel();

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

    protected override void DisposeCore() => Host.Dispose();
}
