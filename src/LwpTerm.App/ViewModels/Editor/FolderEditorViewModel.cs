using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using LwpTerm.Core.Sessions;

namespace LwpTerm.App.ViewModels.Editor;

public sealed record FolderIconChoice(string? Glyph, string Label);

public sealed record FolderWeightChoice(FolderLabelWeight Value, string Label);

/// <summary>Backs <c>FolderEditorWindow</c> — name, accent colour, icon, label weight and notes for a folder.</summary>
public sealed partial class FolderEditorViewModel : ObservableObject
{
    private readonly SessionFolder _folder;

    public FolderEditorViewModel(SessionFolder folder, bool isTopLevel)
    {
        _folder = folder;
        IsTopLevel = isTopLevel;

        _name = folder.Name;
        _colorHex = folder.ColorHex ?? string.Empty;
        _notes = folder.Notes ?? string.Empty;

        IconChoices = new[]
        {
            new FolderIconChoice(null, "Default"),
            new FolderIconChoice("", "Folder"),
            new FolderIconChoice("", "Documents"),
            new FolderIconChoice("", "Cloud"),
            new FolderIconChoice("", "Globe"),
            new FolderIconChoice("", "Network"),
            new FolderIconChoice("", "Machine"),
            new FolderIconChoice("", "Shield"),
            new FolderIconChoice("", "Lock"),
            new FolderIconChoice("", "Home"),
            new FolderIconChoice("", "Star"),
            new FolderIconChoice("", "Location"),
        };
        _selectedIcon = IconChoices.FirstOrDefault(c => c.Glyph == folder.IconGlyph) ?? IconChoices[0];

        WeightChoices = new[]
        {
            new FolderWeightChoice(FolderLabelWeight.Auto,
                isTopLevel ? "Auto (bold — top level)" : "Auto (regular — nested)"),
            new FolderWeightChoice(FolderLabelWeight.Bold, "Bold"),
            new FolderWeightChoice(FolderLabelWeight.Normal, "Regular"),
        };
        _selectedWeight = WeightChoices.First(c => c.Value == folder.LabelWeight);
    }

    public bool IsTopLevel { get; }

    public IReadOnlyList<FolderIconChoice> IconChoices { get; }

    public IReadOnlyList<FolderWeightChoice> WeightChoices { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _colorHex;
    [ObservableProperty] private string _notes;
    [ObservableProperty] private FolderIconChoice _selectedIcon;
    [ObservableProperty] private FolderWeightChoice _selectedWeight;

    public bool Saved { get; private set; }

    public void Save()
    {
        _folder.Name = string.IsNullOrWhiteSpace(Name) ? _folder.Name : Name.Trim();
        _folder.ColorHex = string.IsNullOrWhiteSpace(ColorHex) ? null : ColorHex.Trim();
        _folder.IconGlyph = SelectedIcon?.Glyph;
        _folder.LabelWeight = SelectedWeight?.Value ?? FolderLabelWeight.Auto;
        _folder.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes;
        Saved = true;
    }
}
