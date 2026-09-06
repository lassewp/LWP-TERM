using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.ViewModels.Tabs;

/// <summary>
/// Hosts an embedded Remote Desktop (MSTSC ActiveX) session. The view does the
/// COM interop; this VM holds settings, state and the "open in mstsc.exe"
/// fallback.
/// </summary>
public sealed partial class RdpTabViewModel : SessionTabViewModel
{
    private readonly ILogger _log;

    public RdpTabViewModel(string title, RdpConnectionSettings settings, string? password, ILogger log)
        : base(title)
    {
        Settings = settings;
        Password = password;
        _log = log;
        ToolTip = $"{settings.Host}:{settings.Port}";
        StatusText = "Not connected";
    }

    public RdpConnectionSettings Settings { get; }

    public string? Password { get; }

    /// <summary>Raised by the view when the embedded session ends.</summary>
    public void NotifyDisconnected(string reason)
    {
        State = SessionTabState.Disconnected;
        StatusText = string.IsNullOrWhiteSpace(reason) ? "Disconnected" : "Disconnected: " + reason;
    }

    public void NotifyConnecting() { State = SessionTabState.Connecting; StatusText = "Connecting…"; }

    public void NotifyConnected() { State = SessionTabState.Connected; StatusText = "Connected"; }

    public void NotifyFailed(string message)
    {
        State = SessionTabState.Faulted;
        StatusText = "Failed: " + message;
        _log.LogWarning("RDP '{Title}' failed: {Message}", Title, message);
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
            NotifyFailed(ex.Message);
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
}
