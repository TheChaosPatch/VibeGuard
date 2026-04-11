// xUnit test methods intentionally use Method_State_Expected underscored naming
// for readability in test runner output; CA1707 does not apply to test fixtures.
#pragma warning disable CA1707 // Identifiers should not contain underscores

using VibeGuard.Content;
using VibeGuard.Content.Loading;

namespace VibeGuard.Content.Tests;

public class ArchetypeLoaderTests
{
    private static readonly SupportedLanguageSet DefaultLanguages = SupportedLanguageSet.Default();

    private const string ValidPrinciples =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        title: Password Hashing
        summary: Storing, verifying, and handling user passwords.
        applies_to: [csharp, python]
        status: stable
        author: ehabhussein
        reviewed_by: [ehabhussein]
        stable_since: "2026-04-11"
        keywords: [password, hash, bcrypt]
        ---

        # Principles body
        """;

    private const string ValidCsharpFile =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        language: csharp
        principles_file: _principles.md
        libraries:
          preferred: Konscious.Security.Cryptography.Argon2
          acceptable: []
          avoid: []
        ---

        # C# guidance
        """;

    [Fact]
    public void Load_PrinciplesPlusOneLanguage_ReturnsAggregate()
    {
        var files = new Dictionary<string, string>
        {
            ["_principles.md"] = ValidPrinciples,
            ["csharp.md"] = ValidCsharpFile,
        };

        var archetype = ArchetypeLoader.Load("auth/password-hashing", files, DefaultLanguages);

        archetype.Id.Should().Be("auth/password-hashing");
        archetype.Principles.Archetype.Should().Be("auth/password-hashing");
        archetype.PrinciplesBody.Should().Contain("Principles body");
        archetype.LanguageFiles.Should().ContainKey("csharp");
        archetype.LanguageFiles["csharp"].Body.Should().Contain("C# guidance");
        archetype.LanguageFiles["csharp"].Frontmatter.Language.Should().Be("csharp");
    }

    [Fact]
    public void Load_MissingPrinciples_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["csharp.md"] = ValidCsharpFile,
        };
        var act = () => ArchetypeLoader.Load("auth/password-hashing", files, DefaultLanguages);
        act.Should().Throw<ArchetypeLoadException>()
           .WithMessage("*missing required file '_principles.md'*");
    }

    [Fact]
    public void Load_PrinciplesArchetypeIdMismatch_Throws()
    {
        var files = new Dictionary<string, string>
        {
            ["_principles.md"] = ValidPrinciples, // declares auth/password-hashing
        };
        var act = () => ArchetypeLoader.Load("auth/WRONG", files, DefaultLanguages);
        act.Should().Throw<ArchetypeLoadException>()
           .WithMessage("*frontmatter archetype field is*");
    }

    [Fact]
    public void Load_LanguageFilenameFrontmatterMismatch_Throws()
    {
        // csharp.md but frontmatter says language: python
        const string wrongLanguage =
            """
            ---
            schema_version: 1
            archetype: auth/password-hashing
            language: python
            principles_file: _principles.md
            libraries:
              preferred: argon2-cffi
              acceptable: []
              avoid: []
            ---

            # body
            """;
        var files = new Dictionary<string, string>
        {
            ["_principles.md"] = ValidPrinciples,
            ["csharp.md"] = wrongLanguage,
        };
        var act = () => ArchetypeLoader.Load("auth/password-hashing", files, DefaultLanguages);
        act.Should().Throw<ArchetypeLoadException>()
           .WithMessage("*frontmatter language 'python', expected 'csharp'*");
    }

    [Fact]
    public void Load_FilenameLanguageNotInSet_Throws()
    {
        // Filename-derived language "klingon" is not in the default set,
        // so the loader must reject it rather than silently indexing the file.
        const string klingonFile =
            """
            ---
            schema_version: 1
            archetype: auth/password-hashing
            language: klingon
            principles_file: _principles.md
            libraries:
              preferred: qa'Hom
              acceptable: []
              avoid: []
            ---

            # body
            """;
        var files = new Dictionary<string, string>
        {
            ["_principles.md"] = ValidPrinciples,
            ["klingon.md"] = klingonFile,
        };

        var act = () => ArchetypeLoader.Load("auth/password-hashing", files, DefaultLanguages);

        act.Should().Throw<ArchetypeLoadException>()
           .WithMessage("*'klingon'*not supported*");
    }

    [Fact]
    public void Load_AppliesToEntryNotInSet_Throws()
    {
        // Principles declare applies_to: [csharp, klingon]; the loader must
        // reject this even though no klingon.md exists in the directory,
        // because the claim in applies_to is itself invalid.
        const string principlesWithUnknownLanguage =
            """
            ---
            schema_version: 1
            archetype: auth/password-hashing
            title: Password Hashing
            summary: s
            applies_to: [csharp, klingon]
            status: stable
            author: ehabhussein
            reviewed_by: [ehabhussein]
            stable_since: "2026-04-11"
            keywords: [password]
            ---

            # body
            """;
        var files = new Dictionary<string, string>
        {
            ["_principles.md"] = principlesWithUnknownLanguage,
            ["csharp.md"] = ValidCsharpFile,
        };

        var act = () => ArchetypeLoader.Load("auth/password-hashing", files, DefaultLanguages);

        act.Should().Throw<ArchetypeLoadException>()
           .WithMessage("*applies_to entry 'klingon'*not a supported language*");
    }
}
