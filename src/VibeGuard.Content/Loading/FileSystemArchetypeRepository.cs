using VibeGuard.Content.Validation;

namespace VibeGuard.Content.Loading;

/// <summary>
/// Walks the archetypes root directory, loads every archetype, runs
/// validation, and returns the result. Implements the path-traversal
/// defense from design spec §6.1 with two layers:
///
/// 1. Enumeration skips reparse points (symlinks and junctions), so a
///    directory symlink inside the root cannot redirect the loader to
///    content outside the root.
/// 2. Every enumerated file path is re-normalized via
///    <see cref="Path.GetFullPath(string)"/> and verified to start
///    with the root's full path. The root is stored with a trailing
///    separator to prevent prefix confusion
///    (e.g., <c>/foo/bar</c> matching <c>/foo/barbaz</c>).
///
/// Threat model: an operator-controlled content directory with
/// potentially adversarial archetype files. Not hardened against an
/// attacker who can create reparse points inside the root concurrently
/// with loading (time-of-check/time-of-use races are out of scope for
/// a synchronous startup-time loader).
/// </summary>
public sealed class FileSystemArchetypeRepository : IArchetypeRepository
{
    private static readonly EnumerationOptions MarkdownEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        MatchCasing = MatchCasing.PlatformDefault,
        ReturnSpecialDirectories = false,
    };

    private readonly string _rootFullPath;
    private readonly bool _includeDrafts;

    public FileSystemArchetypeRepository(string rootPath)
        : this(rootPath, includeDrafts: false)
    {
    }

    /// <summary>
    /// Create a repository with explicit control over draft visibility.
    /// When <paramref name="includeDrafts"/> is <c>false</c> (the default),
    /// draft archetypes are still parsed and validated so broken drafts
    /// fail CI, but they are omitted from the returned corpus and will
    /// not appear in <c>prep</c> results or resolve via <c>consult</c>.
    /// </summary>
    public FileSystemArchetypeRepository(string rootPath, bool includeDrafts)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("root path must be non-empty", nameof(rootPath));
        }
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException(
                $"archetypes root does not exist: {rootPath}");
        }
        _rootFullPath = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
        _includeDrafts = includeDrafts;
    }

    public IReadOnlyList<Archetype> LoadAll()
    {
        var filesByDirectory = GroupMarkdownFilesByDirectory();

        var archetypes = new List<Archetype>();
        foreach (var (directory, files) in filesByDirectory)
        {
            if (!files.ContainsKey(ArchetypeLoader.PrinciplesFilename)) continue;

            var archetypeId = DeriveArchetypeId(directory);
            var (archetype, rawLineCounts) = ArchetypeLoader.LoadWithLineCounts(archetypeId, files);
            ArchetypeValidator.Validate(archetype, rawLineCounts);

            // Drafts are parsed and validated so broken drafts fail CI, but
            // they are hidden from the active corpus unless the caller opted
            // in. Hiding happens here (not in the loader/validator) because
            // the filter is a deployment concern, not a content concern.
            if (!_includeDrafts && archetype.Principles.Status == ArchetypeStatus.Draft)
            {
                continue;
            }

            archetypes.Add(archetype);
        }

        archetypes.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        return archetypes;
    }

    private Dictionary<string, Dictionary<string, string>> GroupMarkdownFilesByDirectory()
    {
        var filesByDirectory = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(_rootFullPath, "*.md", MarkdownEnumerationOptions))
        {
            var fullPath = Path.GetFullPath(file);
            EnsureUnderRoot(_rootFullPath, fullPath);

            var directory = Path.GetDirectoryName(fullPath)!;
            if (!filesByDirectory.TryGetValue(directory, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                filesByDirectory[directory] = map;
            }
            map[Path.GetFileName(fullPath)] = File.ReadAllText(fullPath);
        }

        return filesByDirectory;
    }

    internal static void EnsureUnderRoot(string rootFullPath, string candidateFullPath)
    {
        // Path comparison on Windows must be case-insensitive: a drive letter
        // or directory-casing difference between the stored root and an
        // enumerated file path would otherwise produce a false rejection.
        // Linux and macOS remain ordinal because their file systems are
        // conventionally case-sensitive. Fails-closed either way — no path
        // that was previously rejected becomes allowed by this change.
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidateFullPath.StartsWith(rootFullPath, comparison))
        {
            throw new ArchetypeLoadException(
                $"refusing to load file outside archetypes root: {candidateFullPath}");
        }
    }

    private string DeriveArchetypeId(string fullDirectory)
    {
        var relative = Path.GetRelativePath(_rootFullPath, fullDirectory);
        // Normalize both separators defensively. On Windows GetRelativePath
        // returns backslashes today, but covering AltDirectorySeparatorChar
        // makes the archetype ID construction robust to platform variance.
        // On platforms where DirectorySeparatorChar == AltDirectorySeparatorChar
        // the second Replace is a no-op over the already-normalized string.
        return relative
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.Length == 0) return path;
        var last = path[^1];
        if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar) return path;
        return path + Path.DirectorySeparatorChar;
    }
}
