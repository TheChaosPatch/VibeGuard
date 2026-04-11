using System.Collections.Frozen;

namespace VibeGuard.Content.Loading;

/// <summary>
/// Pure transformer: a flat (filename -> content) map for one archetype
/// directory becomes one <see cref="Archetype"/> aggregate. Performs
/// cross-file consistency checks (principles file must exist, archetype
/// IDs must match directory and frontmatter, language filenames must
/// match their frontmatter language) and enforces that every language
/// touched by the archetype — whether via filename or <c>applies_to</c>
/// — is a member of the configured <see cref="SupportedLanguageSet"/>.
/// Does no filesystem I/O of its own — that belongs to
/// <c>FileSystemArchetypeRepository</c>.
/// </summary>
public static class ArchetypeLoader
{
    internal const string PrinciplesFilename = "_principles.md";
    private const string MarkdownExtension = ".md";

    public static Archetype Load(
        string expectedArchetypeId,
        IReadOnlyDictionary<string, string> filesInDirectory,
        SupportedLanguageSet supportedLanguages)
    {
        ArgumentNullException.ThrowIfNull(expectedArchetypeId);
        ArgumentNullException.ThrowIfNull(filesInDirectory);
        ArgumentNullException.ThrowIfNull(supportedLanguages);

        var principles = LoadPrinciples(expectedArchetypeId, filesInDirectory, supportedLanguages);
        var languageFiles = LoadLanguageFiles(expectedArchetypeId, filesInDirectory, supportedLanguages);

        return new Archetype(
            Id: expectedArchetypeId,
            Principles: principles.Frontmatter,
            PrinciplesBody: principles.Body,
            LanguageFiles: languageFiles);
    }

    /// <summary>
    /// Same as <see cref="Load"/>, but also returns the raw (pre-parse)
    /// line count of every file keyed by filename. Used by the validator
    /// to enforce the per-file 200-line budget from spec §4.2.
    /// </summary>
    public static (Archetype Archetype, FrozenDictionary<string, int> RawLineCounts) LoadWithLineCounts(
        string expectedArchetypeId,
        IReadOnlyDictionary<string, string> filesInDirectory,
        SupportedLanguageSet supportedLanguages)
    {
        ArgumentNullException.ThrowIfNull(expectedArchetypeId);
        ArgumentNullException.ThrowIfNull(filesInDirectory);
        ArgumentNullException.ThrowIfNull(supportedLanguages);

        var archetype = Load(expectedArchetypeId, filesInDirectory, supportedLanguages);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (filename, content) in filesInDirectory)
        {
            counts[filename] = CountLines(content);
        }
        return (archetype, counts.ToFrozenDictionary(StringComparer.Ordinal));
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        var count = 1;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n') count++;
        }
        if (content[^1] == '\n') count--;
        return count;
    }

    private static ParseResult<PrinciplesFrontmatter> LoadPrinciples(
        string expectedArchetypeId,
        IReadOnlyDictionary<string, string> filesInDirectory,
        SupportedLanguageSet supportedLanguages)
    {
        if (!filesInDirectory.TryGetValue(PrinciplesFilename, out var principlesContent))
        {
            throw new ArchetypeLoadException(
                $"archetype '{expectedArchetypeId}' is missing required file '{PrinciplesFilename}'");
        }

        var parsed = FrontmatterParser.ParsePrinciples(principlesContent);

        if (!string.Equals(parsed.Frontmatter.Archetype, expectedArchetypeId, StringComparison.Ordinal))
        {
            throw new ArchetypeLoadException(
                $"archetype '{expectedArchetypeId}': frontmatter archetype field is " +
                $"'{parsed.Frontmatter.Archetype}', expected '{expectedArchetypeId}'");
        }

        // Reject any applies_to entry that is not in the configured set.
        // Closing this at load time means `applies_to: [klingon]` fails
        // startup with a clear diagnostic instead of silently passing
        // validation and mysteriously returning nothing at query time.
        foreach (var declared in parsed.Frontmatter.AppliesTo)
        {
            if (!supportedLanguages.Contains(declared))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': applies_to entry '{declared}' " +
                    $"is not a supported language (expected one of: {supportedLanguages.ToSortedList()})");
            }
        }

        return parsed;
    }

    private static FrozenDictionary<string, LanguageFile> LoadLanguageFiles(
        string expectedArchetypeId,
        IReadOnlyDictionary<string, string> filesInDirectory,
        SupportedLanguageSet supportedLanguages)
    {
        var languageFiles = new Dictionary<string, LanguageFile>(StringComparer.Ordinal);

        foreach (var (filename, content) in filesInDirectory)
        {
            if (filename == PrinciplesFilename) continue;

            if (!filename.EndsWith(MarkdownExtension, StringComparison.Ordinal))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': non-markdown file '{filename}' is not allowed");
            }

            var languageFromFilename = Path.GetFileNameWithoutExtension(filename);

            if (!supportedLanguages.Contains(languageFromFilename))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': file '{filename}' declares language " +
                    $"'{languageFromFilename}' which is not supported " +
                    $"(expected one of: {supportedLanguages.ToSortedList()})");
            }

            var parsed = FrontmatterParser.ParseLanguage(content);

            if (!string.Equals(parsed.Frontmatter.Language, languageFromFilename, StringComparison.Ordinal))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': file '{filename}' has frontmatter " +
                    $"language '{parsed.Frontmatter.Language}', expected '{languageFromFilename}'");
            }

            if (!string.Equals(parsed.Frontmatter.Archetype, expectedArchetypeId, StringComparison.Ordinal))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': file '{filename}' has frontmatter " +
                    $"archetype '{parsed.Frontmatter.Archetype}', expected '{expectedArchetypeId}'");
            }

            languageFiles[languageFromFilename] = new LanguageFile(parsed.Frontmatter, parsed.Body);
        }

        return languageFiles.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

/// <summary>
/// Thrown when an archetype directory's contents fail cross-file
/// consistency checks (missing principles, mismatched archetype IDs,
/// filename/frontmatter language disagreement, unsupported language,
/// or stray non-markdown files).
/// </summary>
public sealed class ArchetypeLoadException : Exception
{
    public ArchetypeLoadException() { }
    public ArchetypeLoadException(string message) : base(message) { }
    public ArchetypeLoadException(string message, Exception inner) : base(message, inner) { }
}
