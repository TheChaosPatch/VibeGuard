// xUnit test methods intentionally use Method_State_Expected underscored naming
// for readability in test runner output; CA1707 does not apply to test fixtures.
#pragma warning disable CA1707 // Identifiers should not contain underscores

using VibeGuard.Content.Loading;

namespace VibeGuard.Content.Tests;

public class ArchetypeLoaderTests
{
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

        var archetype = ArchetypeLoader.Load("auth/password-hashing", files);

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
        var act = () => ArchetypeLoader.Load("auth/password-hashing", files);
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
        var act = () => ArchetypeLoader.Load("auth/WRONG", files);
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
        var act = () => ArchetypeLoader.Load("auth/password-hashing", files);
        act.Should().Throw<ArchetypeLoadException>()
           .WithMessage("*frontmatter language 'python', expected 'csharp'*");
    }
}
