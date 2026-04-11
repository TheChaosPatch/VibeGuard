namespace GuardCode.Content.Validation;

/// <summary>
/// Validates the structural invariants on an <see cref="Archetype"/>
/// that cannot be expressed at the frontmatter schema level: required
/// body sections, per-file line budget, and reference-implementation
/// code-size budget.
/// </summary>
public static class ArchetypeValidator
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

    private const string ReferenceImplementationHeading = "## Reference implementation";

    public static void Validate(Archetype archetype, IReadOnlyDictionary<string, int> rawLineCounts)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        ArgumentNullException.ThrowIfNull(rawLineCounts);

        ValidateRequiredSections(archetype.Id, "_principles.md", archetype.PrinciplesBody, RequiredPrinciplesSections);
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
        string archetypeId, string filename, IReadOnlyDictionary<string, int> rawLineCounts)
    {
        if (rawLineCounts.TryGetValue(filename, out var lines) && lines > MaxFileLines)
        {
            throw new ArchetypeValidationException(
                $"archetype '{archetypeId}': file '{filename}' is {lines} lines, " +
                $"exceeds the {MaxFileLines}-line budget");
        }
    }

    private static void ValidateRequiredSections(
        string archetypeId, string filename, string body, IReadOnlyList<string> requiredSections)
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
        var inFence = false;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (IsFenceLine(line)) { inFence = !inFence; continue; }
            if (inFence) continue;
            if (line.Length == 0 || line[0] != '#') continue;

            var hashEnd = 0;
            while (hashEnd < line.Length && line[hashEnd] == '#') hashEnd++;
            if (hashEnd == 0 || hashEnd > 6) continue;
            if (hashEnd < line.Length && line[hashEnd] != ' ') continue;

            // Strip optional trailing hashes (e.g. "## My heading ##")
            var title = line[hashEnd..].Trim().TrimEnd('#').TrimEnd();
            if (title.Length > 0) headings.Add(title);
        }
        return headings;
    }

    private static void ValidateReferenceImplementationBudget(
        string archetypeId, string filename, string language, string body)
    {
        var sectionBody = ExtractReferenceImplementationSectionBody(body);
        if (sectionBody is null) return;

        var code = ExtractFirstCodeBlockOrNull(sectionBody, archetypeId, filename);
        if (code is null) return;

        // "Non-empty" = not whitespace-only. Comment-only lines count as code by design —
        // we're measuring visual density of the reference implementation, not executable SLOC.
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
        var lines = body.Split('\n');
        var inFence = false;
        var startIdx = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (IsFenceLine(line)) { inFence = !inFence; continue; }
            if (inFence) continue;
            if (line.Trim() == ReferenceImplementationHeading) { startIdx = i + 1; break; }
        }
        if (startIdx < 0) return null;

        inFence = false;
        var endIdx = lines.Length;
        for (var i = startIdx; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (IsFenceLine(line)) { inFence = !inFence; continue; }
            if (inFence) continue;
            // Only `## ` (exactly two hashes + space) terminates the section.
            // `### ` subheadings stay inside the section.
            if (line.StartsWith("## ", StringComparison.Ordinal)
                && !line.StartsWith("### ", StringComparison.Ordinal))
            {
                endIdx = i;
                break;
            }
        }
        return string.Join('\n', lines, startIdx, endIdx - startIdx);
    }

    private static string? ExtractFirstCodeBlockOrNull(string sectionBody, string archetypeId, string filename)
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

    private static bool IsFenceLine(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal)
            || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }
}
