using System.Collections.Immutable;
using System.ComponentModel;
using GuardCode.Content;
using GuardCode.Content.Services;
using ModelContextProtocol.Server;

namespace GuardCode.Mcp.Tools;

/// <summary>
/// MCP tool handler for the <c>consult</c> tool. Translates MCP
/// arguments to service input, then reshapes the <see cref="ConsultResult"/>
/// into a wire format that matches design spec §3.2.
/// </summary>
// internal: CA1515 under AllEnabledByDefault; the MCP SDK discovers tool
// types by attribute via reflection, not by visibility (see WithToolsFromAssembly).
[McpServerToolType]
internal static class ConsultTool
{
    [McpServerTool(Name = "consult")]
    [Description(
        "Retrieve the full guidance document for one GuardCode archetype. " +
        "Returns principles plus language-specific implementation guidance " +
        "as a single composed markdown document. If the archetype does not " +
        "apply to the requested language, returns a redirect with a suggested " +
        "alternative when available.")]
    public static ConsultToolResponse Run(
        IConsultationService service,
        [Description("Archetype identifier, e.g. 'auth/password-hashing'.")] string archetype,
        [Description("Target language. One of: csharp, python, c, go.")] string language)
    {
        if (!SupportedLanguageExtensions.TryParseWire(language, out var parsedLanguage))
        {
            return ConsultToolResponse.ErrorResponse(
                archetype,
                language,
                $"language '{language}' is not supported. Expected one of: csharp, python, c, go.");
        }

        try
        {
            var result = service.Consult(archetype, parsedLanguage);
            return new ConsultToolResponse(
                Archetype: result.Archetype,
                Language: result.Language,
                Content: result.Content,
                Redirect: result.Redirect,
                NotFound: result.NotFound,
                Message: result.Message,
                Suggested: result.Suggested,
                RelatedArchetypes: result.RelatedArchetypes,
                References: result.References,
                Error: null);
        }
        catch (ArgumentException ex)
        {
            return ConsultToolResponse.ErrorResponse(archetype, language, ex.Message);
        }
    }
}

internal sealed record ConsultToolResponse(
    string Archetype,
    string Language,
    string? Content,
    bool Redirect,
    bool NotFound,
    string? Message,
    IReadOnlyList<string> Suggested,
    IReadOnlyList<string> RelatedArchetypes,
    IReadOnlyDictionary<string, string> References,
    string? Error)
{
    public static ConsultToolResponse ErrorResponse(string archetype, string language, string error)
        => new(
            Archetype: archetype,
            Language: language,
            Content: null,
            Redirect: false,
            NotFound: false,
            Message: null,
            Suggested: [],
            RelatedArchetypes: [],
            References: ImmutableDictionary<string, string>.Empty,
            Error: error);
}
