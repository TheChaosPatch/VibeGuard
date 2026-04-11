namespace VibeGuard.Content.Loading;

/// <summary>
/// Thrown when a markdown file cannot be parsed into a valid frontmatter + body pair.
/// Covers: missing frontmatter delimiters, malformed YAML, unknown frontmatter fields,
/// type mismatches. Callers surface the filename; this type only carries the "what".
/// </summary>
public sealed class FrontmatterParseException : Exception
{
    public FrontmatterParseException() { }
    public FrontmatterParseException(string message) : base(message) { }
    public FrontmatterParseException(string message, Exception inner) : base(message, inner) { }
}
