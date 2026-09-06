using System;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.Tabs;

/// <summary>Hosts an embedded VNC session (VncSharp <c>RemoteDesktop</c>).</summary>
public sealed partial class VncTabViewModel : SessionTabViewModel
{
    private readonly ILogger _log;

    public VncTabViewModel(string title, VncConnectionSettings settings, string? password, ILogger log)
        : base(title)
    {
        Settings = settings;
        Password = password;
        _log = log;
        ToolTip = $"{settings.Host}:{settings.Port}";
        StatusText = "Not connected";
    }

    public VncConnectionSettings Settings { get; }

    public string? Password { get; }

    /// <summary>The view subscribes and forwards Ctrl+Alt+Del to the control.</summary>
    public event Action? SendCtrlAltDelRequested;

    public void NotifyConnecting() { State = SessionTabState.Connecting; StatusText = "Connecting…"; }

    public void NotifyConnected() { State = SessionTabState.Connected; StatusText = "Connected"; }

    public void NotifyDisconnected(string? reason)
    {
        State = SessionTabState.Disconnected;
        StatusText = string.IsNullOrWhiteSpace(reason) ? "Disconnected" : "Disconnected: " + reason;
    }

    public void NotifyFailed(string message)
    {
        State = SessionTabState.Faulted;
        StatusText = "Failed: " + message;
        _log.LogWarning("VNC '{Title}' failed: {Message}", Title, message);
    }

    [RelayCommand]
    private void SendCtrlAltDel() => SendCtrlAltDelRequested?.Invoke();
}
