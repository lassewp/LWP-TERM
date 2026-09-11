using System;
using System.Windows.Forms.Integration;
using LwpTerm.Core.Sessions;
using VncSharp;

namespace LwpTerm.App.Services;

/// <summary>
/// A long-lived VNC surface owned by the tab view model. The <see cref="View"/>
/// is re-parented as the tab docks / floats, so detaching does not drop the
/// session.
/// </summary>
public sealed class VncSessionHost : IDisposable
{
    private readonly VncConnectionSettings _settings;
    private readonly string _password;

    private RemoteDesktop? _vnc;
    private bool _started;
    private bool _disposed;

    public VncSessionHost(VncConnectionSettings settings, string? password)
    {
        _settings = settings;
        _password = password ?? string.Empty;
        View = new WindowsFormsHost();
    }

    public WindowsFormsHost View { get; }

    public event Action? Connected;

    public event Action<string?>? ConnectionLost;

    public void EnsureStarted()
    {
        if (_disposed || _started)
        {
            return;
        }

        _started = true;

        var s = _settings;
        _vnc = new RemoteDesktop
        {
            VncPort = s.Port <= 0 ? 5900 : s.Port,
            GetPassword = () => _password
        };
        _vnc.ConnectComplete += (_, _) => Connected?.Invoke();
        _vnc.ConnectionLost += (_, _) => ConnectionLost?.Invoke(null);

        View.Child = _vnc;
        _vnc.Connect(s.Host, s.ViewOnly, scaled: true);
    }

    /// <summary>
    /// Nudge the surface to repaint after <see cref="View"/> has been
    /// re-parented across top-level windows (e.g. leaving full screen) — see
    /// <see cref="RdpSessionHost.NotifyReattached"/> for why a resize, not
    /// just a repaint, is what actually wakes it back up.
    /// </summary>
    public void NotifyReattached()
    {
        if (_disposed || _vnc is null || _vnc.ClientSize is not { Width: > 2, Height: > 2 } size)
        {
            return;
        }

        _vnc.ClientSize = new System.Drawing.Size(size.Width - 1, size.Height);
        _vnc.ClientSize = size;
        _vnc.Invalidate(true);
        _vnc.Update();
    }

    /// <summary>Drop the lost client and dial the host again.</summary>
    public void Reconnect()
    {
        if (_disposed)
        {
            return;
        }

        TeardownClient();
        _started = false;
        EnsureStarted();
    }

    public void SendCtrlAltDel()
    {
        try
        {
            _vnc?.SendSpecialKeys(SpecialKeys.CtrlAltDel);
        }
        catch
        {
            // not connected
        }
    }

    private void TeardownClient()
    {
        if (_vnc is not null)
        {
            try
            {
                if (_vnc.IsConnected)
                {
                    _vnc.Disconnect();
                }
            }
            catch
            {
                // ignore
            }

            _vnc.Dispose();
            _vnc = null;
        }

        try { View.Child = null; } catch { /* ignore */ }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TeardownClient();
    }
}
