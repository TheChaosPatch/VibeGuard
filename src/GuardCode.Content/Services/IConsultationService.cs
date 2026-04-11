namespace GuardCode.Content.Services;

/// <summary>
/// Entry point for consult queries. Validates input, dispatches to
/// <see cref="GuardCode.Content.Indexing.IArchetypeIndex"/> for lookup,
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
    /// <param name="language">The target programming language.</param>
    ConsultResult Consult(string archetypeId, SupportedLanguage language);
}
