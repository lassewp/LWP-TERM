using System;
using CommunityToolkit.Mvvm.ComponentModel;
using LwpTerm.App.Services;

namespace LwpTerm.App.ViewModels.FileBrowser;

public sealed partial class FileEntryViewModel : ObservableObject
{
    public FileEntryViewModel(FileNode node) => Node = node;

    public FileNode Node { get; }

    public string Name => Node.Name;

    public bool IsDirectory => Node.IsDirectory;

    [ObservableProperty]
    private bool _isSelected;

    public string Glyph => Node.IsDirectory ? "" : ""; // Folder : Document

    public string SizeText => Node.IsDirectory ? "" : FormatBytes(Node.Size);

    public string ModifiedText => Node.Modified == DateTimeOffset.MinValue
        ? ""
        : Node.Modified.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    public string Permissions => Node.Permissions ?? "";

    /// <summary>Sort key: directories first, then case-insensitive name.</summary>
    public (int, string) SortKey => (Node.IsDirectory ? 0 : 1, Node.Name.ToLowerInvariant());

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
