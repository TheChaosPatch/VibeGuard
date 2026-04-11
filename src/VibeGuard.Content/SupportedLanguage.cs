namespace VibeGuard.Content;

/// <summary>
/// The MVP language set for VibeGuard per design spec §7.1.
/// Deliberately excludes JavaScript and Java — VibeGuard targets
/// backend and systems engineers.
/// </summary>
public enum SupportedLanguage
{
    CSharp,
    Python,
    C,
    Go
}

public static class SupportedLanguageExtensions
{
    /// <summary>
    /// Returns the canonical lowercase wire form used in frontmatter,
    /// filenames, and the MCP tool contract ("csharp", "python", "c", "go").
    /// </summary>
    public static string ToWireString(this SupportedLanguage language) => language switch
    {
        SupportedLanguage.CSharp => "csharp",
        SupportedLanguage.Python => "python",
        SupportedLanguage.C => "c",
        SupportedLanguage.Go => "go",
        _ => throw new System.ArgumentOutOfRangeException(nameof(language), language, null)
    };

    /// <summary>
    /// Parses the wire form back to the enum. Returns false for anything
    /// not in the MVP set — caller decides how to surface the error.
    /// </summary>
    public static bool TryParseWire(string? value, out SupportedLanguage language)
    {
        switch (value)
        {
            case "csharp": language = SupportedLanguage.CSharp; return true;
            case "python": language = SupportedLanguage.Python; return true;
            case "c": language = SupportedLanguage.C; return true;
            case "go": language = SupportedLanguage.Go; return true;
            default: language = default; return false;
        }
    }
}
