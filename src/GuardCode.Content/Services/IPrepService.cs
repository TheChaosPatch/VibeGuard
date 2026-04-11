namespace GuardCode.Content.Services;

/// <summary>
/// Entry point for prep queries. Validates input and delegates to
/// <see cref="GuardCode.Content.Indexing.IArchetypeIndex"/> for matching.
/// </summary>
public interface IPrepService
{
    /// <summary>
    /// Returns archetype matches for a given coding intent.
    /// </summary>
    /// <param name="intent">
    /// The developer's stated coding intent. Must be 1–2000 characters.
    /// </param>
    /// <param name="language">The target programming language.</param>
    /// <param name="framework">
    /// Optional framework hint. Accepted for forward-compatibility but
    /// not used for filtering in MVP per spec §3.1.
    /// </param>
    PrepResult Prep(string intent, SupportedLanguage language, string? framework);
}
