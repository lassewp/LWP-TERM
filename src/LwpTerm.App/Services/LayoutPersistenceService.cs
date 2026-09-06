using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AvalonDock;
using AvalonDock.Layout.Serialization;
using LwpTerm.Core;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.Services;

public interface ILayoutPersistenceService
{
    void Save(DockingManager manager);
    bool TryLoad(DockingManager manager);

    /// <summary>Serialise the current layout to a string (for capturing a pristine default).</summary>
    string Capture(DockingManager manager);

    /// <summary>Apply a layout previously produced by <see cref="Capture"/>.</summary>
    void Apply(DockingManager manager, string layoutXml);

    void SaveNamed(DockingManager manager, string name);
    bool ApplyNamed(DockingManager manager, string name);
    IReadOnlyList<string> ListNamed();
    void DeleteNamed(string name);
}

/// <summary>
/// Persists the AvalonDock layout (pane sizes, float/dock state, hidden panels)
/// to <c>layout.xml</c>, plus named user layouts under <c>layouts\</c>. Dynamic
/// document tabs are never restored — only the anchorable panels and geometry.
/// </summary>
public sealed class LayoutPersistenceService : ILayoutPersistenceService
{
    private readonly AppPaths _paths;
    private readonly ILogger<LayoutPersistenceService> _log;
    private readonly string _layoutsDir;

    public LayoutPersistenceService(AppPaths paths, ILogger<LayoutPersistenceService> log)
    {
        _paths = paths;
        _log = log;
        _layoutsDir = Path.Combine(paths.Root, "layouts");
    }

    public void Save(DockingManager manager)
    {
        try
        {
            File.WriteAllText(_paths.LayoutFile, Capture(manager));
            _log.LogDebug("Saved dock layout to {Path}", _paths.LayoutFile);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to save dock layout");
        }
    }

    public bool TryLoad(DockingManager manager) =>
        File.Exists(_paths.LayoutFile) && TryApplyFile(manager, _paths.LayoutFile);

    public string Capture(DockingManager manager)
    {
        using var writer = new StringWriter();
        new XmlLayoutSerializer(manager).Serialize(writer);
        return writer.ToString();
    }

    public void Apply(DockingManager manager, string layoutXml)
    {
        var serializer = new XmlLayoutSerializer(manager);
        serializer.LayoutSerializationCallback += CancelDocuments;
        using var reader = new StringReader(layoutXml);
        serializer.Deserialize(reader);
    }

    public void SaveNamed(DockingManager manager, string name)
    {
        Directory.CreateDirectory(_layoutsDir);
        File.WriteAllText(PathFor(name), Capture(manager));
        _log.LogInformation("Saved layout '{Name}'", name);
    }

    public bool ApplyNamed(DockingManager manager, string name)
    {
        var path = PathFor(name);
        return File.Exists(path) && TryApplyFile(manager, path);
    }

    public IReadOnlyList<string> ListNamed()
    {
        if (!Directory.Exists(_layoutsDir))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(_layoutsDir, "*.xml")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n!)
            .ToList();
    }

    public void DeleteNamed(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private bool TryApplyFile(DockingManager manager, string path)
    {
        try
        {
            Apply(manager, File.ReadAllText(path));
            _log.LogDebug("Applied dock layout from {Path}", path);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to apply dock layout {Path}", path);
            return false;
        }
    }

    private static void CancelDocuments(object? sender, LayoutSerializationCallbackEventArgs e)
    {
        if (e.Model is AvalonDock.Layout.LayoutDocument)
        {
            e.Cancel = true;
        }
    }

    private string PathFor(string name)
    {
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
        if (safe.Length == 0)
        {
            safe = "layout";
        }

        return Path.Combine(_layoutsDir, safe + ".xml");
    }
}
