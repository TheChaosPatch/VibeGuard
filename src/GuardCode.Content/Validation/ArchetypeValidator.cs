using System.Text.RegularExpressions;

namespace GuardCode.Content.Validation;

/// <summary>
/// Validates the structural invariants on an <see cref="Archetype"/>
/// that cannot be expressed at the frontmatter schema level: required
/// body sections, per-file line budget, and reference-implementation
/// code-size budget.
/// </summary>
public static partial class ArchetypeValidator
{
    private static readonly string[] RequiredPrinciplesSections =
    [
        "When this applies",
        "Architectural placement",
        "Principles",
        "Anti-patterns",
        "References"
    ];

    private static readonly string[] RequiredLanguageSections =
    [
        "Library choice",
        "Reference implementation",
        "Language-specific gotchas",
        "Tests to write"
    ];

    public const int MaxFileLines = 200;
    public const int MaxReferenceImplementationCodeLines = 40;

    [GeneratedRegex(@"^#{1,6}\s+Reference implementation\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceImplementationHeadingRegex();

    [GeneratedRegex(@"^#{1,6}\s+\S", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex NextSectionHeadingRegex();

    public static void Validate(
        Archetype archetype,
        IReadOnlyDictionary<string, int> rawLineCounts)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        ArgumentNullException.ThrowIfNull(rawLineCounts);

        ValidateRequiredSections(
            archetype.Id,
            "_principles.md",
            archetype.PrinciplesBody,
            RequiredPrinciplesSections);

        ValidateFileLineBudget(archetype.Id, "_principles.md", rawLineCounts);

        foreach (var (language, languageFile) in archetype.LanguageFiles)
        {
            var filename = $"{language}.md";
            ValidateRequiredSections(archetype.Id, filename, languageFile.Body, RequiredLanguageSections);
            ValidateFileLineBudget(archetype.Id, filename, rawLineCounts);
            ValidateReferenceImplementationBudget(archetype.Id, filename, language, languageFile.Body);
        }
    }

    private static void ValidateFileLineBudget(
        string archetypeId,
        string filename,
        IReadOnlyDictionary<string, int> rawLineCounts)
    {
        if (rawLineCounts.TryGetValue(filename, out var lines) && lines > MaxFileLines)
        {
            throw new ArchetypeValidationException(
                $"archetype '{archetypeId}': file '{filename}' is {lines} lines, " +
                $"exceeds the {MaxFileLines}-line budget");
        }
    }

    private static void ValidateRequiredSections(
        string archetypeId,
        string filename,
        string body,
        IReadOnlyList<string> requiredSections)
    {
        var presentHeadings = CollectHeadings(body);
        foreach (var section in requiredSections)
        {
            if (!presentHeadings.Contains(section))
            {
                throw new ArchetypeValidationException(
                    $"archetype '{archetypeId}': file '{filename}' is missing required section '{section}'");
            }
        }
    }

    private static HashSet<string> CollectHeadings(string body)
    {
        var headings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();
            if (line.Length == 0 || line[0] != '#') continue;

            var hashEnd = 0;
            while (hashEnd < line.Length && hashEnd < 6 && line[hashEnd] == '#') hashEnd++;
            if (hashEnd == 0 || hashEnd >= line.Length || line[hashEnd] != ' ') continue;

            var title = line[(hashEnd + 1)..].Trim();
            if (title.Length > 0) headings.Add(title);
        }
        return headings;
    }

    private static void ValidateReferenceImplementationBudget(
        string archetypeId,
        string filename,
        string language,
        string body)
    {
        var sectionBody = ExtractReferenceImplementationSectionBody(body);
        if (sectionBody is null) return;

        var code = ExtractFirstCodeBlockOrNull(sectionBody, archetypeId, filename);
        if (code is null) return;

        var codeLines = code.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
        if (codeLines > MaxReferenceImplementationCodeLines)
        {
            throw new ArchetypeValidationException(
                $"archetype '{archetypeId}': file '{filename}' reference implementation is " +
                $"{codeLines} non-empty lines, exceeds the {MaxReferenceImplementationCodeLines}-line budget " +
                $"(language: {language})");
        }
    }

    private static string? ExtractReferenceImplementationSectionBody(string body)
    {
        var sectionStart = ReferenceImplementationHeadingRegex().Match(body);
        if (!sectionStart.Success) return null;

        var afterHeading = body[(sectionStart.Index + sectionStart.Length)..];

        var nextSection = NextSectionHeadingRegex().Match(afterHeading);
        return nextSection.Success
            ? afterHeading[..nextSection.Index]
            : afterHeading;
    }

    private static string? ExtractFirstCodeBlockOrNull(
        string sectionBody,
        string archetypeId,
        string filename)
    {
        const string Fence = "```";
        var openIndex = sectionBody.IndexOf(Fence, StringComparison.Ordinal);
        if (openIndex < 0) return null;

        var afterOpen = sectionBody[(openIndex + Fence.Length)..];
        var firstNewline = afterOpen.IndexOf('\n');
        if (firstNewline < 0) return null;
        var codeStart = firstNewline + 1;

        var closeIndex = afterOpen.IndexOf(Fence, codeStart, StringComparison.Ordinal);
        if (closeIndex < 0)
        {
            throw new ArchetypeValidationException(
                $"archetype '{archetypeId}': file '{filename}' has an unterminated code block " +
                "in the Reference implementation section");
        }

        return afterOpen[codeStart..closeIndex];
    }
}
