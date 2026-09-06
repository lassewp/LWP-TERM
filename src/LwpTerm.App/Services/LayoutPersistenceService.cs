using System;
using System.IO;
using AvalonDock;
using AvalonDock.Layout.Serialization;
using LwpTerm.Core;
using Microsoft.Extensions.Logging;

namespace LwpTerm.App.Services;

public interface ILayoutPersistenceService
{
    void Save(DockingManager manager);
    bool TryLoad(DockingManager manager);
}

/// <summary>
/// Persists the AvalonDock layout (pane sizes, float/dock state, hidden panels)
/// to <c>layout.xml</c>. Dynamic document tabs are not restored — only the
/// static anchorable panels and pane geometry.
/// </summary>
public sealed class LayoutPersistenceService : ILayoutPersistenceService
{
    private readonly AppPaths _paths;
    private readonly ILogger<LayoutPersistenceService> _log;

    public LayoutPersistenceService(AppPaths paths, ILogger<LayoutPersistenceService> log)
    {
        _paths = paths;
        _log = log;
    }

    public void Save(DockingManager manager)
    {
        try
        {
            var serializer = new XmlLayoutSerializer(manager);
            using var writer = new StreamWriter(_paths.LayoutFile);
            serializer.Serialize(writer);
            _log.LogDebug("Saved dock layout to {Path}", _paths.LayoutFile);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to save dock layout");
        }
    }

    public bool TryLoad(DockingManager manager)
    {
        if (!File.Exists(_paths.LayoutFile))
        {
            return false;
        }

        try
        {
            var serializer = new XmlLayoutSerializer(manager);

            // We do not rehydrate document content from a previous run: cancel any
            // serialised document so AvalonDock keeps only the anchorable layout.
            serializer.LayoutSerializationCallback += (_, e) =>
            {
                if (e.Model is AvalonDock.Layout.LayoutDocument)
                {
                    e.Cancel = true;
                }
            };

            using var reader = new StreamReader(_paths.LayoutFile);
            serializer.Deserialize(reader);
            _log.LogDebug("Restored dock layout from {Path}", _paths.LayoutFile);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load dock layout; using defaults");
            return false;
        }
    }
}
