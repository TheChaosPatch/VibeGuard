using GuardCode.Content;
using GuardCode.Content.Validation;

namespace GuardCode.Content.Tests;

#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

public class ArchetypeValidatorTests
{
    private static Archetype BuildArchetype(
        string principlesBody,
        (string lang, string body)[] languageFiles)
    {
        var langMap = new Dictionary<string, LanguageFile>(StringComparer.Ordinal);
        foreach (var (lang, body) in languageFiles)
        {
            langMap[lang] = new LanguageFile(
                new LanguageFrontmatter
                {
                    SchemaVersion = 1,
                    Archetype = "test/example",
                    Language = lang,
                    PrinciplesFile = "_principles.md",
                    Libraries = new LibrariesSection { Preferred = "lib" }
                },
                body);
        }
        return new Archetype(
            Id: "test/example",
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = "test/example",
                Title = "Example",
                Summary = "summary",
                AppliesTo = new List<string> { "csharp" },
                Keywords = new List<string> { "example" }
            },
            PrinciplesBody: principlesBody,
            LanguageFiles: langMap);
    }

    private const string FullyValidPrinciplesBody =
        """
        # Example — Principles

        ## When this applies
        Whenever you need example stuff.

        ## Architectural placement
        In the example layer.

        ## Principles
        Be correct.

        ## Anti-patterns
        Don't be wrong.

        ## References
        OWASP whatever.
        """;

    private const string FullyValidLanguageBody =
        """
        # Example — C#

        ## Library choice
        Use LibX.

        ## Reference implementation
        ```csharp
        void Example() { }
        ```

        ## Language-specific gotchas
        Watch out.

        ## Tests to write
        Test shape, not values.
        """;

    [Fact]
    public void Validate_PrinciplesMissingRequiredSection_Throws()
    {
        var archetype = BuildArchetype(
            principlesBody: "# Example\n\n## When this applies\n\n## Principles\nok\n\n## References\nref",
            languageFiles: new[] { ("csharp", FullyValidLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 20,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*Architectural placement*");
    }

    [Fact]
    public void Validate_LanguageMissingRequiredSection_Throws()
    {
        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", "# Incomplete\n\n## Library choice\nUse X.") });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 20,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*Reference implementation*");
    }

    [Fact]
    public void Validate_FileExceeds200LineBudget_Throws()
    {
        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", FullyValidLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 201,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*csharp.md*201*200*");
    }

    [Fact]
    public void Validate_ReferenceImplementationExceedsCodeBudget_Throws()
    {
        var bloatedCode = string.Join('\n', Enumerable.Range(0, 41).Select(i => $"void Line{i}() {{}}"));
        var bloatedLanguageBody =
            $$"""
            # Example — C#

            ## Library choice
            Use LibX.

            ## Reference implementation
            ```csharp
            {{bloatedCode}}
            ```

            ## Language-specific gotchas
            N/A.

            ## Tests to write
            Test shape.
            """;

        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", bloatedLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 80,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*reference implementation*41*40*");
    }

    [Fact]
    public void Validate_FullyValidArchetype_DoesNotThrow()
    {
        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", FullyValidLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 20,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().NotThrow();
    }
}
