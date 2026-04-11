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
                AppliesTo = ["csharp"],
                Keywords = ["example"],
                // Lifecycle fields required by ValidateLifecycle for Stable
                // status (which is the record default). Kept in the helper so
                // individual tests don't have to restate this boilerplate.
                Status = ArchetypeStatus.Stable,
                Author = "ehabhussein",
                ReviewedBy = ["ehabhussein"],
                StableSince = "2026-04-11",
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

    [Fact]
    public void Validate_FileExactly200Lines_DoesNotThrow()
    {
        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", FullyValidLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 200,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ReferenceImplementationExactly40CodeLines_DoesNotThrow()
    {
        var exactCode = string.Join('\n', Enumerable.Range(0, 40).Select(i => $"void Line{i}() {{}}"));
        var exactLanguageBody =
            $$"""
            # Example — C#

            ## Library choice
            Use LibX.

            ## Reference implementation
            ```csharp
            {{exactCode}}
            ```

            ## Language-specific gotchas
            N/A.

            ## Tests to write
            Test shape.
            """;

        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", exactLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 80,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ReferenceImplementationUnterminatedFence_Throws()
    {
        // All required headings are placed BEFORE "## Reference implementation"
        // so the required-sections check passes and the validator reaches the
        // reference-implementation budget check, where the unterminated fence fires.
        var unterminatedLanguageBody =
            """
            # Example — C#

            ## Library choice
            Use LibX.

            ## Language-specific gotchas
            N/A.

            ## Tests to write
            Test shape.

            ## Reference implementation
            ```csharp
            void Example() { }
            """;

        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", unterminatedLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 20,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*unterminated code block*");
    }

    // -------------------- Lifecycle validation --------------------

    private static Archetype BuildWithPrinciples(PrinciplesFrontmatter principles)
        => new(
            Id: "test/example",
            Principles: principles,
            PrinciplesBody: FullyValidPrinciplesBody,
            LanguageFiles: new Dictionary<string, LanguageFile>(StringComparer.Ordinal)
            {
                ["csharp"] = new LanguageFile(
                    new LanguageFrontmatter
                    {
                        SchemaVersion = 1,
                        Archetype = "test/example",
                        Language = "csharp",
                        PrinciplesFile = "_principles.md",
                        Libraries = new LibrariesSection { Preferred = "lib" }
                    },
                    FullyValidLanguageBody),
            });

    private static PrinciplesFrontmatter BaseStablePrinciples() => new()
    {
        SchemaVersion = 1,
        Archetype = "test/example",
        Title = "Example",
        Summary = "summary",
        AppliesTo = ["csharp"],
        Keywords = ["example"],
        Status = ArchetypeStatus.Stable,
        Author = "ehabhussein",
        ReviewedBy = ["ehabhussein"],
        StableSince = "2026-04-11",
    };

    private static readonly Dictionary<string, int> HappyLineCounts = new()
    {
        ["_principles.md"] = 20,
        ["csharp.md"] = 20,
    };

    [Fact]
    public void Validate_StableMissingAuthor_Throws()
    {
        var archetype = BuildWithPrinciples(BaseStablePrinciples() with { Author = "" });

        var act = () => ArchetypeValidator.Validate(archetype, HappyLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*'stable'*'author' is missing*");
    }

    [Fact]
    public void Validate_StableMissingReviewedBy_Throws()
    {
        var archetype = BuildWithPrinciples(BaseStablePrinciples() with { ReviewedBy = [] });

        var act = () => ArchetypeValidator.Validate(archetype, HappyLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*'stable'*'reviewed_by' is empty*");
    }

    [Fact]
    public void Validate_StableMissingStableSince_Throws()
    {
        var archetype = BuildWithPrinciples(BaseStablePrinciples() with { StableSince = null });

        var act = () => ArchetypeValidator.Validate(archetype, HappyLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*'stable'*'stable_since' is missing*");
    }

    [Fact]
    public void Validate_StableWithSupersededBy_Throws()
    {
        // superseded_by is only meaningful for deprecated archetypes.
        var archetype = BuildWithPrinciples(
            BaseStablePrinciples() with { SupersededBy = "test/example-v2" });

        var act = () => ArchetypeValidator.Validate(archetype, HappyLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*'superseded_by' is only valid when status is 'deprecated'*");
    }

    [Fact]
    public void Validate_DeprecatedMissingSupersededBy_Throws()
    {
        var archetype = BuildWithPrinciples(new PrinciplesFrontmatter
        {
            SchemaVersion = 1,
            Archetype = "test/example",
            Title = "Example",
            Summary = "summary",
            AppliesTo = ["csharp"],
            Keywords = ["example"],
            Status = ArchetypeStatus.Deprecated,
            // SupersededBy intentionally null — the whole point of this test
        });

        var act = () => ArchetypeValidator.Validate(archetype, HappyLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*'deprecated'*'superseded_by' is missing*");
    }

    [Fact]
    public void Validate_Draft_DoesNotRequireStableFields()
    {
        // Drafts are explicitly work-in-progress — they can ship without
        // author, reviewers, or a stable date. The validator still runs
        // every other structural check against them.
        var archetype = BuildWithPrinciples(new PrinciplesFrontmatter
        {
            SchemaVersion = 1,
            Archetype = "test/example",
            Title = "Example",
            Summary = "summary",
            AppliesTo = ["csharp"],
            Keywords = ["example"],
            Status = ArchetypeStatus.Draft,
        });

        var act = () => ArchetypeValidator.Validate(archetype, HappyLineCounts);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_DraftWithSupersededBy_Throws()
    {
        var archetype = BuildWithPrinciples(new PrinciplesFrontmatter
        {
            SchemaVersion = 1,
            Archetype = "test/example",
            Title = "Example",
            Summary = "summary",
            AppliesTo = ["csharp"],
            Keywords = ["example"],
            Status = ArchetypeStatus.Draft,
            SupersededBy = "test/example-v2",
        });

        var act = () => ArchetypeValidator.Validate(archetype, HappyLineCounts);

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*'superseded_by' is only valid when status is 'deprecated'*");
    }

    [Fact]
    public void Validate_ReferenceImplementationWithNoCodeBlock_DoesNotThrow()
    {
        var proseOnlyLanguageBody =
            """
            # Example — C#

            ## Library choice
            Use LibX.

            ## Reference implementation
            See the linked sample repository for a complete worked example.
            No inline code block is provided here because the pattern is purely architectural.

            ## Language-specific gotchas
            N/A.

            ## Tests to write
            Test shape.
            """;

        var archetype = BuildArchetype(
            principlesBody: FullyValidPrinciplesBody,
            languageFiles: new[] { ("csharp", proseOnlyLanguageBody) });
        var rawLineCounts = new Dictionary<string, int>
        {
            ["_principles.md"] = 20,
            ["csharp.md"] = 20,
        };

        var act = () => ArchetypeValidator.Validate(archetype, rawLineCounts);

        act.Should().NotThrow();
    }
}
