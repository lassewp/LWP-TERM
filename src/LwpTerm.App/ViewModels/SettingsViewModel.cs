using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LwpTerm.App.Views.Dialogs;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using LwpTerm.Core.Settings;

namespace LwpTerm.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private readonly ICredentialStore _credentials;

    public SettingsViewModel(ISettingsStore store, ICredentialStore credentials)
    {
        _store = store;
        _credentials = credentials;

        var s = store.Current;
        _theme = s.Theme;
        _terminalFontFamily = s.TerminalFontFamily;
        _terminalFontSize = s.TerminalFontSize;
        _terminalScrollback = s.TerminalScrollback;
        _colorizeOutput = s.ColorizeOutput;
        _defaultShell = s.DefaultShell;
        _restoreTabs = s.RestoreTabsOnStartup;
        _masterPasswordEnabled = credentials.MasterPasswordEnabled;
    }

    public Array Themes => Enum.GetValues<AppTheme>();
    public Array Shells => Enum.GetValues<LocalShellKind>();

    /// <summary>Installed fonts, for the searchable terminal-font picker. The field
    /// still holds a free-text CSS-style fallback stack ("Cascadia Mono, Consolas,
    /// monospace") — picking one from the list replaces it with just that name;
    /// typing is still open for anyone who wants a custom stack.</summary>
    public ObservableCollection<string> AvailableFonts { get; } = new(
        Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Distinct()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

    [ObservableProperty] private AppTheme _theme;
    [ObservableProperty] private string _terminalFontFamily;
    [ObservableProperty] private int _terminalFontSize;
    [ObservableProperty] private int _terminalScrollback;
    [ObservableProperty] private bool _colorizeOutput;
    [ObservableProperty] private LocalShellKind _defaultShell;
    [ObservableProperty] private bool _restoreTabs;

    [ObservableProperty] private bool _masterPasswordEnabled;

    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Set true by the window when Save succeeds.</summary>
    public bool Saved { get; private set; }

    [RelayCommand]
    private void ToggleMasterPassword()
    {
        try
        {
            if (_credentials.MasterPasswordEnabled)
            {
                var current = InputDialog.Ask("Enter the current master password to disable it:", "Master password", isPassword: true);
                if (current is null)
                {
                    return;
                }

                _credentials.DisableMasterPassword(current);
                MasterPasswordEnabled = false;
                StatusText = "Master password disabled.";
            }
            else
            {
                var pw1 = InputDialog.Ask("Choose a master password:", "Master password", isPassword: true);
                if (string.IsNullOrEmpty(pw1))
                {
                    return;
                }

                var pw2 = InputDialog.Ask("Re-enter the master password:", "Master password", isPassword: true);
                if (pw1 != pw2)
                {
                    StatusText = "Passwords did not match.";
                    return;
                }

                _credentials.EnableMasterPassword(pw1);
                MasterPasswordEnabled = true;
                StatusText = "Master password enabled. Existing secrets keep working; new ones use it.";
            }
        }
        catch (InvalidMasterPasswordException)
        {
            StatusText = "Incorrect master password.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private void Save()
    {
        _store.Save(new AppSettings
        {
            Theme = Theme,
            TerminalFontFamily = string.IsNullOrWhiteSpace(TerminalFontFamily) ? "Cascadia Mono, Consolas, monospace" : TerminalFontFamily.Trim(),
            TerminalFontSize = Math.Clamp(TerminalFontSize, 6, 48),
            TerminalScrollback = Math.Clamp(TerminalScrollback, 100, 500_000),
            ColorizeOutput = ColorizeOutput,
            DefaultShell = DefaultShell,
            RestoreTabsOnStartup = RestoreTabs
        });

        Saved = true;
    }
}
