using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using VibeGuard.Content.Indexing;

namespace VibeGuard.Content.Services;

/// <summary>
/// Answers <c>consult</c> queries per design spec §3.2. Validates the
/// archetype ID at the service boundary as the second line of the
/// path-traversal defense from spec §6.1, then dispatches to one of
/// three response shapes: normal composition, unsupported-language
/// redirect, or archetype-not-found.
/// </summary>
public sealed partial class ConsultationService(IArchetypeIndex index) : IConsultationService
{
    /// <summary>
    /// Separator inserted between the principles body and the language-specific
    /// body in the composed <see cref="ConsultResult.Content"/> field.
    /// </summary>
    public const string BodySeparator = "\n\n---\n\n";

    /// <summary>
    /// Composite format for the deprecation banner prepended to
    /// <c>consult</c> content when the archetype is deprecated. Keeps
    /// the marker stable so LLM clients can pattern-match on it.
    /// Cached as a <see cref="CompositeFormat"/> per CA1863 — the format
    /// string is hot on every deprecated consult call.
    /// </summary>
    private static readonly CompositeFormat DeprecationBannerFormat =
        CompositeFormat.Parse(
            "> **DEPRECATED** — this archetype has been superseded by `{0}`. "
            + "The content below is preserved for reference; new code should "
            + "follow `{0}` instead.\n\n");

    // Zero-allocation sentinel for error-path IReadOnlyDictionary<string,string> returns.
    private static readonly IReadOnlyDictionary<string, string> EmptyReferences =
        ImmutableDictionary<string, string>.Empty;

    [GeneratedRegex(@"^[a-z0-9\-]+(/[a-z0-9\-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchetypeIdRegex();

    /// <inheritdoc/>
    public ConsultResult Consult(string archetypeId, SupportedLanguage language)
    {
        ValidateArchetypeId(archetypeId);

        var wireLanguage = language.ToWireString();
        var archetype = index.GetById(archetypeId);

        if (archetype is null)
        {
            return NotFound(archetypeId, wireLanguage);
        }

        var appliesToLanguage = archetype.Principles.AppliesTo
            .Contains(wireLanguage, StringComparer.Ordinal);

        if (!appliesToLanguage)
        {
            return Redirect(archetype, wireLanguage);
        }

        if (!archetype.LanguageFiles.TryGetValue(wireLanguage, out var languageFile))
        {
            return MissingLanguageFile(archetypeId, wireLanguage);
        }

        return NormalComposition(archetype, wireLanguage, languageFile);
    }

    private static void ValidateArchetypeId(string archetypeId)
    {
        ArgumentNullException.ThrowIfNull(archetypeId);

        if (!ArchetypeIdRegex().IsMatch(archetypeId))
        {
            throw new ArgumentException(
                $"'{archetypeId}' is not a valid identifier. " +
                "Archetype IDs must match ^[a-z0-9\\-]+(/[a-z0-9\\-]+)*$.",
                nameof(archetypeId));
        }
    }

    private static ConsultResult NotFound(string archetypeId, string wireLanguage)
        => new(
            Archetype: archetypeId,
            Language: wireLanguage,
            Content: null,
            RelatedArchetypes: Array.Empty<string>(),
            References: EmptyReferences,
            Redirect: false,
            Message: $"Archetype '{archetypeId}' was not found.",
            Suggested: Array.Empty<string>(),
            NotFound: true);

    private static ConsultResult Redirect(Archetype archetype, string wireLanguage)
    {
        var suggested = new List<string>();
        string message;

        if (archetype.Principles.EquivalentsIn.TryGetValue(wireLanguage, out var equivalent))
        {
            suggested.Add(equivalent);
            message =
                $"Archetype '{archetype.Id}' does not apply to {wireLanguage}. " +
                $"See '{equivalent}' for the equivalent guidance in {wireLanguage}.";
        }
        else
        {
            message =
                $"Archetype '{archetype.Id}' does not apply to {wireLanguage}. " +
                "No direct equivalent is registered; consider searching with prep() for " +
                "a related archetype in this language.";
        }

        return new ConsultResult(
            Archetype: archetype.Id,
            Language: wireLanguage,
            Content: null,
            RelatedArchetypes: Array.Empty<string>(),
            References: EmptyReferences,
            Redirect: true,
            Message: message,
            Suggested: suggested,
            NotFound: false);
    }

    private static ConsultResult MissingLanguageFile(string archetypeId, string wireLanguage)
        => new(
            Archetype: archetypeId,
            Language: wireLanguage,
            Content: null,
            RelatedArchetypes: Array.Empty<string>(),
            References: EmptyReferences,
            Redirect: false,
            Message: $"Archetype '{archetypeId}' lists {wireLanguage} in applies_to " +
                     "but no language file exists on disk.",
            Suggested: Array.Empty<string>(),
            NotFound: true);

    private ConsultResult NormalComposition(
        Archetype archetype,
        string wireLanguage,
        LanguageFile languageFile)
    {
        var body = archetype.PrinciplesBody + BodySeparator + languageFile.Body;
        var content = archetype.Principles.Status == ArchetypeStatus.Deprecated
            && !string.IsNullOrWhiteSpace(archetype.Principles.SupersededBy)
                ? string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    DeprecationBannerFormat,
                    archetype.Principles.SupersededBy) + body
                : body;
        // ^ string.Format(IFormatProvider, CompositeFormat, ...) is the
        // CA1863-preferred overload.

        // Merge forward-declared related archetypes with reverse-related ones
        // (archetypes that list this one in their own frontmatter) per spec §3.2.
        // Concat+Distinct+OrderBy gives deterministic ordinal ordering; Union does not.
        var related = archetype.Principles.RelatedArchetypes
            .Concat(index.GetReverseRelated(archetype.Id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return new ConsultResult(
            Archetype: archetype.Id,
            Language: wireLanguage,
            Content: content,
            RelatedArchetypes: related,
            References: archetype.Principles.References,
            Redirect: false,
            Message: null,
            Suggested: Array.Empty<string>(),
            NotFound: false);
    }
}
