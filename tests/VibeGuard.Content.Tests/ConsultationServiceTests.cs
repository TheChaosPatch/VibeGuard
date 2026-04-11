using System.Collections.Frozen;
using VibeGuard.Content;
using VibeGuard.Content.Indexing;
using VibeGuard.Content.Services;

namespace VibeGuard.Content.Tests;

#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

public class ConsultationServiceTests
{
    private static readonly SupportedLanguageSet DefaultLanguages = SupportedLanguageSet.Default();

    private static ConsultationService BuildService(params Archetype[] archetypes)
        => new(KeywordArchetypeIndex.Build(archetypes), DefaultLanguages);

    private static Archetype Make(
        string id,
        string[] appliesTo,
        (string lang, string body)[] languageFiles,
        string principlesBody = "PRINCIPLES_BODY",
        string[]? relatedArchetypes = null,
        IReadOnlyDictionary<string, string>? equivalentsIn = null,
        IReadOnlyDictionary<string, string>? references = null)
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
                AppliesTo = [.. appliesTo],
                Keywords = ["k"],
                RelatedArchetypes = [.. relatedArchetypes ?? []],
                EquivalentsIn = equivalentsIn ?? FrozenDictionary<string, string>.Empty,
                References = references ?? FrozenDictionary<string, string>.Empty
            },
            PrinciplesBody: principlesBody,
            LanguageFiles: langMap);
    }

    [Fact]
    public void Consult_ValidArchetypeAndLanguage_ComposesPrinciplesAndLanguageBody()
    {
        var service = BuildService(Make(
            "auth/password-hashing",
            appliesTo: new[] { "csharp", "python" },
            languageFiles: new[] { ("python", "PYTHON_BODY") },
            principlesBody: "PRINCIPLES_BODY",
            relatedArchetypes: new[] { "auth/session-tokens" },
            references: new Dictionary<string, string>
            {
                ["owasp_asvs"] = "V2.4",
                ["cwe"] = "916"
            }));

        var result = service.Consult("auth/password-hashing", "python");

        result.NotFound.Should().BeFalse();
        result.Redirect.Should().BeFalse();
        result.Content.Should().Be("PRINCIPLES_BODY\n\n---\n\nPYTHON_BODY");
        result.RelatedArchetypes.Should().Contain("auth/session-tokens");
        result.References.Should().ContainKey("owasp_asvs").WhoseValue.Should().Be("V2.4");
    }

    [Fact]
    public void Consult_LanguageNotInAppliesTo_WithEquivalent_ReturnsRedirect()
    {
        var service = BuildService(Make(
            "memory/safe-string-handling",
            appliesTo: new[] { "c" },
            languageFiles: new[] { ("c", "C_BODY") },
            equivalentsIn: new Dictionary<string, string>
            {
                ["python"] = "io/input-validation"
            }));

        var result = service.Consult("memory/safe-string-handling", "python");

        result.Redirect.Should().BeTrue();
        result.NotFound.Should().BeFalse();
        result.Content.Should().BeNull();
        result.Suggested.Should().ContainSingle().Which.Should().Be("io/input-validation");
        result.Message.Should().Contain("io/input-validation");
        result.Message.Should().Contain("Archetype 'memory/safe-string-handling' does not apply to python");
        result.Message.Should().Contain("See 'io/input-validation' for the equivalent guidance in python");
    }

    [Fact]
    public void Consult_LanguageNotInAppliesTo_WithoutEquivalent_ReturnsGenericRedirect()
    {
        var service = BuildService(Make(
            "memory/safe-string-handling",
            appliesTo: new[] { "c" },
            languageFiles: new[] { ("c", "C_BODY") }));

        var result = service.Consult("memory/safe-string-handling", "python");

        result.Redirect.Should().BeTrue();
        result.Suggested.Should().BeEmpty();
        result.Message.Should().Contain("No direct equivalent");
        result.Message.Should().Contain("Archetype 'memory/safe-string-handling' does not apply to python");
        result.Message.Should().Contain("No direct equivalent is registered");
        result.Message.Should().Contain("consider searching with prep()");
    }

    [Fact]
    public void Consult_UnknownArchetype_ReturnsNotFound()
    {
        var service = BuildService();

        var result = service.Consult("nope/nope", "csharp");

        result.NotFound.Should().BeTrue();
        result.Redirect.Should().BeFalse();
        result.Content.Should().BeNull();
        result.Message.Should().Be("Archetype 'nope/nope' was not found.");
    }

    [Fact]
    public void Consult_InvalidArchetypeId_Throws()
    {
        var service = BuildService();

        var act = () => service.Consult("../../etc/passwd", "csharp");

        act.Should().Throw<ArgumentException>()
           .WithMessage("*not a valid identifier*");
    }

    [Fact]
    public void Consult_UnsupportedLanguage_Throws()
    {
        var service = BuildService(Make(
            "auth/password-hashing",
            appliesTo: new[] { "csharp" },
            languageFiles: new[] { ("csharp", "CSHARP_BODY") }));

        var act = () => service.Consult("auth/password-hashing", "klingon");

        act.Should().Throw<ArgumentException>()
           .WithMessage("*'klingon'*not supported*")
           .And.Message.Should().Contain("csharp");
    }

    [Fact]
    public void Consult_DeprecatedArchetype_PrependsDeprecationBanner()
    {
        // A deprecated archetype with a successor should still serve content
        // (so existing callers don't hard-fail on upgrade) but the response
        // must lead with a DEPRECATED banner naming the successor, so LLM
        // clients can pattern-match on it and steer the user away.
        var archetype = new Archetype(
            Id: "auth/legacy-password-hashing",
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = "auth/legacy-password-hashing",
                Title = "Legacy Password Hashing",
                Summary = "s",
                AppliesTo = ["csharp"],
                Keywords = ["k"],
                Status = ArchetypeStatus.Deprecated,
                SupersededBy = "auth/password-hashing",
            },
            PrinciplesBody: "PRINCIPLES_BODY",
            LanguageFiles: new Dictionary<string, LanguageFile>(StringComparer.Ordinal)
            {
                ["csharp"] = new LanguageFile(
                    new LanguageFrontmatter
                    {
                        SchemaVersion = 1,
                        Archetype = "auth/legacy-password-hashing",
                        Language = "csharp",
                        PrinciplesFile = "_principles.md",
                        Libraries = new LibrariesSection { Preferred = "lib" }
                    },
                    "CSHARP_BODY"),
            });
        var service = new ConsultationService(
            KeywordArchetypeIndex.Build(new[] { archetype }),
            DefaultLanguages);

        var result = service.Consult("auth/legacy-password-hashing", "csharp");

        result.NotFound.Should().BeFalse();
        result.Redirect.Should().BeFalse();
        result.Content.Should().NotBeNull();
        result.Content!.Should().StartWith("> **DEPRECATED**");
        result.Content.Should().Contain("auth/password-hashing");
        // The original composition must still be present after the banner.
        result.Content.Should().Contain("PRINCIPLES_BODY");
        result.Content.Should().Contain("CSHARP_BODY");
    }

    [Fact]
    public void Consult_StableArchetype_DoesNotPrependDeprecationBanner()
    {
        var service = BuildService(Make(
            "auth/password-hashing",
            appliesTo: new[] { "csharp" },
            languageFiles: new[] { ("csharp", "CSHARP_BODY") }));

        var result = service.Consult("auth/password-hashing", "csharp");

        result.Content.Should().NotBeNull();
        result.Content!.Should().NotContain("DEPRECATED");
    }

    [Fact]
    public void Consult_AppliesToListsLanguageButFileMissing_ReturnsNotFoundWithDisconnectMessage()
    {
        var service = BuildService(Make(
            "memory/safe-string-handling",
            appliesTo: new[] { "c", "python" },
            languageFiles: new[] { ("c", "C_BODY") }));

        var result = service.Consult("memory/safe-string-handling", "python");

        result.NotFound.Should().BeTrue();
        result.Redirect.Should().BeFalse();
        result.Content.Should().BeNull();
        result.Message.Should().Contain("lists python in applies_to");
        result.Message.Should().Contain("but no language file exists on disk");
    }
}
