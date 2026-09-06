using System;
using System.IO;
using FluentAssertions;
using LwpTerm.Core;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Settings;

namespace LwpTerm.Core.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _dir;
    private readonly AppPaths _paths;

    public AppSettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lwpterm-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _paths = AppPaths.Portable(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var store = new JsonSettingsStore(_paths);
        store.Current.Theme.Should().Be(AppTheme.Dark);
        store.Current.TerminalFontSize.Should().Be(14);
    }

    [Fact]
    public void Saved_settings_round_trip_including_enums()
    {
        var store = new JsonSettingsStore(_paths);
        store.Save(new AppSettings
        {
            Theme = AppTheme.Light,
            TerminalFontSize = 18,
            TerminalScrollback = 12345,
            DefaultShell = LocalShellKind.Wsl,
            RestoreTabsOnStartup = true
        });

        var reloaded = new JsonSettingsStore(_paths).Current;
        reloaded.Theme.Should().Be(AppTheme.Light);
        reloaded.TerminalFontSize.Should().Be(18);
        reloaded.TerminalScrollback.Should().Be(12345);
        reloaded.DefaultShell.Should().Be(LocalShellKind.Wsl);
        reloaded.RestoreTabsOnStartup.Should().BeTrue();
    }
}
