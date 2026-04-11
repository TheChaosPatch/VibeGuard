using GuardCode.Content;
using GuardCode.Content.Indexing;
using GuardCode.Content.Services;

namespace GuardCode.Content.Tests;

#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

public class ConsultationServiceTests
{
    private static Archetype Make(
        string id,
        string[] appliesTo,
        (string lang, string body)[] languageFiles,
        string principlesBody = "PRINCIPLES_BODY",
        string[]? relatedArchetypes = null,
        Dictionary<string, string>? equivalentsIn = null,
        Dictionary<string, string>? references = null)
    {
        var langMap = new Dictionary<string, LanguageFile>(StringComparer.Ordinal);
        foreach (var (lang, body) in languageFiles)
        {
            langMap[lang] = new LanguageFile(
                new LanguageFrontmatter
                {
                    SchemaVersion = 1,
                    Archetype = id,
                    Language = lang,
                    PrinciplesFile = "_principles.md",
                    Libraries = new LibrariesSection { Preferred = "lib" }
                },
                body);
        }
        return new Archetype(
            Id: id,
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = id,
                Title = id,
                Summary = "s",
                AppliesTo = new List<string>(appliesTo),
                Keywords = new List<string> { "k" },
                RelatedArchetypes = new List<string>(relatedArchetypes ?? Array.Empty<string>()),
                EquivalentsIn = equivalentsIn ?? new Dictionary<string, string>(),
                References = references ?? new Dictionary<string, string>()
            },
            PrinciplesBody: principlesBody,
            LanguageFiles: langMap);
    }

    [Fact]
    public void Consult_ValidArchetypeAndLanguage_ComposesPrinciplesAndLanguageBody()
    {
        var archetype = Make(
            "auth/password-hashing",
            appliesTo: new[] { "csharp", "python" },
            languageFiles: new[] { ("python", "PYTHON_BODY") },
            principlesBody: "PRINCIPLES_BODY",
            relatedArchetypes: new[] { "auth/session-tokens" },
            references: new Dictionary<string, string>
            {
                ["owasp_asvs"] = "V2.4",
                ["cwe"] = "916"
            });
        var index = KeywordArchetypeIndex.Build(new[] { archetype });
        var service = new ConsultationService(index);

        var result = service.Consult("auth/password-hashing", SupportedLanguage.Python);

        result.NotFound.Should().BeFalse();
        result.Redirect.Should().BeFalse();
        result.Content.Should().Be("PRINCIPLES_BODY\n\n---\n\nPYTHON_BODY");
        result.RelatedArchetypes.Should().Contain("auth/session-tokens");
        result.References.Should().ContainKey("owasp_asvs").WhoseValue.Should().Be("V2.4");
    }

    [Fact]
    public void Consult_LanguageNotInAppliesTo_WithEquivalent_ReturnsRedirect()
    {
        var archetype = Make(
            "memory/safe-string-handling",
            appliesTo: new[] { "c" },
            languageFiles: new[] { ("c", "C_BODY") },
            equivalentsIn: new Dictionary<string, string>
            {
                ["python"] = "io/input-validation"
            });
        var index = KeywordArchetypeIndex.Build(new[] { archetype });
        var service = new ConsultationService(index);

        var result = service.Consult("memory/safe-string-handling", SupportedLanguage.Python);

        result.Redirect.Should().BeTrue();
        result.NotFound.Should().BeFalse();
        result.Content.Should().BeNull();
        result.Suggested.Should().ContainSingle().Which.Should().Be("io/input-validation");
        result.Message.Should().Contain("io/input-validation");
    }

    [Fact]
    public void Consult_LanguageNotInAppliesTo_WithoutEquivalent_ReturnsGenericRedirect()
    {
        var archetype = Make(
            "memory/safe-string-handling",
            appliesTo: new[] { "c" },
            languageFiles: new[] { ("c", "C_BODY") });
        var index = KeywordArchetypeIndex.Build(new[] { archetype });
        var service = new ConsultationService(index);

        var result = service.Consult("memory/safe-string-handling", SupportedLanguage.Python);

        result.Redirect.Should().BeTrue();
        result.Suggested.Should().BeEmpty();
        result.Message.Should().Contain("No direct equivalent");
    }

    [Fact]
    public void Consult_UnknownArchetype_ReturnsNotFound()
    {
        var index = KeywordArchetypeIndex.Build(Array.Empty<Archetype>());
        var service = new ConsultationService(index);

        var result = service.Consult("nope/nope", SupportedLanguage.CSharp);

        result.NotFound.Should().BeTrue();
        result.Redirect.Should().BeFalse();
        result.Content.Should().BeNull();
    }

    [Fact]
    public void Consult_InvalidArchetypeId_Throws()
    {
        var service = new ConsultationService(
            KeywordArchetypeIndex.Build(Array.Empty<Archetype>()));

        var act = () => service.Consult("../../etc/passwd", SupportedLanguage.CSharp);

        act.Should().Throw<ArgumentException>()
           .WithMessage("*not a valid identifier*");
    }
}
