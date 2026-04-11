namespace VibeGuard.Content.Indexing;

/// <summary>
/// One scored search hit returned by <see cref="IArchetypeIndex.Search"/>.
/// Scores are normalized to [0,1] by the index; callers should not attempt
/// to compare scores across different queries.
/// </summary>
public sealed record PrepMatch(
    string ArchetypeId,
    string Title,
    string Summary,
    double Score);
