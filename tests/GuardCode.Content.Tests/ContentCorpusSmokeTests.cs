using GuardCode.Content;
using GuardCode.Content.Indexing;
using GuardCode.Content.Loading;
using GuardCode.Content.Services;

namespace GuardCode.Content.Tests;

#pragma warning disable CA1707
// CA1707: xUnit idiomatic Method_State_Expected naming.

/// <summary>
/// Integration smoke test. Loads the *real* <c>archetypes/</c> directory
/// (not a fake temp directory) through the full production pipeline —
/// <see cref="FileSystemArchetypeRepository"/> → <see cref="KeywordArchetypeIndex"/>
/// → <see cref="PrepService"/> / <see cref="ConsultationService"/> — and asserts
/// the MVP archetypes parse, validate, and are discoverable. This is the
/// first line of defense against broken content in CI: unit tests alone
/// cannot catch a typo in a real markdown file.
/// </summary>
public class ContentCorpusSmokeTests
{
    private static string FindArchetypesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SecureCodingMcp.slnx")))
            {
                return Path.Combine(dir.FullName, "archetypes");
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "could not locate SecureCodingMcp.slnx by walking up from the test bin directory");
    }

    [Fact]
    public void RealCorpus_LoadsValidatesAndIndexes()
    {
        var root = FindArchetypesRoot();
        var repo = new FileSystemArchetypeRepository(root);

        var archetypes = repo.LoadAll();

        archetypes.Should().NotBeEmpty(because: "MVP ships with three smoke-test archetypes at minimum");
        archetypes.Should().Contain(a => a.Id == "auth/password-hashing");
        archetypes.Should().Contain(a => a.Id == "io/input-validation");
        archetypes.Should().Contain(a => a.Id == "errors/error-handling");
    }

    [Fact]
    public void Prep_FindsPasswordHashingForHashingIntent()
    {
        var root = FindArchetypesRoot();
        var index = KeywordArchetypeIndex.Build(new FileSystemArchetypeRepository(root).LoadAll());
        var prep = new PrepService(index);

        var result = prep.Prep(
            "I'm about to write a function to hash and verify user passwords",
            SupportedLanguage.Python,
            framework: null);

        result.Matches.Should().NotBeEmpty();
        result.Matches.Should().Contain(m => m.ArchetypeId == "auth/password-hashing");
    }

    [Fact]
    public void Consult_ComposesPrinciplesAndLanguageBody()
    {
        var root = FindArchetypesRoot();
        var index = KeywordArchetypeIndex.Build(new FileSystemArchetypeRepository(root).LoadAll());
        var consult = new ConsultationService(index);

        var result = consult.Consult("auth/password-hashing", SupportedLanguage.Python);

        result.NotFound.Should().BeFalse();
        result.Redirect.Should().BeFalse();
        result.Content.Should().NotBeNull();
        result.Content!.Should().Contain("Password Hashing — Principles");
        result.Content.Should().Contain("Password Hashing — Python");
        result.Content.Should().Contain("\n\n---\n\n");
    }

    [Fact]
    public void Consult_InputValidationInC_HasContent()
    {
        var root = FindArchetypesRoot();
        var index = KeywordArchetypeIndex.Build(new FileSystemArchetypeRepository(root).LoadAll());
        var consult = new ConsultationService(index);

        var result = consult.Consult("io/input-validation", SupportedLanguage.C);

        result.NotFound.Should().BeFalse();
        result.Redirect.Should().BeFalse();
        result.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Consult_PasswordHashingInC_Redirects()
    {
        var root = FindArchetypesRoot();
        var index = KeywordArchetypeIndex.Build(new FileSystemArchetypeRepository(root).LoadAll());
        var consult = new ConsultationService(index);

        var result = consult.Consult("auth/password-hashing", SupportedLanguage.C);

        result.Redirect.Should().BeTrue();
        result.NotFound.Should().BeFalse();
    }
}
