using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LwpTerm.Core.Sessions;

/// <summary>A node in the saved-session tree: either a <see cref="SessionFolder"/> or a <see cref="SessionItem"/>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "node")]
[JsonDerivedType(typeof(SessionFolder), "folder")]
[JsonDerivedType(typeof(SessionItem), "item")]
public abstract class SessionNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;
}

/// <summary>How a folder's label is weighted in the tree.</summary>
public enum FolderLabelWeight
{
    /// <summary>Bold for top-level folders, regular for nested ones.</summary>
    Auto,
    Bold,
    Normal
}

public sealed class SessionFolder : SessionNode
{
    public bool IsExpanded { get; set; } = true;

    /// <summary>Optional Segoe MDL2 glyph override; null uses the default folder glyph.</summary>
    public string? IconGlyph { get; set; }

    /// <summary>Optional accent colour as <c>#RRGGBB</c>; inherited by child rows that have none.</summary>
    public string? ColorHex { get; set; }

    public FolderLabelWeight LabelWeight { get; set; } = FolderLabelWeight.Auto;

    public string? Notes { get; set; }

    public List<SessionNode> Children { get; set; } = new();
}

public sealed class SessionItem : SessionNode
{
    public ProtocolType Protocol { get; set; } = ProtocolType.Ssh;

    public ConnectionSettings Settings { get; set; } = new SshConnectionSettings();

    /// <summary>Optional Segoe MDL2 glyph override; when null the protocol default is used.</summary>
    public string? IconGlyph { get; set; }

    /// <summary>Optional accent colour as <c>#RRGGBB</c>.</summary>
    public string? ColorHex { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Root document persisted to <c>sessions.json</c>.</summary>
public sealed class SessionTree
{
    public int Version { get; set; } = 1;

    public List<SessionNode> Roots { get; set; } = new();

    public static SessionTree CreateSeed()
    {
        return new SessionTree
        {
            Roots =
            {
                new SessionFolder { Name = "General" }
            }
        };
    }
}
