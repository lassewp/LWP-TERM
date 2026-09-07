using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LwpTerm.App.ViewModels.Tabs;

public enum SessionTabState
{
    Idle,
    Connecting,
    Connected,
    Disconnected,
    Faulted
}

/// <summary>
/// Base class for anything that can live as a document tab in the centre pane:
/// terminals, file browsers, RDP and VNC surfaces.
/// </summary>
public abstract partial class SessionTabViewModel : ObservableObject, IDisposable
{
    private bool _disposed;

    protected SessionTabViewModel(string title)
    {
        _title = title;
        _contentId = Guid.NewGuid().ToString("N");
    }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _toolTip;

    [ObservableProperty]
    private SessionTabState _state = SessionTabState.Idle;

    [ObservableProperty]
    private string _statusText = string.Empty;

    partial void OnStateChanged(SessionTabState value) => OnPropertyChanged(nameof(IsDisconnected));

    /// <summary>True once the session has dropped or failed — drives the reconnect overlay.</summary>
    public bool IsDisconnected => State is SessionTabState.Disconnected or SessionTabState.Faulted;

    /// <summary>
    /// Whether the reconnect overlay offers a "Reconnect" button. Session tabs
    /// that can re-establish their transport (terminals, RDP, VNC) set this true.
    /// </summary>
    public virtual bool CanReconnect => false;

    /// <summary>Overlay "Reconnect" — subclasses re-establish the transport.</summary>
    [RelayCommand]
    private void Reconnect() => OnReconnectRequested();

    /// <summary>Overlay "Quit" — closes and disposes the tab.</summary>
    [RelayCommand]
    private void QuitSession() => RequestClose();

    protected virtual void OnReconnectRequested()
    {
    }

    /// <summary>Stable id used by AvalonDock for layout serialisation.</summary>
    public string ContentId
    {
        get => _contentId;
        init => _contentId = value;
    }

    private string _contentId;

    public bool CanClose => true;

    /// <summary>Raised when the tab wants to be removed from the document pane.</summary>
    public event EventHandler? CloseRequested;

    public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    protected virtual void DisposeCore()
    {
    }
}
