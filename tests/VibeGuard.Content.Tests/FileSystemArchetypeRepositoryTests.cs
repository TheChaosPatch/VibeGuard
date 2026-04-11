#pragma warning disable CA1707, CA1861
// CA1707: xUnit idiomatic Method_State_Expected naming.
// CA1861: inline constant arrays in test fixtures are clearer than hoisted statics.

using VibeGuard.Content;
using VibeGuard.Content.Loading;
using VibeGuard.Content.Validation;

namespace VibeGuard.Content.Tests;

public sealed class FileSystemArchetypeRepositoryTests : IDisposable
{
    private readonly string _rootDir;

    public FileSystemArchetypeRepositoryTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "vibeguard-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir))
        {
            Directory.Delete(_rootDir, recursive: true);
        }
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_rootDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private const string ValidPrinciples =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        title: Password Hashing
        summary: Summary.
        applies_to: [csharp]
        status: stable
        author: ehabhussein
        reviewed_by: [ehabhussein]
        stable_since: "2026-04-11"
        keywords: [password]
        ---

        # Password Hashing — Principles

        ## When this applies
        When storing passwords.

        ## Architectural placement
        At the auth boundary.

        ## Principles
        Use a slow KDF.

        ## Anti-patterns
        Don't use MD5.

        ## References
        OWASP ASVS V2.4.
        """;

    private const string ValidCsharp =
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

        # Password Hashing — C#

        ## Library choice
        Konscious.

        ## Reference implementation
        ```csharp
        void H() { }
        ```

        ## Language-specific gotchas
        Watch out.

        ## Tests to write
        Shape tests.
        """;

    private const string ValidInputValidationPrinciples =
        """
        ---
        schema_version: 1
        archetype: io/input-validation
        title: Input Validation
        summary: Summary.
        applies_to: [csharp]
        status: stable
        author: ehabhussein
        reviewed_by: [ehabhussein]
        stable_since: "2026-04-11"
        keywords: [input, validation]
        ---

        # Input Validation — Principles

        ## When this applies
        At every trust boundary.

        ## Architectural placement
        At edges.

        ## Principles
        Reject invalid early.

        ## Anti-patterns
        Blacklists.

        ## References
        OWASP cheat sheet.
        """;

    [Fact]
    public void LoadAll_TwoArchetypes_ReturnsBoth()
    {
        WriteFile("auth/password-hashing/_principles.md", ValidPrinciples);
        WriteFile("auth/password-hashing/csharp.md", ValidCsharp);
        WriteFile("io/input-validation/_principles.md", ValidInputValidationPrinciples);

        var repo = new FileSystemArchetypeRepository(_rootDir);
        var archetypes = repo.LoadAll();

        archetypes.Should().HaveCount(2);
        archetypes.Should().Contain(a => a.Id == "auth/password-hashing");
        archetypes.Should().Contain(a => a.Id == "io/input-validation");
    }

    [Fact]
    public void Ctor_NonExistentDirectory_Throws()
    {
        var act = () => new FileSystemArchetypeRepository(
            Path.Combine(_rootDir, "does-not-exist"));
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void LoadAll_DirectoryWithoutPrinciples_IsIgnored()
    {
        // Just a stray language file with no _principles.md — should be ignored silently,
        // not cause a validation failure, because it's not a claimed archetype yet.
        WriteFile("draft/something/csharp.md", ValidCsharp);

        var repo = new FileSystemArchetypeRepository(_rootDir);
        var archetypes = repo.LoadAll();

        archetypes.Should().BeEmpty();
    }

    [Fact]
    public void EnsureUnderRoot_CandidateInsideRoot_DoesNotThrow()
    {
        var root = Path.GetFullPath(_rootDir) + Path.DirectorySeparatorChar;
        var candidate = Path.Combine(root, "auth", "password-hashing", "csharp.md");

        var act = () => FileSystemArchetypeRepository.EnsureUnderRoot(root, candidate);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureUnderRoot_CandidateOutsideRoot_Throws()
    {
        var root = Path.GetFullPath(_rootDir) + Path.DirectorySeparatorChar;
        // A hand-constructed absolute path that lexically lives outside the root.
        var candidate = Path.GetFullPath(Path.Combine(_rootDir, "..", "evil.md"));

        var act = () => FileSystemArchetypeRepository.EnsureUnderRoot(root, candidate);

        act.Should().Throw<ArchetypeLoadException>()
            .WithMessage("*refusing to load file outside archetypes root*");
    }

    // -------------------- Lifecycle filtering --------------------

    // Drafts deliberately skip author/reviewed_by/stable_since — those are
    // stable-only requirements. The required body sections are still present
    // so the validator doesn't reject the content for unrelated reasons.
    private const string ValidDraftPrinciples =
        """
        ---
        schema_version: 1
        archetype: io/draft-thing
        title: Draft Thing
        summary: Summary.
        applies_to: [csharp]
        status: draft
        keywords: [draft]
        ---

        # Draft Thing — Principles

        ## When this applies
        While drafting.

        ## Architectural placement
        At edges.

        ## Principles
        Be tentative.

        ## Anti-patterns
        Shipping drafts.

        ## References
        N/A.
        """;

    [Fact]
    public void LoadAll_DraftArchetype_ExcludedByDefault()
    {
        WriteFile("auth/password-hashing/_principles.md", ValidPrinciples);
        WriteFile("auth/password-hashing/csharp.md", ValidCsharp);
        WriteFile("io/draft-thing/_principles.md", ValidDraftPrinciples);

        var repo = new FileSystemArchetypeRepository(_rootDir);
        var archetypes = repo.LoadAll();

        archetypes.Should().ContainSingle()
                  .Which.Id.Should().Be("auth/password-hashing");
    }

    [Fact]
    public void LoadAll_DraftArchetype_IncludedWhenFlagSet()
    {
        WriteFile("auth/password-hashing/_principles.md", ValidPrinciples);
        WriteFile("auth/password-hashing/csharp.md", ValidCsharp);
        WriteFile("io/draft-thing/_principles.md", ValidDraftPrinciples);

        var repo = new FileSystemArchetypeRepository(_rootDir, includeDrafts: true);
        var archetypes = repo.LoadAll();

        archetypes.Should().HaveCount(2);
        archetypes.Should().Contain(a => a.Id == "io/draft-thing"
                                      && a.Principles.Status == ArchetypeStatus.Draft);
    }

    [Fact]
    public void LoadAll_BrokenDraft_FailsValidationEvenWhenHidden()
    {
        // Key invariant: drafts are hidden from the active corpus by default,
        // but they are still parsed and validated so CI catches breakage in
        // in-progress archetypes before they get merged.
        const string BrokenDraftPrinciples =
            """
            ---
            schema_version: 1
            archetype: io/draft-thing
            title: Draft Thing
            summary: Summary.
            applies_to: [csharp]
            status: draft
            superseded_by: io/other-thing
            keywords: [draft]
            ---

            # Draft Thing — Principles

            ## When this applies
            x

            ## Architectural placement
            x

            ## Principles
            x

            ## Anti-patterns
            x

            ## References
            x
            """;
        WriteFile("io/draft-thing/_principles.md", BrokenDraftPrinciples);

        var repo = new FileSystemArchetypeRepository(_rootDir);

        var act = () => repo.LoadAll();

        act.Should().Throw<ArchetypeValidationException>()
           .WithMessage("*'superseded_by' is only valid when status is 'deprecated'*");
    }
}
