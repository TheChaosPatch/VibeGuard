namespace VibeGuard.Content.Services;

/// <summary>
/// Entry point for consult queries. Validates input, dispatches to
/// <see cref="VibeGuard.Content.Indexing.IArchetypeIndex"/> for lookup,
/// and returns one of three response shapes (normal, redirect, not-found)
/// per design spec §3.2.
/// </summary>
public interface IConsultationService
{
    /// <summary>
    /// Returns guidance for a specific archetype in the requested language.
    /// </summary>
    /// <param name="archetypeId">
    /// The archetype identifier. Must match <c>^[a-z0-9\-]+(/[a-z0-9\-]+)*$</c>.
    /// </param>
    /// <param name="language">
    /// The target programming language as a wire-form identifier
    /// (e.g. <c>"python"</c>, <c>"rust"</c>). Must be a member of the
    /// configured <see cref="SupportedLanguageSet"/>.
    /// </param>
    ConsultResult Consult(string archetypeId, string language);
}
