namespace VibeGuard.Content.Services;

/// <summary>
/// Typed response from <see cref="IConsultationService.Consult"/>.
/// Three shapes share one DTO, keyed by the nullable fields, so the
/// MCP tool layer can serialize whichever wire shape applies. See
/// design spec §3.2 for the three shapes (normal, redirect, not-found).
/// </summary>
public sealed record ConsultResult(
    string Archetype,
    string Language,
    string? Content,
    IReadOnlyList<string> RelatedArchetypes,
    IReadOnlyDictionary<string, string> References,
    bool Redirect,
    string? Message,
    IReadOnlyList<string> Suggested,
    bool NotFound);
