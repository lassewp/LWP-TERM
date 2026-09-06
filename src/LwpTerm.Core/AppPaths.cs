using System;
using System.IO;

namespace LwpTerm.Core;

/// <summary>
/// Resolves and creates the on-disk locations LWP-TERM uses for configuration,
/// session storage and logs. Supports a portable mode where every file lives
/// next to the executable instead of under %APPDATA%.
/// </summary>
public sealed class AppPaths
{
    public const string AppFolderName = "LwpTerm";

    private AppPaths(string root)
    {
        Root = root;
        LogsDirectory = Path.Combine(root, "logs");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>Base directory that holds every LWP-TERM file.</summary>
    public string Root { get; }

    public string LogsDirectory { get; }

    public string SessionsFile => Path.Combine(Root, "sessions.json");

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LayoutFile => Path.Combine(Root, "layout.xml");

    public string KnownHostsFile => Path.Combine(Root, "known_hosts");

    /// <summary>
    /// Standard per-user location: <c>%APPDATA%\LwpTerm</c>.
    /// </summary>
    public static AppPaths ForCurrentUser()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new AppPaths(Path.Combine(appData, AppFolderName));
    }

    /// <summary>
    /// Portable location: a <c>data</c> folder next to the running executable.
    /// Selected when a <c>portable.txt</c> marker file sits beside the exe.
    /// </summary>
    public static AppPaths Portable(string executableDirectory)
        => new(Path.Combine(executableDirectory, "data"));

    /// <summary>
    /// Picks portable mode when a <c>portable.txt</c> marker exists next to the
    /// executable, otherwise the per-user AppData location.
    /// </summary>
    public static AppPaths Resolve(string executableDirectory)
    {
        var marker = Path.Combine(executableDirectory, "portable.txt");
        return File.Exists(marker) ? Portable(executableDirectory) : ForCurrentUser();
    }
}
