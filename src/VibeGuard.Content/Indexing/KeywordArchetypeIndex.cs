using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;

namespace VibeGuard.Content.Indexing;

/// <summary>
/// Builds three frozen data structures once from the loaded corpus and
/// answers all runtime lookups from memory. The implementation is
/// deliberately keyword-based with deterministic scoring: no embeddings,
/// no fuzzy matching — those are deferred per design spec §10.
/// </summary>
public sealed class KeywordArchetypeIndex : IArchetypeIndex
{
    // Stopwords are matched case-insensitively via the frozen set's comparer,
    // so the tokenizer can stay allocation-light and skip case folding.
    private static readonly FrozenSet<string> Stopwords = new[]
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else",
        "of", "in", "on", "at", "to", "for", "with", "from", "by",
        "is", "are", "was", "were", "be", "been", "being",
        "i", "im", "my", "we", "you", "your",
        "how", "do", "does", "about", "want", "need",
        "this", "that", "these", "those",
        "it", "its", "as", "so"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly FrozenDictionary<string, Archetype> _byId;
    private readonly FrozenDictionary<string, FrozenSet<string>> _keywordIndex;
    private readonly FrozenDictionary<string, ImmutableArray<string>> _reverseRelated;

    private KeywordArchetypeIndex(
        FrozenDictionary<string, Archetype> byId,
        FrozenDictionary<string, FrozenSet<string>> keywordIndex,
        FrozenDictionary<string, ImmutableArray<string>> reverseRelated)
    {
        _byId = byId;
        _keywordIndex = keywordIndex;
        _reverseRelated = reverseRelated;
    }

    public static KeywordArchetypeIndex Build(IReadOnlyList<Archetype> archetypes)
    {
        ArgumentNullException.ThrowIfNull(archetypes);
        var byId = BuildByIdIndex(archetypes);
        var keywordIndex = BuildKeywordIndex(archetypes);
        var reverseRelated = BuildReverseRelatedIndex(archetypes);
        return new KeywordArchetypeIndex(byId, keywordIndex, reverseRelated);
    }

    public Archetype? GetById(string archetypeId)
        => _byId.TryGetValue(archetypeId, out var a) ? a : null;

    public IReadOnlyList<string> GetReverseRelated(string archetypeId)
        => _reverseRelated.TryGetValue(archetypeId, out var list) ? list : ImmutableArray<string>.Empty;

    public IReadOnlyList<PrepMatch> Search(string intent, string language, int maxResults)
    {
        ArgumentNullException.ThrowIfNull(language);
        if (maxResults <= 0) return Array.Empty<PrepMatch>();
        var tokens = Tokenize(intent);
        if (tokens.Count == 0) return Array.Empty<PrepMatch>();

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        AccumulateKeywordScores(tokens, scores);
        AccumulateTitleSummaryBonus(tokens, scores);

        var divisor = tokens.Count * 1.5;
        var hits = new List<PrepMatch>(scores.Count);
        foreach (var (id, raw) in scores)
        {
            if (raw <= 0) continue;
            var archetype = _byId[id];
            if (!AppliesToLanguage(archetype, language)) continue;
            var normalized = Math.Min(1.0, raw / divisor);
            hits.Add(new PrepMatch(id, archetype.Principles.Title, archetype.Principles.Summary, normalized));
        }

        hits.Sort(CompareHits);
        return hits.Count <= maxResults ? hits : hits.GetRange(0, maxResults);
    }

    private static int CompareHits(PrepMatch a, PrepMatch b)
    {
        var byScore = b.Score.CompareTo(a.Score);
        return byScore != 0 ? byScore : StringComparer.Ordinal.Compare(a.ArchetypeId, b.ArchetypeId);
    }

    /// <summary>
    /// Returns true when an archetype is relevant for the given language.
    /// Archetypes with <c>applies_to: [all]</c> match every language —
    /// this supports principles-only (language-agnostic) archetypes.
    /// </summary>
    private static bool AppliesToLanguage(Archetype archetype, string language)
        => archetype.Principles.AppliesTo.Contains("all", StringComparer.OrdinalIgnoreCase)
        || archetype.Principles.AppliesTo.Contains(language, StringComparer.Ordinal);

    private static FrozenDictionary<string, Archetype> BuildByIdIndex(IReadOnlyList<Archetype> archetypes)
    {
        var map = new Dictionary<string, Archetype>(archetypes.Count, StringComparer.Ordinal);
        foreach (var archetype in archetypes)
        {
            map[archetype.Id] = archetype;
        }
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, FrozenSet<string>> BuildKeywordIndex(IReadOnlyList<Archetype> archetypes)
    {
        var accumulator = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var archetype in archetypes)
        {
            foreach (var keyword in archetype.Principles.Keywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                if (!accumulator.TryGetValue(keyword, out var bucket))
                {
                    bucket = new HashSet<string>(StringComparer.Ordinal);
                    accumulator[keyword] = bucket;
                }
                bucket.Add(archetype.Id);
            }
        }
        var frozen = new Dictionary<string, FrozenSet<string>>(accumulator.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (keyword, bucket) in accumulator)
        {
            frozen[keyword] = bucket.ToFrozenSet(StringComparer.Ordinal);
        }
        return frozen.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, ImmutableArray<string>> BuildReverseRelatedIndex(
        IReadOnlyList<Archetype> archetypes)
    {
        var accumulator = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var source in archetypes)
        {
            foreach (var target in source.Principles.RelatedArchetypes)
            {
                if (string.IsNullOrWhiteSpace(target)) continue;
                if (!accumulator.TryGetValue(target, out var bucket))
                {
                    bucket = new SortedSet<string>(StringComparer.Ordinal);
                    accumulator[target] = bucket;
                }
                bucket.Add(source.Id);
            }
        }
        var frozen = new Dictionary<string, ImmutableArray<string>>(accumulator.Count, StringComparer.Ordinal);
        foreach (var (target, sources) in accumulator)
        {
            frozen[target] = [.. sources];
        }
        return frozen.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private void AccumulateKeywordScores(IReadOnlyList<string> tokens, Dictionary<string, double> scores)
    {
        foreach (var token in tokens)
        {
            if (!_keywordIndex.TryGetValue(token, out var ids)) continue;
            foreach (var id in ids)
            {
                scores.TryGetValue(id, out var current);
                scores[id] = current + 1.0;
            }
        }
    }

    private void AccumulateTitleSummaryBonus(IReadOnlyList<string> tokens, Dictionary<string, double> scores)
    {
        foreach (var (id, archetype) in _byId)
        {
            var haystack = archetype.Principles.Title + " " + archetype.Principles.Summary;
            var matches = 0;
            foreach (var token in tokens)
            {
                if (haystack.Contains(token, StringComparison.OrdinalIgnoreCase)) matches++;
            }
            if (matches == 0) continue;
            scores.TryGetValue(id, out var current);
            scores[id] = current + (matches * 0.5);
        }
    }

    private static List<string> Tokenize(string intent)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(intent)) return result;
        var buffer = new StringBuilder();
        foreach (var ch in intent)
        {
            if (char.IsLetterOrDigit(ch)) { buffer.Append(ch); continue; }
            FlushToken(buffer, result);
        }
        FlushToken(buffer, result);
        return result;
    }

    private static void FlushToken(StringBuilder buffer, List<string> result)
    {
        if (buffer.Length < 2) { buffer.Clear(); return; }
        var token = buffer.ToString();
        buffer.Clear();
        if (Stopwords.Contains(token)) return;
        result.Add(token);
    }
}
