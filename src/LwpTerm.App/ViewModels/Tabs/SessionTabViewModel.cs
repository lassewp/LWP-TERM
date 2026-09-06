using System;
using CommunityToolkit.Mvvm.ComponentModel;

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
