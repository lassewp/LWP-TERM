using System;
using System.IO;
using System.Text.Json;
using LwpTerm.Core.Sessions;

namespace LwpTerm.Core.Settings;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>User-tunable application settings persisted to <c>settings.json</c>.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string TerminalFontFamily { get; set; } = "Cascadia Mono, Consolas, 'Courier New', monospace";

    public int TerminalFontSize { get; set; } = 14;

    public int TerminalScrollback { get; set; } = 5000;

    public LocalShellKind DefaultShell { get; set; } = LocalShellKind.PowerShell;

    public bool RestoreTabsOnStartup { get; set; }
}

public interface ISettingsStore
{
    AppSettings Current { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;

    public JsonSettingsStore(AppPaths paths)
    {
        _paths = paths;
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile), Options)
                          ?? new AppSettings();
                return Current;
            }
        }
        catch
        {
            // fall through to defaults
        }

        Current = new AppSettings();
        return Current;
    }

    public void Save(AppSettings settings)
    {
        Current = settings;
        var tmp = _paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        if (File.Exists(_paths.SettingsFile))
        {
            File.Replace(tmp, _paths.SettingsFile, null);
        }
        else
        {
            File.Move(tmp, _paths.SettingsFile);
        }
    }
}
