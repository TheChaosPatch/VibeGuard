using VibeGuard.Content;
using VibeGuard.Content.Indexing;
using VibeGuard.Content.Services;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707, CA1861, CA1859
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.
// CA1859: BuildIndex returns IArchetypeIndex intentionally — tests must exercise the interface contract, not the concrete type.

public class PrepServiceTests
{
    private static IArchetypeIndex BuildIndex(params Archetype[] archetypes)
        => KeywordArchetypeIndex.Build(archetypes);

    private static Archetype Make(
        string id,
        string title,
        string[] keywords,
        string[] appliesTo)
        => new(
            Id: id,
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = id,
                Title = title,
                Summary = title + " summary.",
                AppliesTo = [.. appliesTo],
                Keywords = [.. keywords],
                RelatedArchetypes = []
            },
            PrinciplesBody: "body",
            LanguageFiles: new Dictionary<string, LanguageFile>(StringComparer.Ordinal));

    [Fact]
    public void Prep_ValidIntent_ReturnsMatches()
    {
        var index = BuildIndex(
            Make("auth/password-hashing", "Password Hashing",
                new[] { "password", "bcrypt" }, new[] { "csharp", "python" }));
        var service = new PrepService(index);

        var result = service.Prep(
            intent: "I'm writing a function to hash a password",
            language: SupportedLanguage.Python,
            framework: null);

        result.Matches.Should().ContainSingle()
              .Which.ArchetypeId.Should().Be("auth/password-hashing");
    }

    [Fact]
    public void Prep_EmptyIntent_Throws()
    {
        var service = new PrepService(BuildIndex());
        var act = () => service.Prep("", SupportedLanguage.CSharp, null);
        act.Should().Throw<ArgumentException>().WithMessage("*non-empty*");
    }

    [Fact]
    public void Prep_OversizedIntent_Throws()
    {
        var service = new PrepService(BuildIndex());
        var giant = new string('x', PrepService.MaxIntentLength + 1);
        var act = () => service.Prep(giant, SupportedLanguage.CSharp, null);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*characters or fewer*");
    }

    [Fact]
    public void Prep_LanguageFilter_HidesUnsupportedArchetypes()
    {
        var index = BuildIndex(
            Make("memory/safe-string-handling", "Safe Strings",
                new[] { "string", "buffer", "overflow" }, new[] { "c" }));
        var service = new PrepService(index);

        var result = service.Prep(
            "safe string buffer handling",
            SupportedLanguage.Python, // not in applies_to
            framework: null);

        result.Matches.Should().BeEmpty();
    }
}
