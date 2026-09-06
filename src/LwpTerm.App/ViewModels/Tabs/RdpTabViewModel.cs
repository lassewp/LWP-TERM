using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Services;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.Tabs;

/// <summary>
/// Hosts an embedded Remote Desktop session. The live surface lives in
/// <see cref="Host"/> (owned here, re-parented by the view as the tab docks or
/// floats) so detaching a tab does not drop the session.
/// </summary>
public sealed partial class RdpTabViewModel : SessionTabViewModel
{
    private readonly ILogger _log;

    public RdpTabViewModel(string title, RdpConnectionSettings settings, string? password, ILogger log)
        : base(title)
    {
        Settings = settings;
        _log = log;
        ToolTip = $"{settings.Host}:{settings.Port}";
        StatusText = "Not connected";

        Host = new RdpSessionHost(settings, password);
        Host.Connected += () => Dispatch(() => { State = SessionTabState.Connected; StatusText = "Connected"; });
        Host.Disconnected += reason => Dispatch(() =>
        {
            State = SessionTabState.Disconnected;
            StatusText = string.IsNullOrWhiteSpace(reason) ? "Disconnected" : "Disconnected: " + reason;
        });
    }

    public RdpConnectionSettings Settings { get; }

    public RdpSessionHost Host { get; }

    /// <summary>Called by the view when the surface starts connecting.</summary>
    public void NotifyConnecting()
    {
        if (State is SessionTabState.Idle or SessionTabState.Disconnected)
        {
            State = SessionTabState.Connecting;
            StatusText = "Connecting…";
        }
    }

    [RelayCommand]
    private void OpenInMstsc()
    {
        try
        {
            var file = Path.Combine(Path.GetTempPath(), $"lwpterm-{Guid.NewGuid():N}.rdp");
            File.WriteAllText(file, BuildRdpFile());
            Process.Start(new ProcessStartInfo("mstsc.exe", $"\"{file}\"") { UseShellExecute = true });
            StatusText = "Opened in mstsc.exe";
        }
        catch (Exception ex)
        {
            State = SessionTabState.Faulted;
            StatusText = "Failed: " + ex.Message;
            _log.LogWarning(ex, "mstsc.exe fallback failed for '{Title}'", Title);
        }
    }

    private string BuildRdpFile()
    {
        var full = $"{Settings.Host}:{(Settings.Port <= 0 ? 3389 : Settings.Port)}";
        var lines = new[]
        {
            $"full address:s:{full}",
            $"username:s:{(string.IsNullOrWhiteSpace(Settings.Domain) ? "" : Settings.Domain + "\\")}{Settings.Username}",
            $"screen mode id:i:{(Settings.ResolutionMode == RdpResolutionMode.FitToWindow ? 2 : 1)}",
            Settings.ResolutionMode == RdpResolutionMode.Fixed ? $"desktopwidth:i:{Settings.Width}" : "",
            Settings.ResolutionMode == RdpResolutionMode.Fixed ? $"desktopheight:i:{Settings.Height}" : "",
            $"redirectclipboard:i:{(Settings.RedirectClipboard ? 1 : 0)}",
            $"drivestoredirect:s:{(Settings.RedirectDrives ? "*" : "")}",
            "prompt for credentials:i:1"
        };

        return string.Join(Environment.NewLine, Array.FindAll(lines, l => l.Length > 0));
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

    protected override void DisposeCore() => Host.Dispose();
}
