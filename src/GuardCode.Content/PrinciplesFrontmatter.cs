using System.Collections.Frozen;

namespace GuardCode.Content;

/// <summary>
/// Typed YAML frontmatter for an archetype's <c>_principles.md</c> file.
/// Fields map 1:1 to design spec §4.1. Immutable record projected from
/// a file-scoped mutable DTO inside <c>Loading.FrontmatterParser</c>
/// after strict YamlDotNet deserialization.
/// </summary>
public sealed record PrinciplesFrontmatter
{
    public int SchemaVersion { get; init; }
    public string Archetype { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> AppliesTo { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public IReadOnlyList<string> RelatedArchetypes { get; init; } = [];
    public IReadOnlyDictionary<string, string> EquivalentsIn { get; init; } = FrozenDictionary<string, string>.Empty;
    public IReadOnlyDictionary<string, string> References { get; init; } = FrozenDictionary<string, string>.Empty;

    // ------- Lifecycle metadata (see ArchetypeStatus for rationale) -------

    /// <summary>
    /// Lifecycle stage. Defaults to <see cref="ArchetypeStatus.Stable"/>
    /// so direct record construction in unit tests can stay terse; YAML
    /// parsing requires the field explicitly and throws on absence.
    /// </summary>
    public ArchetypeStatus Status { get; init; } = ArchetypeStatus.Stable;

    /// <summary>
    /// Handle or name of the archetype's original author. Required for
    /// <see cref="ArchetypeStatus.Stable"/>; optional for drafts.
    /// </summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Handles of everyone who has signed off on the current content.
    /// Required non-empty for <see cref="ArchetypeStatus.Stable"/>.
    /// Drafts may ship with an empty list.
    /// </summary>
    public IReadOnlyList<string> ReviewedBy { get; init; } = [];

    /// <summary>
    /// Calendar date the archetype was promoted to stable
    /// (ISO 8601, <c>YYYY-MM-DD</c>). Required for stable archetypes,
    /// null otherwise.
    /// </summary>
    public string? StableSince { get; init; }

    /// <summary>
    /// Archetype ID that replaces this one. Required when
    /// <see cref="Status"/> is <see cref="ArchetypeStatus.Deprecated"/>,
    /// forbidden otherwise. The successor is not required to exist at
    /// validation time — broken links fail at <c>consult</c> resolution,
    /// which keeps deprecation lightweight.
    /// </summary>
    public string? SupersededBy { get; init; }
}
