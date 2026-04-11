using GuardCode.Content;
using GuardCode.Content.Indexing;

namespace GuardCode.Content.Tests;

#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

public class KeywordArchetypeIndexTests
{
    private static Archetype MakeArchetype(
        string id,
        string title,
        string summary,
        string[] keywords,
        string[] appliesTo,
        string[]? relatedArchetypes = null)
        => new(
            Id: id,
            Principles: new PrinciplesFrontmatter
            {
                SchemaVersion = 1,
                Archetype = id,
                Title = title,
                Summary = summary,
                AppliesTo = new List<string>(appliesTo),
                Keywords = new List<string>(keywords),
                RelatedArchetypes = new List<string>(relatedArchetypes ?? Array.Empty<string>())
            },
            PrinciplesBody: "body",
            LanguageFiles: new Dictionary<string, LanguageFile>(StringComparer.Ordinal));

    [Fact]
    public void Search_ByKeyword_ReturnsHit()
    {
        var hashing = MakeArchetype(
            "auth/password-hashing",
            "Password Hashing",
            "Hashing and verifying passwords.",
            new[] { "password", "bcrypt", "argon2" },
            new[] { "csharp", "python" });
        var index = KeywordArchetypeIndex.Build(new[] { hashing });

        var hits = index.Search("how do I hash a password", SupportedLanguage.Python, maxResults: 8);

        hits.Should().ContainSingle()
            .Which.ArchetypeId.Should().Be("auth/password-hashing");
    }

    [Fact]
    public void Search_LanguageNotInAppliesTo_FiltersOut()
    {
        var cOnly = MakeArchetype(
            "memory/safe-string-handling",
            "Safe String Handling",
            "Bounds-checked string ops in C.",
            new[] { "string", "buffer", "overflow" },
            new[] { "c" });
        var index = KeywordArchetypeIndex.Build(new[] { cOnly });

        var hits = index.Search("safe string buffer overflow", SupportedLanguage.Python, maxResults: 8);

        hits.Should().BeEmpty();
    }

    [Fact]
    public void Search_MaxResults_IsRespected()
    {
        var archetypes = new List<Archetype>();
        for (var i = 0; i < 12; i++)
        {
            archetypes.Add(MakeArchetype(
                id: $"x/a{i:D2}",
                title: $"Archetype {i}",
                summary: "about passwords and hashing",
                keywords: new[] { "password", "hash" },
                appliesTo: new[] { "csharp" }));
        }
        var index = KeywordArchetypeIndex.Build(archetypes);

        var hits = index.Search("password hash", SupportedLanguage.CSharp, maxResults: 5);

        hits.Should().HaveCount(5);
    }

    [Fact]
    public void GetReverseRelated_IncludesArchetypesThatListThisOne()
    {
        var hashing = MakeArchetype(
            "auth/password-hashing",
            "Password Hashing",
            "sum",
            new[] { "password" },
            new[] { "csharp" });
        var login = MakeArchetype(
            "auth/login-endpoint",
            "Login Endpoint",
            "sum",
            new[] { "login" },
            new[] { "csharp" },
            relatedArchetypes: new[] { "auth/password-hashing", "auth/session-tokens" });

        var index = KeywordArchetypeIndex.Build(new[] { hashing, login });

        index.GetReverseRelated("auth/password-hashing")
             .Should().ContainSingle()
             .Which.Should().Be("auth/login-endpoint");
        index.GetReverseRelated("auth/session-tokens")
             .Should().ContainSingle()
             .Which.Should().Be("auth/login-endpoint");
        index.GetReverseRelated("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void GetById_UnknownArchetype_ReturnsNull()
    {
        var index = KeywordArchetypeIndex.Build(Array.Empty<Archetype>());
        index.GetById("nope/nope").Should().BeNull();
    }
}
