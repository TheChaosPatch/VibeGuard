using System.ComponentModel;
using GuardCode.Content;
using GuardCode.Content.Services;
using ModelContextProtocol.Server;

namespace GuardCode.Mcp.Tools;

/// <summary>
/// MCP tool handler for the <c>prep</c> tool. Thin translator:
/// parses the language wire-string (framework is passed through
/// unchanged for forward compatibility), delegates to
/// <see cref="IPrepService"/>, and returns a serializable shape.
/// All scoring, filtering, and content lookup happens in the service.
/// </summary>
// internal: CA1515 under AllEnabledByDefault; the MCP SDK discovers tool
// types by attribute via reflection, not by visibility (see WithToolsFromAssembly).
[McpServerToolType]
internal static class PrepTool
{
    [McpServerTool(Name = "prep")]
    [Description(
        "Discover which GuardCode archetypes are relevant to an upcoming task. " +
        "Call this before writing a function or class: pass a natural-language " +
        "description of what you are about to build and the target language, " +
        "and receive up to 8 ranked archetype identifiers to consult().")]
    public static PrepToolResponse Run(
        IPrepService service,
        [Description("Free-text description of what you are about to write. Max 2000 chars.")] string intent,
        [Description("Target language. One of: csharp, python, c, go.")] string language,
        [Description("Optional framework hint. Accepted for forward compatibility; not used for filtering in MVP.")] string? framework = null)
    {
        if (!SupportedLanguageExtensions.TryParseWire(language, out var parsedLanguage))
        {
            return PrepToolResponse.ErrorResponse(
                $"language '{language}' is not supported. Expected one of: csharp, python, c, go.");
        }

        try
        {
            var result = service.Prep(intent, parsedLanguage, framework);
            var matches = new List<PrepToolMatch>(result.Matches.Count);
            foreach (var match in result.Matches)
            {
                matches.Add(new PrepToolMatch(
                    Archetype: match.ArchetypeId,
                    Title: match.Title,
                    Summary: match.Summary,
                    Score: match.Score));
            }
            return new PrepToolResponse(matches, Error: null);
        }
        catch (ArgumentException ex)
        {
            return PrepToolResponse.ErrorResponse(ex.Message);
        }
    }
}

internal sealed record PrepToolMatch(string Archetype, string Title, string Summary, double Score);

internal sealed record PrepToolResponse(
    IReadOnlyList<PrepToolMatch> Matches,
    string? Error)
{
    public static PrepToolResponse ErrorResponse(string message)
        => new([], message);
}
