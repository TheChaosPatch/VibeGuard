namespace GuardCode.Content.Indexing;

/// <summary>
/// In-memory index over the loaded archetype corpus. One instance lives
/// for the lifetime of the process; all lookups are pure reads with no
/// I/O and no allocation on the hot path beyond the returned projections.
/// </summary>
public interface IArchetypeIndex
{
    /// <summary>
    /// Returns up to <paramref name="maxResults"/> archetypes whose
    /// keywords, title, or summary match the intent, filtered by the
    /// requested language's <c>applies_to</c> membership.
    /// </summary>
    IReadOnlyList<PrepMatch> Search(string intent, SupportedLanguage language, int maxResults);

    /// <summary>O(1) lookup by archetype ID. Returns <c>null</c> if unknown.</summary>
    Archetype? GetById(string archetypeId);

    /// <summary>
    /// Returns the archetype IDs that list <paramref name="archetypeId"/> in
    /// their own <c>related_archetypes</c> frontmatter. Used to expose the
    /// bidirectional related-archetypes view required by design spec §3.2.
    /// </summary>
    IReadOnlyList<string> GetReverseRelated(string archetypeId);
}
