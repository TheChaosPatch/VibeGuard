// xUnit test methods intentionally use Method_State_Expected underscored naming
// for readability in test runner output; CA1707 does not apply to test fixtures.
#pragma warning disable CA1707 // Identifiers should not contain underscores
// Inline literal arrays in assertions are clearer than hoisting to fields for
// one-off expected values; CA1861's perf concern is irrelevant in test code.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

using GuardCode.Content;
using GuardCode.Content.Loading;

namespace GuardCode.Content.Tests;

public class FrontmatterParserTests
{
    private const string ValidPrinciples =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        title: Password Hashing
        summary: Storing, verifying, and handling user passwords in any backend.
        applies_to: [csharp, python, go]
        status: stable
        author: ehabhussein
        reviewed_by: [ehabhussein]
        stable_since: "2026-04-11"
        keywords:
          - password
          - bcrypt
        related_archetypes:
          - auth/session-tokens
        equivalents_in:
          c: crypto/key-derivation
        references:
          owasp_asvs: V2.4
          cwe: "916"
        ---

        # Body

        Principles body text.
        """;

    [Fact]
    public void Parse_ValidPrinciples_ReturnsFrontmatterAndBody()
    {
        var result = FrontmatterParser.ParsePrinciples(ValidPrinciples);

        result.Frontmatter.SchemaVersion.Should().Be(1);
        result.Frontmatter.Archetype.Should().Be("auth/password-hashing");
        result.Frontmatter.Title.Should().Be("Password Hashing");
        result.Frontmatter.AppliesTo.Should().BeEquivalentTo(new[] { "csharp", "python", "go" });
        result.Frontmatter.Keywords.Should().BeEquivalentTo(new[] { "password", "bcrypt" });
        result.Frontmatter.RelatedArchetypes.Should().ContainSingle().Which.Should().Be("auth/session-tokens");
        result.Frontmatter.EquivalentsIn.Should().ContainKey("c").WhoseValue.Should().Be("crypto/key-derivation");
        result.Frontmatter.References.Should().ContainKey("owasp_asvs").WhoseValue.Should().Be("V2.4");
        result.Body.Should().StartWith("# Body");
        result.Body.Should().Contain("Principles body text.");
        result.Body.Should().NotContain("schema_version");
    }

    [Fact]
    public void Parse_MissingOpeningDelimiter_Throws()
    {
        const string content = "no frontmatter here\njust body.";
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*does not begin*");
    }

    [Fact]
    public void Parse_UnclosedFrontmatter_Throws()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: x/y
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*not closed*");
    }

    [Fact]
    public void Parse_UnknownField_Throws()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: x/y
            title: T
            summary: s
            applies_to: [csharp]
            keywords: [k]
            unexpected_field: boom
            ---

            body
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*malformed or contains unknown fields*");
    }

    [Fact]
    public void Parse_MalformedYaml_Throws()
    {
        const string content =
            """
            ---
            schema_version: [not, a, number
            ---

            body
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>();
    }

    [Fact]
    public void Parse_MissingStatus_Throws()
    {
        // All other fields present; only 'status' is absent. The parser should
        // reject this before the projection layer runs — lifecycle is required.
        const string content =
            """
            ---
            schema_version: 1
            archetype: auth/password-hashing
            title: Password Hashing
            summary: s
            applies_to: [csharp]
            keywords: [k]
            ---

            body
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*missing required field 'status'*");
    }

    [Fact]
    public void Parse_UnknownStatus_Throws()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: auth/password-hashing
            title: Password Hashing
            summary: s
            applies_to: [csharp]
            status: experimental
            keywords: [k]
            ---

            body
            """;
        var act = () => FrontmatterParser.ParsePrinciples(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*must be one of draft, stable, deprecated*experimental*");
    }

    [Fact]
    public void Parse_StableStatus_ProjectsLifecycleFields()
    {
        var result = FrontmatterParser.ParsePrinciples(ValidPrinciples);

        result.Frontmatter.Status.Should().Be(ArchetypeStatus.Stable);
        result.Frontmatter.Author.Should().Be("ehabhussein");
        result.Frontmatter.ReviewedBy.Should().ContainSingle().Which.Should().Be("ehabhussein");
        result.Frontmatter.StableSince.Should().Be("2026-04-11");
        result.Frontmatter.SupersededBy.Should().BeNull();
    }

    [Fact]
    public void Parse_DraftStatus_AllowsMissingStableFields()
    {
        // A draft does not require author/reviewed_by/stable_since at the
        // parser level — those gates live in the validator. The parser just
        // needs to accept 'draft' as a legal value and project null defaults
        // through to the record.
        const string content =
            """
            ---
            schema_version: 1
            archetype: auth/password-hashing
            title: Password Hashing
            summary: s
            applies_to: [csharp]
            status: draft
            keywords: [k]
            ---

            body
            """;

        var result = FrontmatterParser.ParsePrinciples(content);

        result.Frontmatter.Status.Should().Be(ArchetypeStatus.Draft);
        result.Frontmatter.Author.Should().BeEmpty();
        result.Frontmatter.ReviewedBy.Should().BeEmpty();
        result.Frontmatter.StableSince.Should().BeNull();
        result.Frontmatter.SupersededBy.Should().BeNull();
    }

    [Fact]
    public void Parse_DeprecatedStatus_ProjectsSupersededBy()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: auth/password-hashing
            title: Password Hashing
            summary: s
            applies_to: [csharp]
            status: deprecated
            superseded_by: auth/password-hashing-v2
            keywords: [k]
            ---

            body
            """;

        var result = FrontmatterParser.ParsePrinciples(content);

        result.Frontmatter.Status.Should().Be(ArchetypeStatus.Deprecated);
        result.Frontmatter.SupersededBy.Should().Be("auth/password-hashing-v2");
    }
}
