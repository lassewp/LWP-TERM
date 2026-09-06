using System;
using System.IO;
using FluentAssertions;
using LwpTerm.Core;

namespace LwpTerm.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void Portable_places_all_files_under_a_data_folder_next_to_the_exe()
    {
        var tempExeDir = Path.Combine(Path.GetTempPath(), "lwpterm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempExeDir);
        try
        {
            var paths = AppPaths.Portable(tempExeDir);

            paths.Root.Should().Be(Path.Combine(tempExeDir, "data"));
            Directory.Exists(paths.Root).Should().BeTrue();
            Directory.Exists(paths.LogsDirectory).Should().BeTrue();
            paths.SessionsFile.Should().Be(Path.Combine(tempExeDir, "data", "sessions.json"));
            paths.LayoutFile.Should().Be(Path.Combine(tempExeDir, "data", "layout.xml"));
        }
        finally
        {
            Directory.Delete(tempExeDir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_picks_portable_when_marker_file_present()
    {
        var tempExeDir = Path.Combine(Path.GetTempPath(), "lwpterm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempExeDir);
        try
        {
            File.WriteAllText(Path.Combine(tempExeDir, "portable.txt"), "");

            var paths = AppPaths.Resolve(tempExeDir);

            paths.Root.Should().Be(Path.Combine(tempExeDir, "data"));
        }
        finally
        {
            Directory.Delete(tempExeDir, recursive: true);
        }
    }

    [Fact]
    public void ForCurrentUser_lives_under_AppData()
    {
        var paths = AppPaths.ForCurrentUser();

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        paths.Root.Should().Be(Path.Combine(appData, AppPaths.AppFolderName));
    }
}
