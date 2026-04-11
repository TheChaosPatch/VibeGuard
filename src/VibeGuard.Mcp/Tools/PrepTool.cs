using System.ComponentModel;
using VibeGuard.Content.Services;
using ModelContextProtocol.Server;

namespace VibeGuard.Mcp.Tools;

/// <summary>
/// MCP tool handler for the <c>prep</c> tool. Thin translator: forwards
/// the wire-string language unchanged (the service validates it against
/// the configured <c>SupportedLanguageSet</c>), forwards the framework
/// hint for forward compatibility, and reshapes the service result into
/// a serializable response. All scoring, filtering, and content lookup
/// happens in the service.
/// </summary>
// internal: CA1515 under AllEnabledByDefault; the MCP SDK discovers tool
// types by attribute via reflection, not by visibility (see WithToolsFromAssembly).
[McpServerToolType]
internal static class PrepTool
{
    [McpServerTool(Name = "prep")]
    [Description(
        "Discover which VibeGuard archetypes are relevant to an upcoming task. " +
        "Call this before writing a function or class: pass a natural-language " +
        "description of what you are about to build and the target language, " +
        "and receive up to 8 ranked archetype identifiers to consult().")]
    public static PrepToolResponse Run(
        IPrepService service,
        [Description("Free-text description of what you are about to write. Max 2000 chars.")] string intent,
        [Description(
            "Target language as a lowercase wire name (e.g. 'csharp', 'python', 'c', 'go', 'rust'). " +
            "The exact set is configured on the server; an unsupported value yields an error " +
            "that lists the currently supported languages.")] string language,
        [Description("Optional framework hint. Accepted for forward compatibility; not used for filtering in MVP.")] string? framework = null)
    {
        try
        {
            var result = service.Prep(intent, language, framework);
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
