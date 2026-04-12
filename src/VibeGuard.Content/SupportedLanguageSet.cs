using System.Collections;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace VibeGuard.Content;

/// <summary>
/// Immutable set of wire-form language identifiers that the server
/// accepts on the <c>prep</c> and <c>consult</c> tools and enforces
/// at archetype load time. Replaces the closed <c>SupportedLanguage</c>
/// enum from the MVP so that adding a language becomes a content and
/// config change rather than a code change.
/// </summary>
/// <remarks>
/// The set is resolved once at startup from
/// <c>VIBEGUARD_SUPPORTED_LANGUAGES</c> (env, comma-separated) →
/// <c>VibeGuard:SupportedLanguages</c> (appsettings.json, string array) →
/// <see cref="Default"/>. The resolved instance is injected as a singleton
/// into the loader and the services, where it is the single source of
/// truth for what "supported" means.
/// <para>
/// Wire names are lowercase ASCII identifiers; the same string is used
/// in <c>applies_to</c> frontmatter, language filename stems
/// (<c>rust.md</c>), and the MCP tool arguments. Uppercase, whitespace,
/// and punctuation other than <c>-</c> are rejected at construction so
/// a typo in config fails at startup with a clear diagnostic rather
/// than silently widening what the index accepts.
/// </para>
/// </remarks>
public sealed partial class SupportedLanguageSet : IReadOnlyCollection<string>
{
    /// <summary>Maximum length of a single wire-form language identifier.</summary>
    public const int MaxWireLength = 32;

    /// <summary>
    /// The canonical default language set, used when neither the env
    /// variable nor the configuration key is set. Includes <c>rust</c>
    /// from day one as the proof that the set is genuinely open: if the
    /// code, the loader, the services, and the corpus round-trip a fifth
    /// language end-to-end, a sixth is a content-only change.
    /// </summary>
    private static readonly string[] DefaultWireNames =
        ["csharp", "python", "c", "go", "rust", "javascript", "typescript", "java", "kotlin", "swift", "ruby", "php"];

    private readonly FrozenSet<string> _wireNames;
    private readonly string _sortedList;

    /// <summary>
    /// Construct a set from the given wire-form language identifiers.
    /// Each entry must match <c>^[a-z][a-z0-9\-]*$</c> and be no longer
    /// than <see cref="MaxWireLength"/> characters. Duplicates are
    /// collapsed. An empty set is rejected — the server must accept at
    /// least one language or every call would fail.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="wireNames"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// Any entry is null, fails the wire-name regex, exceeds the length
    /// cap, or the final set is empty after deduplication.
    /// </exception>
    public SupportedLanguageSet(IEnumerable<string> wireNames)
    {
        ArgumentNullException.ThrowIfNull(wireNames);

        var accumulator = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in wireNames)
        {
            if (raw is null)
            {
                throw new ArgumentException(
                    "supported language entry must not be null",
                    nameof(wireNames));
            }
            if (raw.Length == 0 || raw.Length > MaxWireLength)
            {
                throw new ArgumentException(
                    $"supported language '{raw}' must be 1..{MaxWireLength} characters",
                    nameof(wireNames));
            }
            if (!WireNameRegex().IsMatch(raw))
            {
                throw new ArgumentException(
                    $"supported language '{raw}' must match ^[a-z][a-z0-9\\-]*$",
                    nameof(wireNames));
            }
            accumulator.Add(raw);
        }

        if (accumulator.Count == 0)
        {
            throw new ArgumentException(
                "supported language set must contain at least one entry",
                nameof(wireNames));
        }

        _wireNames = accumulator.ToFrozenSet(StringComparer.Ordinal);
        // Pre-compute the sorted comma-joined form once so every error
        // message can stamp the configured set without re-sorting.
        _sortedList = string.Join(
            ", ",
            accumulator.OrderBy(static s => s, StringComparer.Ordinal));
    }

    /// <summary>The canonical default set shipped with VibeGuard.</summary>
    public static SupportedLanguageSet Default() => new(DefaultWireNames);

    /// <summary>Number of languages in the set.</summary>
    public int Count => _wireNames.Count;

    /// <summary>
    /// Returns <c>true</c> if <paramref name="wire"/> is in the set.
    /// Comparison is ordinal and case-sensitive: wire names are canonical
    /// lowercase by construction, and the tool layer normalizes user
    /// input before calling here.
    /// </summary>
    public bool Contains(string? wire) =>
        wire is not null && _wireNames.Contains(wire);

    /// <summary>
    /// Sorted, comma-separated list of the wire names — suitable for
    /// tool error messages. Ordering is ordinal so the output is stable
    /// across runs.
    /// </summary>
    public string ToSortedList() => _sortedList;

    /// <inheritdoc/>
    public IEnumerator<string> GetEnumerator() => _wireNames.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [GeneratedRegex(@"^[a-z][a-z0-9\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex WireNameRegex();
}
