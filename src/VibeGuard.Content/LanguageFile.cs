namespace VibeGuard.Content;

/// <summary>
/// One language-specific guidance file for an archetype:
/// its parsed frontmatter plus the markdown body (frontmatter stripped).
/// </summary>
public sealed record LanguageFile(
    LanguageFrontmatter Frontmatter,
    string Body);
