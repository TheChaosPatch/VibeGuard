namespace VibeGuard.Content;

/// <summary>
/// An archetype aggregate: its identifier, principles file, and
/// all available language files keyed by wire-form language string.
/// Constructed once at startup by <c>Loading.ArchetypeLoader</c>
/// and never mutated afterwards.
/// </summary>
public sealed record Archetype(
    string Id,
    PrinciplesFrontmatter Principles,
    string PrinciplesBody,
    IReadOnlyDictionary<string, LanguageFile> LanguageFiles);
