using VibeGuard.Content.Indexing;

namespace VibeGuard.Content.Services;

/// <summary>
/// Answers <c>prep</c> queries. Thin wrapper over
/// <see cref="IArchetypeIndex"/> that owns input validation and
/// caps result count per spec §3.1 (max 8 matches).
/// </summary>
public sealed class PrepService(IArchetypeIndex index) : IPrepService
{
    /// <summary>Maximum allowed length for the <c>intent</c> parameter.</summary>
    public const int MaxIntentLength = 2000;

    /// <summary>Maximum number of archetype matches returned per query.</summary>
    public const int MaxResults = 8;

    /// <inheritdoc/>
    public PrepResult Prep(string intent, SupportedLanguage language, string? framework)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (intent.Length == 0)
        {
            throw new ArgumentException("intent must be non-empty", nameof(intent));
        }

        if (intent.Length > MaxIntentLength)
        {
            throw new ArgumentException(
                $"intent must be {MaxIntentLength} characters or fewer (got {intent.Length})",
                nameof(intent));
        }

        // framework is accepted for forward-compatibility per spec §3.1
        // but is not used for filtering in MVP.
        _ = framework;

        var matches = index.Search(intent, language, MaxResults);
        return new PrepResult(matches);
    }
}
