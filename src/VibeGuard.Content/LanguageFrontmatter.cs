using System.Collections.Frozen;

namespace VibeGuard.Content;

/// <summary>
/// Typed YAML frontmatter for an archetype's language file
/// (<c>csharp.md</c>, <c>python.md</c>, etc.). See design spec §4.2.
/// Immutable record projected from a file-scoped mutable DTO inside
/// <c>Loading.FrontmatterParser</c> after strict YamlDotNet deserialization.
/// </summary>
public sealed record LanguageFrontmatter
{
    public int SchemaVersion { get; init; }
    public string Archetype { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string? Framework { get; init; }
    public string PrinciplesFile { get; init; } = string.Empty;
    public LibrariesSection Libraries { get; init; } = new();
    public IReadOnlyDictionary<string, string> MinimumVersions { get; init; } = FrozenDictionary<string, string>.Empty;
}

public sealed record LibrariesSection
{
    public string Preferred { get; init; } = string.Empty;
    public IReadOnlyList<string> Acceptable { get; init; } = [];
    public IReadOnlyList<AvoidedLibrary> Avoid { get; init; } = [];
}

public sealed record AvoidedLibrary
{
    public string Name { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
