// YamlDotNet requires concrete mutable collection types for deserialization.
// The pragma is justified and contained to the file-scoped DTOs below: the
// public domain surface (PrinciplesFrontmatter, LanguageFrontmatter, ...) is
// fully immutable and carries no suppressions.
#pragma warning disable CA1002 // Do not expose generic lists
#pragma warning disable CA2227 // Collection properties should be read only

using System.Collections.Frozen;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace VibeGuard.Content.Loading;

/// <summary>
/// The outcome of a successful frontmatter parse: the strongly-typed
/// frontmatter record and the remaining markdown body.
/// </summary>
/// <remarks>
/// Declared at the top level (not nested inside <see cref="FrontmatterParser"/>)
/// to satisfy CA1034 — nested types should not be publicly visible.
/// </remarks>
public readonly record struct ParseResult<T>(T Frontmatter, string Body);

/// <summary>
/// Splits a markdown file into its YAML frontmatter and body, then
/// strictly deserializes the frontmatter into a mutable DTO before
/// projecting it onto the public immutable record.
/// </summary>
/// <remarks>
/// Strictness is load-bearing for security (design spec §6.3):
/// unknown properties are rejected, the naming convention is fixed,
/// and no dynamic type resolution is ever performed.
/// </remarks>
public static class FrontmatterParser
{
    private const string Delimiter = "---";
    private const string ErrNullContent = "file content is null";
    private const string ErrMissingOpen = "file does not begin with YAML frontmatter delimiter (---)";
    private const string ErrUnclosed = "YAML frontmatter is not closed — missing terminating --- delimiter";
    private const string ErrMalformedPrefix = "YAML frontmatter is malformed or contains unknown fields: ";
    private const string ErrMissingStatus =
        "frontmatter is missing required field 'status' (one of: draft, stable, deprecated)";
    private const string ErrUnknownStatusPrefix =
        "frontmatter field 'status' must be one of draft, stable, deprecated — got: ";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // Do NOT call IgnoreUnmatchedProperties: we want unknown fields to throw.
        .Build();

    /// <summary>
    /// Parse the frontmatter of an archetype <c>_principles.md</c> file into
    /// the public immutable <see cref="PrinciplesFrontmatter"/> record.
    /// </summary>
    public static ParseResult<PrinciplesFrontmatter> ParsePrinciples(string fileContent)
    {
        var (dto, body) = ParseInternal<PrinciplesFrontmatterDto>(fileContent);

        if (string.IsNullOrWhiteSpace(dto.Status))
        {
            throw new FrontmatterParseException(ErrMissingStatus);
        }
        if (!ArchetypeStatusExtensions.TryParseWire(dto.Status, out var status))
        {
            throw new FrontmatterParseException(ErrUnknownStatusPrefix + dto.Status);
        }

        var projected = new PrinciplesFrontmatter
        {
            SchemaVersion = dto.SchemaVersion,
            Archetype = dto.Archetype,
            Title = dto.Title,
            Summary = dto.Summary,
            AppliesTo = dto.AppliesTo.ToArray(),
            Keywords = dto.Keywords.ToArray(),
            RelatedArchetypes = dto.RelatedArchetypes.ToArray(),
            EquivalentsIn = dto.EquivalentsIn.ToFrozenDictionary(StringComparer.Ordinal),
            References = dto.References.ToFrozenDictionary(StringComparer.Ordinal),
            Status = status,
            Author = dto.Author ?? string.Empty,
            ReviewedBy = dto.ReviewedBy.ToArray(),
            StableSince = string.IsNullOrWhiteSpace(dto.StableSince) ? null : dto.StableSince,
            SupersededBy = string.IsNullOrWhiteSpace(dto.SupersededBy) ? null : dto.SupersededBy,
        };
        return new ParseResult<PrinciplesFrontmatter>(projected, body);
    }

    /// <summary>
    /// Parse the frontmatter of an archetype language file (e.g. <c>csharp.md</c>)
    /// into the public immutable <see cref="LanguageFrontmatter"/> record.
    /// </summary>
    public static ParseResult<LanguageFrontmatter> ParseLanguage(string fileContent)
    {
        var (dto, body) = ParseInternal<LanguageFrontmatterDto>(fileContent);
        var projected = new LanguageFrontmatter
        {
            SchemaVersion = dto.SchemaVersion,
            Archetype = dto.Archetype,
            Language = dto.Language,
            Framework = dto.Framework,
            PrinciplesFile = dto.PrinciplesFile,
            Libraries = new LibrariesSection
            {
                Preferred = dto.Libraries.Preferred,
                Acceptable = dto.Libraries.Acceptable.ToArray(),
                Avoid = dto.Libraries.Avoid.ConvertAll(static a => new AvoidedLibrary
                {
                    Name = a.Name,
                    Reason = a.Reason,
                }),
            },
            MinimumVersions = dto.MinimumVersions.ToFrozenDictionary(StringComparer.Ordinal),
        };
        return new ParseResult<LanguageFrontmatter>(projected, body);
    }

    private static (TDto Dto, string Body) ParseInternal<TDto>(string fileContent) where TDto : class, new()
    {
        if (fileContent is null)
        {
            throw new FrontmatterParseException(ErrNullContent);
        }

        using var reader = new StringReader(fileContent);

        // First non-empty line must be the opening delimiter.
        var firstLine = ReadNextNonEmptyLine(reader);
        if (firstLine is null || firstLine.Trim() != Delimiter)
        {
            throw new FrontmatterParseException(ErrMissingOpen);
        }

        // Accumulate until the closing delimiter.
        var yamlBuilder = new StringBuilder();
        string? line;
        var foundClose = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == Delimiter)
            {
                foundClose = true;
                break;
            }
            yamlBuilder.AppendLine(line);
        }

        if (!foundClose)
        {
            throw new FrontmatterParseException(ErrUnclosed);
        }

        TDto dto;
        try
        {
            dto = Deserializer.Deserialize<TDto>(yamlBuilder.ToString()) ?? new TDto();
        }
        catch (YamlException ex)
        {
            throw new FrontmatterParseException(ErrMalformedPrefix + ex.Message, ex);
        }

        var body = reader.ReadToEnd().TrimStart('\r', '\n');
        return (dto, body);
    }

    private static string? ReadNextNonEmptyLine(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }
        return null;
    }

}

// -----------------------------------------------------------------------------
// File-scoped mutable DTOs — YamlDotNet deserialization targets only.
//
// These types exist solely to give YamlDotNet mutable concrete collections to
// populate; they are immediately projected onto the public immutable records
// above and are not observable outside this file. The CA1002/CA2227 pragma at
// the top of the file is justified and contained here.
// -----------------------------------------------------------------------------

file sealed class PrinciplesFrontmatterDto
{
    public int SchemaVersion { get; set; }
    public string Archetype { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> AppliesTo { get; set; } = [];
    public List<string> Keywords { get; set; } = [];
    public List<string> RelatedArchetypes { get; set; } = [];
    public Dictionary<string, string> EquivalentsIn { get; set; } = [];
    public Dictionary<string, string> References { get; set; } = [];

    // Lifecycle metadata. Nullable on the DTO so YamlDotNet can tell
    // "absent" apart from "empty"; the projection layer enforces which
    // fields are required.
    public string? Status { get; set; }
    public string? Author { get; set; }
    public List<string> ReviewedBy { get; set; } = [];
    public string? StableSince { get; set; }
    public string? SupersededBy { get; set; }
}

file sealed class LanguageFrontmatterDto
{
    public int SchemaVersion { get; set; }
    public string Archetype { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Framework { get; set; }
    public string PrinciplesFile { get; set; } = string.Empty;
    public LibrariesSectionDto Libraries { get; set; } = new();
    public Dictionary<string, string> MinimumVersions { get; set; } = [];
}

file sealed class LibrariesSectionDto
{
    public string Preferred { get; set; } = string.Empty;
    public List<string> Acceptable { get; set; } = [];
    public List<AvoidedLibraryDto> Avoid { get; set; } = [];
}

file sealed class AvoidedLibraryDto
{
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
