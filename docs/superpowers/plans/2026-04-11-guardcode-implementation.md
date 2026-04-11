# GuardCode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an MCP stdio server in C# 14 / .NET 10 that serves human-authored, per-archetype engineering guidance (`prep` and `consult` tools), backed by a validated, keyword-indexed markdown corpus with 3 smoke-test archetypes.

**Architecture:** Three-project solution — `GuardCode.Mcp` (executable, composition root, MCP tool handlers), `GuardCode.Content` (domain, loading, indexing, services), `GuardCode.Content.Tests` (xUnit + AwesomeAssertions). Content lives in a sibling `archetypes/` directory as plain markdown with YAML frontmatter. All content is eager-loaded and validated at startup; request-path is pure in-memory lookup.

**Tech Stack:** C# 14, .NET 10, `ModelContextProtocol` (official MCP C# SDK, Microsoft-co-maintained), `YamlDotNet` (strict typed deserializer), `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `xunit.v3`, `AwesomeAssertions`. Solution file uses the new `.slnx` format (XML-based, default in .NET 10 SDK).

**Design source:** `docs/superpowers/specs/2026-04-11-guardcode-design.md` — read §3 (MCP contract), §4 (content schema), §5 (architecture), §6 (security) before starting.

**Working directory for all commands:** `F:\repositories\SecureCodingMcp` (use forward slashes in bash: `F:/repositories/SecureCodingMcp`).

**NuGet policy — use the latest stable version at implementation time, managed centrally:**

All NuGet versions live in `Directory.Packages.props` at the repo root (Central Package Management, `ManagePackageVersionsCentrally=true`). The three csproj files reference package IDs **without** version attributes. To bump a dependency, edit one file.

Do **not** pin older versions. For every package below, take whatever `dotnet package search <id>` reports as the current stable release (or the latest preview if the package has no stable line yet). For reference, these were the latest stable versions at the time the plan was written (2026-04-11):

- `ModelContextProtocol` — 1.2.0 (official MCP C# SDK — owner `ModelContextProtocol` on nuget.org, repo `modelcontextprotocol/csharp-sdk`; this is the canonical Microsoft-co-maintained package — do NOT substitute community forks like `MCPSharp` or `mcpdotnet`)
- `YamlDotNet` — 17.0.1
- `Microsoft.Extensions.Hosting` — 10.0.5
- `Microsoft.Extensions.Logging.Console` — 10.0.5
- `xunit.v3` — 3.2.2 (use xunit v3, not v2)
- `xunit.runner.visualstudio` — 3.1.5
- `AwesomeAssertions` — 9.4.0 (Apache-2.0 community fork of FluentAssertions 7.x; used instead of `FluentAssertions` because FluentAssertions 8+ became commercial under Xceed — incompatible with GuardCode's MIT-for-free-adoption goal)
- `Microsoft.NET.Test.Sdk` — 18.4.0
- `Microsoft.CodeAnalysis.NetAnalyzers` — 10.0.201

If a newer stable exists at implementation time, **use it**. If a later `ModelContextProtocol` version introduces a breaking API change, adapt the code — don't downgrade the package to match stale example code in this plan.

**Design principles (enforced, not aspirational):**

These are the rules of the road. Every task below complies with them; deviations from them in a review are changes, not observations.

1. **Layered sub-namespaces — layer N depends only on layers 0..N−1.** GuardCode's layers are:
   - Layer 0: **Domain** — `GuardCode.Content` root (`SupportedLanguage`, `Archetype`, `PrinciplesFrontmatter`, `LanguageFrontmatter`, `LanguageFile`)
   - Layer 1: **Loading** — `GuardCode.Content.Loading` (parsing, reading, path-traversal defense)
   - Layer 2: **Validation** — `GuardCode.Content.Validation`
   - Layer 3: **Indexing** — `GuardCode.Content.Indexing`
   - Layer 4: **Services** — `GuardCode.Content.Services`
   - Layer 5: **MCP tool handlers** — `GuardCode.Mcp.Tools`
   - Layer 6: **Composition root / host** — `GuardCode.Mcp.Program`
   A file in Layer 3 may `using` Layers 0–2, never Layer 4+. Circular references are a bug, not a style preference.

2. **Single Responsibility, measured in LOC.** Production files cap at **200 lines** (design spec §7.3). Reference-implementation function budgets cap at **40 lines**. If a file grows past the budget, it splits.

3. **No static utility classes except pure-function helpers.** Every service is DI-injected through an interface. The one intentional static is `SupportedLanguageExtensions` — a pure wire-format mapper with no state and no dependencies.

4. **Immutable-after-construction collections use `FrozenDictionary<K,V>` / `FrozenSet<T>`.** The keyword index, archetype-id → archetype map, and reverse `related_archetypes` index are all built once at startup and never mutated. `FrozenDictionary` expresses that intent and gives faster reads than `Dictionary`.

5. **One DI extension method per library.** `GuardCode.Content` exposes `AddGuardCodeContent(this IServiceCollection, string archetypesRoot)`; `GuardCode.Mcp` composes it in `Program.cs`. No direct `services.AddSingleton<...>` calls in the host except what belongs to the host itself.

(Note: `required` modifiers on frontmatter records were evaluated and deferred — YamlDotNet's reflection-based construction path interacts awkwardly with `required`, and the existing "default to empty, validate at load time" pattern produces better error messages than a runtime reflection failure. Reconsider if YamlDotNet adds first-class `required` enforcement.)

These principles are informed by the Cortex Refactor1 design doc Ehab shared on 2026-04-11 (see `reference_cortex_refactor1.md` in the project memory). GuardCode deliberately does **not** adopt Cortex's "more granularity the better" rule — three projects at the right grain beats 99 projects at the wrong one.

**TDD discipline:** every production file in `GuardCode.Content` is written test-first. Every task has explicit fail → implement → pass → commit steps. No batching of tests and implementation into a single commit.

---

## File Structure

Files created (in order of first appearance):

- `SecureCodingMcp.slnx` — solution
- `Directory.Build.props` — target framework, language version, strict analyzers, nullable, `InvariantGlobalization`
- `Directory.Packages.props` — Central Package Management: single source of truth for every NuGet version
- `src/GuardCode.Content/GuardCode.Content.csproj` — class library
- `src/GuardCode.Content/SupportedLanguage.cs` — MVP language enum
- `src/GuardCode.Content/PrinciplesFrontmatter.cs` — typed YAML model for `_principles.md`
- `src/GuardCode.Content/LanguageFrontmatter.cs` — typed YAML model for `<lang>.md`
- `src/GuardCode.Content/LanguageFile.cs` — record holding language-file frontmatter + body
- `src/GuardCode.Content/Archetype.cs` — aggregate record (principles + all language files)
- `src/GuardCode.Content/Loading/FrontmatterParser.cs` — splits frontmatter from body, strict YAML deserialize
- `src/GuardCode.Content/Loading/FrontmatterParseException.cs` — thrown on malformed / unknown / missing fields
- `src/GuardCode.Content/Loading/ArchetypeLoader.cs` — groups parsed files into `Archetype` aggregates
- `src/GuardCode.Content/Validation/ArchetypeValidationException.cs` — thrown on body/budget violations
- `src/GuardCode.Content/Validation/ArchetypeValidator.cs` — body sections, line budgets, code budgets
- `src/GuardCode.Content/Loading/IArchetypeRepository.cs` — loader interface
- `src/GuardCode.Content/Loading/FileSystemArchetypeRepository.cs` — reads disk, path-traversal defense
- `src/GuardCode.Content/Indexing/PrepMatch.cs` — scored search hit DTO
- `src/GuardCode.Content/Indexing/IArchetypeIndex.cs` — index interface
- `src/GuardCode.Content/Indexing/KeywordArchetypeIndex.cs` — inverted + reverse-related index
- `src/GuardCode.Content/Services/PrepResult.cs` — DTO
- `src/GuardCode.Content/Services/IPrepService.cs`
- `src/GuardCode.Content/Services/PrepService.cs` — tokenize, filter, score, top-8
- `src/GuardCode.Content/Services/ConsultResult.cs` — DTO (normal + redirect + not-found variants)
- `src/GuardCode.Content/Services/IConsultationService.cs`
- `src/GuardCode.Content/Services/ConsultationService.cs` — composition, redirect, not-found
- `src/GuardCode.Mcp/GuardCode.Mcp.csproj` — executable
- `src/GuardCode.Mcp/Program.cs` — generic host, DI, stdio MCP
- `src/GuardCode.Mcp/Tools/PrepTool.cs` — `[McpServerTool]` for `prep`
- `src/GuardCode.Mcp/Tools/ConsultTool.cs` — `[McpServerTool]` for `consult`
- `tests/GuardCode.Content.Tests/GuardCode.Content.Tests.csproj`
- `tests/GuardCode.Content.Tests/FrontmatterParserTests.cs`
- `tests/GuardCode.Content.Tests/ArchetypeLoaderTests.cs`
- `tests/GuardCode.Content.Tests/ArchetypeValidatorTests.cs`
- `tests/GuardCode.Content.Tests/FileSystemArchetypeRepositoryTests.cs`
- `tests/GuardCode.Content.Tests/KeywordArchetypeIndexTests.cs`
- `tests/GuardCode.Content.Tests/PrepServiceTests.cs`
- `tests/GuardCode.Content.Tests/ConsultationServiceTests.cs`
- `tests/GuardCode.Content.Tests/ContentCorpusSmokeTests.cs` — loads real `archetypes/`
- `archetypes/auth/password-hashing/_principles.md` + `csharp.md` + `python.md`
- `archetypes/io/input-validation/_principles.md` + `csharp.md` + `python.md` + `c.md`
- `archetypes/errors/error-handling/_principles.md` + `csharp.md` + `go.md`
- `README.md` — contains "GUARD — Global Unified AI Rules for Development" verbatim
- `CONTRIBUTING.md` — schema documentation

---

## Task 1: Solution Scaffold

**Files:**
- Create: `SecureCodingMcp.slnx`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `src/GuardCode.Content/GuardCode.Content.csproj`
- Create: `src/GuardCode.Mcp/GuardCode.Mcp.csproj`
- Create: `src/GuardCode.Mcp/GlobalUsings.cs`
- Create: `tests/GuardCode.Content.Tests/GuardCode.Content.Tests.csproj`
- Create: `tests/GuardCode.Content.Tests/GlobalUsings.cs`

- [ ] **Step 1: Verify .NET 10 SDK is installed**

Run: `dotnet --version`
Expected: a version string starting with `10.` (e.g., `10.0.100`). If not, install the .NET 10 SDK before continuing.

- [ ] **Step 2: Create solution file**

Run:
```bash
cd F:/repositories/SecureCodingMcp
dotnet new sln -n SecureCodingMcp
```
Expected: `SecureCodingMcp.slnx` created at repo root. `dotnet sln list` shows no projects yet.

- [ ] **Step 3: Create `Directory.Build.props` at repo root**

Create `F:/repositories/SecureCodingMcp/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

Rationale: centralizes target framework, C# version, nullable, and analyzer config so every project inherits the same strict baseline. `TreatWarningsAsErrors` is deliberate — a project that teaches discipline must practice it.

- [ ] **Step 3a: Create `Directory.Packages.props` at repo root**

Create `F:/repositories/SecureCodingMcp/Directory.Packages.props`:

```xml
<Project>

  <!--
    Central Package Management for GuardCode.
    Every NuGet version lives here. The three csproj files reference
    packages by ID only (no Version attribute). To update a dependency,
    change the version in one place and rebuild.
    Policy: always use the latest stable version.
    Versions below were current at 2026-04-11; bump freely.
  -->

  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <!-- GuardCode.Content -->
    <PackageVersion Include="YamlDotNet" Version="17.0.1" />
    <PackageVersion Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="10.0.201" />

    <!-- GuardCode.Mcp -->
    <PackageVersion Include="ModelContextProtocol" Version="1.2.0" />
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.5" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Console" Version="10.0.5" />

    <!-- GuardCode.Content.Tests -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="AwesomeAssertions" Version="9.4.0" />
  </ItemGroup>

</Project>
```

Rationale: Central Package Management (CPM) gives one file to edit when a dependency moves. With `ManagePackageVersionsCentrally=true`, csproj files must reference packages **without** a `Version` attribute — that's what the next three steps do. `CentralPackageTransitivePinningEnabled=true` also prevents transitive versions from drifting silently.

Do not put `--` inside the XML comment block — XML does not allow `--` inside comments.

- [ ] **Step 4: Create the content class library**

Run:
```bash
dotnet new classlib -n GuardCode.Content -o src/GuardCode.Content --framework net10.0
dotnet sln SecureCodingMcp.slnx add src/GuardCode.Content/GuardCode.Content.csproj
```

Then overwrite `src/GuardCode.Content/GuardCode.Content.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>GuardCode.Content</RootNamespace>
    <AssemblyName>GuardCode.Content</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="YamlDotNet" />
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

(No `Version="..."` attributes — versions come from `Directory.Packages.props`. If a reference here carries a version, restore will fail with `NU1008`.)

Also delete the template file that classlib creates:
```bash
rm src/GuardCode.Content/Class1.cs
```

- [ ] **Step 5: Create the MCP executable project**

Run:
```bash
dotnet new console -n GuardCode.Mcp -o src/GuardCode.Mcp --framework net10.0
dotnet sln SecureCodingMcp.slnx add src/GuardCode.Mcp/GuardCode.Mcp.csproj
```

Then overwrite `src/GuardCode.Mcp/GuardCode.Mcp.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>GuardCode.Mcp</RootNamespace>
    <AssemblyName>guardcode-mcp</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\GuardCode.Content\GuardCode.Content.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" />
  </ItemGroup>
</Project>
```

(`ModelContextProtocol` is the official MCP C# SDK — owner `ModelContextProtocol` on nuget.org, repo `modelcontextprotocol/csharp-sdk`, co-maintained with Microsoft. `AssemblyName` produces the `guardcode-mcp` binary per spec §0 naming. Versions come from `Directory.Packages.props`.) Overwrite the default `Program.cs` with a stub we'll replace in Task 11:

```csharp
// Placeholder — replaced in Task 11 (composition root).
System.Console.Error.WriteLine("guardcode-mcp: composition root not yet implemented");
return 1;
```

Also create `src/GuardCode.Mcp/GlobalUsings.cs`:

```csharp
// Host-project global usings.
// GuardCode.Mcp is a generic-host MCP server: DI, hosting, and logging
// appear in nearly every file, so they're hoisted here. Keep this list
// tight — if a using is only touched by one or two files, it belongs
// in those files, not here.
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
```

Later Task 10 and Task 11 code examples omit these three `using` lines — they're provided globally. If you paste plan code verbatim and get a CS0105 "duplicate using directive" error, drop the redundant line, don't add a `#pragma`.

- [ ] **Step 6: Create the test project**

Run:
```bash
dotnet new xunit -n GuardCode.Content.Tests -o tests/GuardCode.Content.Tests --framework net10.0
dotnet sln SecureCodingMcp.slnx add tests/GuardCode.Content.Tests/GuardCode.Content.Tests.csproj
```

Then overwrite `tests/GuardCode.Content.Tests/GuardCode.Content.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>GuardCode.Content.Tests</RootNamespace>
    <AssemblyName>GuardCode.Content.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GuardCode.Content\GuardCode.Content.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="AwesomeAssertions" />
  </ItemGroup>
</Project>
```

(Use `xunit.v3`, not the legacy `xunit` v2 package. `AwesomeAssertions` is the Apache-2.0 community fork of FluentAssertions 7 — FluentAssertions 8+ is under a commercial Xceed license and must NOT be used here: GuardCode is MIT-licensed for free adoption by LLM toolmakers, and a commercial test dependency would poison that. Versions come from `Directory.Packages.props`.)

Also create `tests/GuardCode.Content.Tests/GlobalUsings.cs`:

```csharp
// Test-project global usings.
// Every test file leans on these — hoisting them keeps per-file headers to
// what's *specific* about that test (the subsystem under test, helper
// namespaces, etc.).
global using Xunit;
global using AwesomeAssertions;
```

Every test-file code example in later tasks omits `using Xunit;` and `using AwesomeAssertions;` — they're global. Keep sub-namespace imports (`using GuardCode.Content.Loading;` etc.) explicit per test file; globalizing every sub-namespace hides which subsystem a test actually targets.

Delete the `xunit`-template `UnitTest1.cs`:
```bash
rm tests/GuardCode.Content.Tests/UnitTest1.cs
```

- [ ] **Step 7: Restore and build the empty solution**

Run:
```bash
dotnet restore SecureCodingMcp.slnx
dotnet build SecureCodingMcp.slnx -c Debug
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The content library is empty, the MCP exe has only a 2-line stub, and the test project has no tests. The build must pass cleanly.

If any package version fails to resolve, check https://www.nuget.org/packages/<package-id> for the current latest and use that — do not downgrade, and do not add a floating `*` version. Update the plan header's reference list if you take a newer version than the one noted there.

- [ ] **Step 8: Commit**

```bash
git add SecureCodingMcp.slnx Directory.Build.props Directory.Packages.props src/GuardCode.Content/ src/GuardCode.Mcp/ tests/GuardCode.Content.Tests/
git commit -m "scaffold: three-project .slnx solution for GuardCode MCP server

Adds an empty C# 14 / .NET 10 solution (new .slnx format, default in
the .NET 10 SDK) with the three projects from the design spec (§5.1):
GuardCode.Content class library, GuardCode.Mcp executable, and
GuardCode.Content.Tests xUnit project. All projects inherit strict
analyzer + nullable + TreatWarningsAsErrors settings from
Directory.Build.props so the server embodies the discipline it teaches.
GlobalUsings.cs files hoist the host-project Microsoft.Extensions.*
and test-project Xunit/AwesomeAssertions namespaces."
```

---

## Task 2: Domain Types

**Files:**
- Create: `src/GuardCode.Content/SupportedLanguage.cs`
- Create: `src/GuardCode.Content/PrinciplesFrontmatter.cs`
- Create: `src/GuardCode.Content/LanguageFrontmatter.cs`
- Create: `src/GuardCode.Content/LanguageFile.cs`
- Create: `src/GuardCode.Content/Archetype.cs`

These are pure data records with no logic, so they don't get dedicated tests — downstream tests exercise them as a side effect. The build itself is the first verification.

- [ ] **Step 1: Create `SupportedLanguage.cs`**

Create `src/GuardCode.Content/SupportedLanguage.cs`:

```csharp
namespace GuardCode.Content;

/// <summary>
/// The MVP language set for GuardCode per design spec §7.1.
/// Deliberately excludes JavaScript and Java — GuardCode targets
/// backend and systems engineers.
/// </summary>
public enum SupportedLanguage
{
    CSharp,
    Python,
    C,
    Go
}

public static class SupportedLanguageExtensions
{
    /// <summary>
    /// Returns the canonical lowercase wire form used in frontmatter,
    /// filenames, and the MCP tool contract ("csharp", "python", "c", "go").
    /// </summary>
    public static string ToWireString(this SupportedLanguage language) => language switch
    {
        SupportedLanguage.CSharp => "csharp",
        SupportedLanguage.Python => "python",
        SupportedLanguage.C => "c",
        SupportedLanguage.Go => "go",
        _ => throw new System.ArgumentOutOfRangeException(nameof(language), language, null)
    };

    /// <summary>
    /// Parses the wire form back to the enum. Returns false for anything
    /// not in the MVP set — caller decides how to surface the error.
    /// </summary>
    public static bool TryParseWire(string? value, out SupportedLanguage language)
    {
        switch (value)
        {
            case "csharp": language = SupportedLanguage.CSharp; return true;
            case "python": language = SupportedLanguage.Python; return true;
            case "c": language = SupportedLanguage.C; return true;
            case "go": language = SupportedLanguage.Go; return true;
            default: language = default; return false;
        }
    }
}
```

- [ ] **Step 2: Create `PrinciplesFrontmatter.cs`**

Create `src/GuardCode.Content/PrinciplesFrontmatter.cs`:

```csharp
using System.Collections.Generic;

namespace GuardCode.Content;

/// <summary>
/// Typed YAML frontmatter for an archetype's <c>_principles.md</c> file.
/// Fields map 1:1 to design spec §4.1. Property initialization with
/// <c>required</c> is enforced by the strict YamlDotNet deserializer
/// configured in <see cref="Loading.FrontmatterParser"/>.
/// </summary>
public sealed class PrinciplesFrontmatter
{
    public int SchemaVersion { get; set; }
    public string Archetype { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> AppliesTo { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<string> RelatedArchetypes { get; set; } = new();
    public Dictionary<string, string> EquivalentsIn { get; set; } = new();
    public Dictionary<string, string> References { get; set; } = new();
}
```

Note: we use mutable `set` properties (not `init`) because YamlDotNet writes through setters during deserialization. The objects are never mutated after construction — the loader treats them as immutable and hands out only read-only views through the `Archetype` record.

- [ ] **Step 3: Create `LanguageFrontmatter.cs`**

Create `src/GuardCode.Content/LanguageFrontmatter.cs`:

```csharp
using System.Collections.Generic;

namespace GuardCode.Content;

/// <summary>
/// Typed YAML frontmatter for an archetype's language file
/// (<c>csharp.md</c>, <c>python.md</c>, etc.). See design spec §4.2.
/// </summary>
public sealed class LanguageFrontmatter
{
    public int SchemaVersion { get; set; }
    public string Archetype { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string? Framework { get; set; }
    public string PrinciplesFile { get; set; } = string.Empty;
    public LibrariesSection Libraries { get; set; } = new();
    public Dictionary<string, string> MinimumVersions { get; set; } = new();
}

public sealed class LibrariesSection
{
    public string Preferred { get; set; } = string.Empty;
    public List<string> Acceptable { get; set; } = new();
    public List<AvoidedLibrary> Avoid { get; set; } = new();
}

public sealed class AvoidedLibrary
{
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Create `LanguageFile.cs`**

Create `src/GuardCode.Content/LanguageFile.cs`:

```csharp
namespace GuardCode.Content;

/// <summary>
/// One language-specific guidance file for an archetype:
/// its parsed frontmatter plus the markdown body (frontmatter stripped).
/// </summary>
public sealed record LanguageFile(
    LanguageFrontmatter Frontmatter,
    string Body);
```

- [ ] **Step 5: Create `Archetype.cs`**

Create `src/GuardCode.Content/Archetype.cs`:

```csharp
using System.Collections.Generic;

namespace GuardCode.Content;

/// <summary>
/// An archetype aggregate: its identifier, principles file, and
/// all available language files keyed by wire-form language string.
/// Constructed once at startup by <see cref="Loading.ArchetypeLoader"/>
/// and never mutated afterwards.
/// </summary>
public sealed record Archetype(
    string Id,
    PrinciplesFrontmatter Principles,
    string PrinciplesBody,
    IReadOnlyDictionary<string, LanguageFile> LanguageFiles);
```

- [ ] **Step 6: Build to verify**

Run: `dotnet build SecureCodingMcp.slnx -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If the strict analyzer set complains about any property, fix the specific complaint before committing — do not disable the analyzer globally.

- [ ] **Step 7: Commit**

```bash
git add src/GuardCode.Content/
git commit -m "content: add domain types for archetypes and frontmatter

Introduces the data records from design spec §5.3 and §4:
SupportedLanguage enum with wire-form parse/serialize, and the
typed YAML frontmatter models for principles and language files.
Pure data — no tests, verified by build + downstream usage."
```

---

## Task 3: FrontmatterParser (TDD)

**Files:**
- Create: `src/GuardCode.Content/Loading/FrontmatterParseException.cs`
- Create: `src/GuardCode.Content/Loading/FrontmatterParser.cs`
- Create: `tests/GuardCode.Content.Tests/FrontmatterParserTests.cs`

`FrontmatterParser` takes a raw markdown file's content and returns `(TFrontmatter frontmatter, string body)` where `body` is the markdown with the `---`-delimited YAML block removed. It uses a strict `YamlDotNet` deserializer that rejects unknown fields and unknown types.

- [ ] **Step 1: Create `FrontmatterParseException.cs`**

Create `src/GuardCode.Content/Loading/FrontmatterParseException.cs`:

```csharp
using System;

namespace GuardCode.Content.Loading;

/// <summary>
/// Thrown when a markdown file cannot be parsed into a valid frontmatter + body pair.
/// Covers: missing frontmatter delimiters, malformed YAML, unknown frontmatter fields,
/// type mismatches. Callers surface the filename; this type only carries the "what".
/// </summary>
public sealed class FrontmatterParseException : Exception
{
    public FrontmatterParseException(string message) : base(message) { }
    public FrontmatterParseException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 2: Write the first failing test — valid principles frontmatter parses**

Create `tests/GuardCode.Content.Tests/FrontmatterParserTests.cs`:

```csharp
using GuardCode.Content;
using GuardCode.Content.Loading;

namespace GuardCode.Content.Tests;

public class FrontmatterParserTests
{
    private const string ValidPrinciples =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        title: Password Hashing
        summary: Storing, verifying, and handling user passwords in any backend.
        applies_to: [csharp, python, go]
        keywords:
          - password
          - bcrypt
        related_archetypes:
          - auth/session-tokens
        equivalents_in:
          c: crypto/key-derivation
        references:
          owasp_asvs: V2.4
          cwe: "916"
        ---

        # Body

        Principles body text.
        """;

    [Fact]
    public void Parse_ValidPrinciples_ReturnsFrontmatterAndBody()
    {
        var result = FrontmatterParser.Parse<PrinciplesFrontmatter>(ValidPrinciples);

        result.Frontmatter.SchemaVersion.Should().Be(1);
        result.Frontmatter.Archetype.Should().Be("auth/password-hashing");
        result.Frontmatter.Title.Should().Be("Password Hashing");
        result.Frontmatter.AppliesTo.Should().BeEquivalentTo(new[] { "csharp", "python", "go" });
        result.Frontmatter.Keywords.Should().BeEquivalentTo(new[] { "password", "bcrypt" });
        result.Frontmatter.RelatedArchetypes.Should().ContainSingle().Which.Should().Be("auth/session-tokens");
        result.Frontmatter.EquivalentsIn.Should().ContainKey("c").WhoseValue.Should().Be("crypto/key-derivation");
        result.Frontmatter.References.Should().ContainKey("owasp_asvs").WhoseValue.Should().Be("V2.4");
        result.Body.Should().StartWith("# Body");
        result.Body.Should().Contain("Principles body text.");
        result.Body.Should().NotContain("schema_version");
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/GuardCode.Content.Tests/GuardCode.Content.Tests.csproj --filter FullyQualifiedName~FrontmatterParserTests`
Expected: **COMPILE ERROR** — `FrontmatterParser` type does not exist. That counts as a failing test for our purposes.

- [ ] **Step 4: Implement `FrontmatterParser` minimally to pass the first test**

Create `src/GuardCode.Content/Loading/FrontmatterParser.cs`:

```csharp
using System;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GuardCode.Content.Loading;

/// <summary>
/// Splits a markdown file into its YAML frontmatter and body, then
/// strictly deserializes the frontmatter into a typed record.
/// </summary>
/// <remarks>
/// Strictness is load-bearing for security (design spec §6.3):
/// unknown properties are rejected, the naming convention is fixed,
/// and no dynamic type resolution is ever performed.
/// </remarks>
public static class FrontmatterParser
{
    private const string Delimiter = "---";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        // Do NOT call IgnoreUnmatchedProperties: we want unknown fields to throw.
        .Build();

    public readonly record struct ParseResult<T>(T Frontmatter, string Body);

    public static ParseResult<T> Parse<T>(string fileContent) where T : class, new()
    {
        if (fileContent is null)
        {
            throw new FrontmatterParseException("file content is null");
        }

        using var reader = new StringReader(fileContent);

        // First non-empty line must be the opening delimiter.
        var firstLine = ReadNextNonEmptyLine(reader);
        if (firstLine is null || firstLine.Trim() != Delimiter)
        {
            throw new FrontmatterParseException(
                "file does not begin with YAML frontmatter delimiter (---)");
        }

        // Accumulate until the closing delimiter.
        var yamlBuilder = new System.Text.StringBuilder();
        string? line;
        var foundClose = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim() == Delimiter)
            {
                foundClose = true;
                break;
            }
            yamlBuilder.AppendLine(line);
        }

        if (!foundClose)
        {
            throw new FrontmatterParseException(
                "YAML frontmatter is not closed — missing terminating --- delimiter");
        }

        T frontmatter;
        try
        {
            frontmatter = Deserializer.Deserialize<T>(yamlBuilder.ToString()) ?? new T();
        }
        catch (YamlException ex)
        {
            throw new FrontmatterParseException(
                $"YAML frontmatter is malformed or contains unknown fields: {ex.Message}", ex);
        }

        var body = reader.ReadToEnd().TrimStart('\r', '\n');
        return new ParseResult<T>(frontmatter, body);
    }

    private static string? ReadNextNonEmptyLine(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }
        return null;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/GuardCode.Content.Tests/GuardCode.Content.Tests.csproj --filter FullyQualifiedName~FrontmatterParserTests`
Expected: **1 passed**. If the test fails because `References["cwe"]` doesn't round-trip — that's why the test puts `"916"` in quotes: the `references` map is `Dictionary<string, string>` for simplicity, and YAML integer coercion would throw.

- [ ] **Step 6: Add the rejection tests**

Add to `FrontmatterParserTests.cs` (inside the class):

```csharp
    [Fact]
    public void Parse_MissingOpeningDelimiter_Throws()
    {
        const string content = "no frontmatter here\njust body.";
        var act = () => FrontmatterParser.Parse<PrinciplesFrontmatter>(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*does not begin*");
    }

    [Fact]
    public void Parse_UnclosedFrontmatter_Throws()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: x/y
            """;
        var act = () => FrontmatterParser.Parse<PrinciplesFrontmatter>(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*not closed*");
    }

    [Fact]
    public void Parse_UnknownField_Throws()
    {
        const string content =
            """
            ---
            schema_version: 1
            archetype: x/y
            title: T
            summary: s
            applies_to: [csharp]
            keywords: [k]
            unexpected_field: boom
            ---

            body
            """;
        var act = () => FrontmatterParser.Parse<PrinciplesFrontmatter>(content);
        act.Should().Throw<FrontmatterParseException>()
           .WithMessage("*malformed or contains unknown fields*");
    }

    [Fact]
    public void Parse_MalformedYaml_Throws()
    {
        const string content =
            """
            ---
            schema_version: [not, a, number
            ---

            body
            """;
        var act = () => FrontmatterParser.Parse<PrinciplesFrontmatter>(content);
        act.Should().Throw<FrontmatterParseException>();
    }
```

- [ ] **Step 7: Run all FrontmatterParser tests**

Run: `dotnet test tests/GuardCode.Content.Tests/GuardCode.Content.Tests.csproj --filter FullyQualifiedName~FrontmatterParserTests`
Expected: **5 passed** (the original plus four rejection cases). If `Parse_UnknownField_Throws` fails because YamlDotNet by default ignores unknown properties — it doesn't in current versions when `IgnoreUnmatchedProperties()` is not called, but if the test fails here, switch to a wrapper `DeserializerBuilder().WithDuplicateKeyChecking()` and use `Deserializer.Deserialize` with explicit strict mode. Verify by re-running.

- [ ] **Step 8: Commit**

```bash
git add src/GuardCode.Content/Loading/ tests/GuardCode.Content.Tests/FrontmatterParserTests.cs
git commit -m "content: add strict frontmatter parser with TDD

Parses YAML frontmatter from markdown files into typed records
with a strict YamlDotNet deserializer. Rejects missing/unclosed
delimiters, unknown fields, and malformed YAML — all covered by
dedicated tests. Strictness is a security boundary per spec §6.3:
the loader treats content files as untrusted input."
```

---

## Task 4: ArchetypeLoader (TDD)

**Files:**
- Create: `src/GuardCode.Content/Loading/ArchetypeLoader.cs`
- Create: `tests/GuardCode.Content.Tests/ArchetypeLoaderTests.cs`

`ArchetypeLoader` is a pure transformer: given a flat list of `(relativePath, fileContent)` tuples for one archetype directory, it produces one `Archetype` aggregate. It does **no** filesystem I/O — that's `FileSystemArchetypeRepository`'s job (Task 6). Splitting the two keeps the loader fully unit-testable with in-memory inputs.

- [ ] **Step 1: Write the first failing test — a directory with principles + one language file**

Create `tests/GuardCode.Content.Tests/ArchetypeLoaderTests.cs`:

```csharp
using System.Collections.Generic;
using GuardCode.Content.Loading;

namespace GuardCode.Content.Tests;

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
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ArchetypeLoaderTests`
Expected: **COMPILE ERROR** — `ArchetypeLoader` does not exist.

- [ ] **Step 3: Implement `ArchetypeLoader` minimally**

Create `src/GuardCode.Content/Loading/ArchetypeLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace GuardCode.Content.Loading;

/// <summary>
/// Pure transformer: flat (filename -> content) map for one archetype
/// directory becomes one <see cref="Archetype"/> aggregate. Performs
/// cross-file consistency checks (principles file must exist, archetype
/// IDs must match directory and frontmatter, language filenames must
/// match their frontmatter language).
/// </summary>
public static class ArchetypeLoader
{
    private const string PrinciplesFilename = "_principles.md";

    public static Archetype Load(
        string expectedArchetypeId,
        IReadOnlyDictionary<string, string> filesInDirectory)
    {
        if (!filesInDirectory.TryGetValue(PrinciplesFilename, out var principlesContent))
        {
            throw new ArchetypeLoadException(
                $"archetype '{expectedArchetypeId}' is missing required file '{PrinciplesFilename}'");
        }

        var principlesParse = FrontmatterParser.Parse<PrinciplesFrontmatter>(principlesContent);

        if (!string.Equals(principlesParse.Frontmatter.Archetype, expectedArchetypeId, StringComparison.Ordinal))
        {
            throw new ArchetypeLoadException(
                $"archetype '{expectedArchetypeId}': frontmatter archetype field is " +
                $"'{principlesParse.Frontmatter.Archetype}', expected '{expectedArchetypeId}'");
        }

        var languageFiles = new Dictionary<string, LanguageFile>(StringComparer.Ordinal);

        foreach (var (filename, content) in filesInDirectory)
        {
            if (filename == PrinciplesFilename) continue;
            if (!filename.EndsWith(".md", StringComparison.Ordinal))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': non-markdown file '{filename}' is not allowed");
            }

            var languageFromFilename = Path.GetFileNameWithoutExtension(filename);
            var languageParse = FrontmatterParser.Parse<LanguageFrontmatter>(content);

            if (!string.Equals(languageParse.Frontmatter.Language, languageFromFilename, StringComparison.Ordinal))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': file '{filename}' has frontmatter " +
                    $"language '{languageParse.Frontmatter.Language}', expected '{languageFromFilename}'");
            }

            if (!string.Equals(languageParse.Frontmatter.Archetype, expectedArchetypeId, StringComparison.Ordinal))
            {
                throw new ArchetypeLoadException(
                    $"archetype '{expectedArchetypeId}': file '{filename}' has frontmatter " +
                    $"archetype '{languageParse.Frontmatter.Archetype}', expected '{expectedArchetypeId}'");
            }

            languageFiles[languageFromFilename] = new LanguageFile(languageParse.Frontmatter, languageParse.Body);
        }

        return new Archetype(
            Id: expectedArchetypeId,
            Principles: principlesParse.Frontmatter,
            PrinciplesBody: principlesParse.Body,
            LanguageFiles: languageFiles);
    }
}

public sealed class ArchetypeLoadException : Exception
{
    public ArchetypeLoadException(string message) : base(message) { }
    public ArchetypeLoadException(string message, Exception inner) : base(message, inner) { }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ArchetypeLoaderTests`
Expected: **1 passed**.

- [ ] **Step 5: Add the consistency-check tests**

Add to `ArchetypeLoaderTests.cs`:

```csharp
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
```

- [ ] **Step 6: Run all ArchetypeLoader tests**

Run: `dotnet test --filter FullyQualifiedName~ArchetypeLoaderTests`
Expected: **4 passed**.

- [ ] **Step 7: Commit**

```bash
git add src/GuardCode.Content/Loading/ArchetypeLoader.cs tests/GuardCode.Content.Tests/ArchetypeLoaderTests.cs
git commit -m "content: add archetype loader with cross-file consistency checks

Groups parsed markdown files into Archetype aggregates and enforces
that (a) every archetype has _principles.md, (b) frontmatter archetype
IDs match the directory, and (c) language filenames match their
frontmatter language field. Pure transformer — no filesystem I/O —
so it can be unit-tested with in-memory inputs."
```

---

## Task 5: ArchetypeValidator (TDD)

**Files:**
- Create: `src/GuardCode.Content/Validation/ArchetypeValidationException.cs`
- Create: `src/GuardCode.Content/Validation/ArchetypeValidator.cs`
- Create: `tests/GuardCode.Content.Tests/ArchetypeValidatorTests.cs`

Validates an already-loaded `Archetype` against design spec §4.1/§4.2 structural rules:

1. Principles body contains required sections: `When this applies`, `Architectural placement`, `Principles`, `Anti-patterns`, `References`.
2. Language file body contains required sections: `Library choice`, `Reference implementation`, `Language-specific gotchas`, `Tests to write`.
3. Each language file stays within the 200-line budget (including frontmatter — which we count by adding the frontmatter size back in with an estimated YAML serialization, since the `Body` string alone has already had frontmatter stripped).
4. The reference implementation code block in each language file is ≤ 40 lines of code.

For (3) we keep it simple: the validator takes the original *file content* as well as the parsed `Archetype`, so we can count raw lines directly. We'll thread that through by having the loader hand back an additional `IReadOnlyDictionary<string,int>` of raw line counts.

- [ ] **Step 1: Extend `ArchetypeLoader` to emit raw line counts**

Edit `src/GuardCode.Content/Loading/ArchetypeLoader.cs` — add a static helper and a new overload that also returns raw line counts per filename. Keep the existing `Load` method signature and add a sibling method `LoadWithLineCounts` used by the validator. Replace the bottom of the file (the `Load` method body is unchanged; add below it):

```csharp
    /// <summary>
    /// Same as <see cref="Load"/>, but also returns the raw (pre-parse)
    /// line count of every file keyed by filename. Used by the validator
    /// to enforce the per-file 200-line budget from spec §4.2.
    /// </summary>
    public static (Archetype Archetype, IReadOnlyDictionary<string, int> RawLineCounts) LoadWithLineCounts(
        string expectedArchetypeId,
        IReadOnlyDictionary<string, string> filesInDirectory)
    {
        var archetype = Load(expectedArchetypeId, filesInDirectory);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (filename, content) in filesInDirectory)
        {
            counts[filename] = CountLines(content);
        }
        return (archetype, counts);
    }

    private static int CountLines(string content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        var count = 1;
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n') count++;
        }
        if (content[^1] == '\n') count--;
        return count;
    }
```

Build: `dotnet build`. Expected: success.

- [ ] **Step 2: Create `ArchetypeValidationException.cs`**

Create `src/GuardCode.Content/Validation/ArchetypeValidationException.cs`:

```csharp
using System;

namespace GuardCode.Content.Validation;

/// <summary>
/// Thrown when an archetype violates a structural rule from design
/// spec §4: missing body sections, exceeding line or code budgets,
/// or required-field violations not caught at parse time.
/// </summary>
public sealed class ArchetypeValidationException : Exception
{
    public ArchetypeValidationException(string message) : base(message) { }
}
```

- [ ] **Step 3: Write the first failing test — principles missing a required section**

Create `tests/GuardCode.Content.Tests/ArchetypeValidatorTests.cs`:

```csharp
using System.Collections.Generic;
using GuardCode.Content;
using GuardCode.Content.Validation;

namespace GuardCode.Content.Tests;

public class ArchetypeValidatorTests
{
    private static Archetype BuildArchetype(
        string principlesBody,
        (string lang, string body)[] languageFiles)
    {
        var langMap = new Dictionary<string, LanguageFile>();
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
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ArchetypeValidatorTests`
Expected: COMPILE ERROR — `ArchetypeValidator` missing.

- [ ] **Step 5: Implement `ArchetypeValidator`**

Create `src/GuardCode.Content/Validation/ArchetypeValidator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GuardCode.Content.Validation;

/// <summary>
/// Validates the structural invariants on an <see cref="Archetype"/>
/// that cannot be expressed at the frontmatter schema level: required
/// body sections, per-file line budget, and reference-implementation
/// code-size budget.
/// </summary>
public static class ArchetypeValidator
{
    private static readonly string[] RequiredPrinciplesSections =
    {
        "When this applies",
        "Architectural placement",
        "Principles",
        "Anti-patterns",
        "References"
    };

    private static readonly string[] RequiredLanguageSections =
    {
        "Library choice",
        "Reference implementation",
        "Language-specific gotchas",
        "Tests to write"
    };

    public const int MaxFileLines = 200;
    public const int MaxReferenceImplementationCodeLines = 40;

    public static void Validate(
        Archetype archetype,
        IReadOnlyDictionary<string, int> rawLineCounts)
    {
        ValidateRequiredSections(
            archetype.Id,
            "_principles.md",
            archetype.PrinciplesBody,
            RequiredPrinciplesSections);

        if (rawLineCounts.TryGetValue("_principles.md", out var principlesLines)
            && principlesLines > MaxFileLines)
        {
            throw new ArchetypeValidationException(
                $"archetype '{archetype.Id}': file '_principles.md' is {principlesLines} lines, " +
                $"exceeds the {MaxFileLines}-line budget");
        }

        foreach (var (language, languageFile) in archetype.LanguageFiles)
        {
            var filename = $"{language}.md";
            ValidateRequiredSections(archetype.Id, filename, languageFile.Body, RequiredLanguageSections);

            if (rawLineCounts.TryGetValue(filename, out var lines) && lines > MaxFileLines)
            {
                throw new ArchetypeValidationException(
                    $"archetype '{archetype.Id}': file '{filename}' is {lines} lines, " +
                    $"exceeds the {MaxFileLines}-line budget");
            }

            ValidateReferenceImplementationBudget(archetype.Id, filename, language, languageFile.Body);
        }
    }

    private static void ValidateRequiredSections(
        string archetypeId,
        string filename,
        string body,
        IReadOnlyList<string> requiredSections)
    {
        foreach (var section in requiredSections)
        {
            var pattern = new Regex(
                $@"^#{{1,6}}\s+{Regex.Escape(section)}\s*$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            if (!pattern.IsMatch(body))
            {
                throw new ArchetypeValidationException(
                    $"archetype '{archetypeId}': file '{filename}' is missing required section '{section}'");
            }
        }
    }

    private static void ValidateReferenceImplementationBudget(
        string archetypeId,
        string filename,
        string language,
        string body)
    {
        // Find the first fenced code block inside the "Reference implementation" section.
        var sectionStart = Regex.Match(
            body,
            @"^#{1,6}\s+Reference implementation\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (!sectionStart.Success) return;

        var afterHeading = body[(sectionStart.Index + sectionStart.Length)..];

        // Stop at the next section heading so we only scan this section.
        var nextSection = Regex.Match(
            afterHeading,
            @"^#{1,6}\s+\S",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var sectionBody = nextSection.Success
            ? afterHeading[..nextSection.Index]
            : afterHeading;

        var codeBlockStartMarker = "```";
        var openIndex = sectionBody.IndexOf(codeBlockStartMarker, StringComparison.Ordinal);
        if (openIndex < 0) return; // No code block is fine — budget doesn't apply.

        var afterOpen = sectionBody[(openIndex + codeBlockStartMarker.Length)..];
        var firstNewline = afterOpen.IndexOf('\n');
        if (firstNewline < 0) return;
        var codeStart = firstNewline + 1;

        var closeIndex = afterOpen.IndexOf("```", codeStart, StringComparison.Ordinal);
        if (closeIndex < 0)
        {
            throw new ArchetypeValidationException(
                $"archetype '{archetypeId}': file '{filename}' has an unterminated code block " +
                "in the Reference implementation section");
        }

        var code = afterOpen[codeStart..closeIndex];
        var codeLines = code.Split('\n').Count(line => !string.IsNullOrWhiteSpace(line));
        if (codeLines > MaxReferenceImplementationCodeLines)
        {
            throw new ArchetypeValidationException(
                $"archetype '{archetypeId}': file '{filename}' reference implementation is " +
                $"{codeLines} non-empty lines, exceeds the {MaxReferenceImplementationCodeLines}-line budget " +
                $"(language: {language})");
        }
    }
}
```

- [ ] **Step 6: Run the first test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ArchetypeValidatorTests`
Expected: **1 passed**.

- [ ] **Step 7: Add the remaining validator tests**

Add inside `ArchetypeValidatorTests`:

```csharp
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
```

Also add `using System.Linq;` at the top of the test file if it's not already there.

- [ ] **Step 8: Run all validator tests**

Run: `dotnet test --filter FullyQualifiedName~ArchetypeValidatorTests`
Expected: **5 passed**.

- [ ] **Step 9: Commit**

```bash
git add src/GuardCode.Content/Validation/ src/GuardCode.Content/Loading/ArchetypeLoader.cs tests/GuardCode.Content.Tests/ArchetypeValidatorTests.cs
git commit -m "content: add structural archetype validator

Validates required body sections in principles and language files,
the 200-line per-file budget, and the 40-line reference-implementation
code budget from spec §4. Also extends ArchetypeLoader with a sibling
LoadWithLineCounts method so the validator can check raw file sizes
without re-reading the disk."
```

---

## Task 6: FileSystemArchetypeRepository (TDD)

**Files:**
- Create: `src/GuardCode.Content/Loading/IArchetypeRepository.cs`
- Create: `src/GuardCode.Content/Loading/FileSystemArchetypeRepository.cs`
- Create: `tests/GuardCode.Content.Tests/FileSystemArchetypeRepositoryTests.cs`

The repository is the only component in the loading pipeline that touches the filesystem. It walks the archetypes root, groups files by directory, derives each archetype ID from the directory path relative to the root, and delegates the rest to `ArchetypeLoader` + `ArchetypeValidator`. It also implements the path-traversal defense from design spec §6.1 — every file path is verified to be strictly under the root using `Path.GetFullPath` + `StartsWith`.

- [ ] **Step 1: Create the interface**

Create `src/GuardCode.Content/Loading/IArchetypeRepository.cs`:

```csharp
using System.Collections.Generic;

namespace GuardCode.Content.Loading;

/// <summary>
/// Loads every archetype from the content store. Synchronous by design:
/// this is called once from the composition root before the MCP event
/// loop starts. Design spec §5.3 explains why async would be a liability.
/// </summary>
public interface IArchetypeRepository
{
    IReadOnlyList<Archetype> LoadAll();
}
```

- [ ] **Step 2: Write the first failing test — two archetypes loaded from a temp directory**

Create `tests/GuardCode.Content.Tests/FileSystemArchetypeRepositoryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using GuardCode.Content.Loading;

namespace GuardCode.Content.Tests;

public class FileSystemArchetypeRepositoryTests : IDisposable
{
    private readonly string _rootDir;

    public FileSystemArchetypeRepositoryTests()
    {
        _rootDir = Path.Combine(Path.GetTempPath(), "guardcode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, recursive: true);
    }

    private void WriteFile(string relativePath, string content)
    {
        var full = Path.Combine(_rootDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private const string ValidPrinciples =
        """
        ---
        schema_version: 1
        archetype: auth/password-hashing
        title: Password Hashing
        summary: Summary.
        applies_to: [csharp]
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
}
```

- [ ] **Step 3: Run to verify compile error**

Run: `dotnet test --filter FullyQualifiedName~FileSystemArchetypeRepositoryTests`
Expected: COMPILE ERROR — `FileSystemArchetypeRepository` missing.

- [ ] **Step 4: Implement `FileSystemArchetypeRepository`**

Create `src/GuardCode.Content/Loading/FileSystemArchetypeRepository.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GuardCode.Content.Validation;

namespace GuardCode.Content.Loading;

/// <summary>
/// Walks the archetypes root directory, loads every archetype, runs
/// validation, and returns the result. Implements the path-traversal
/// defense from design spec §6.1 by verifying every resolved file path
/// sits strictly under the root.
/// </summary>
public sealed class FileSystemArchetypeRepository : IArchetypeRepository
{
    private readonly string _rootFullPath;

    public FileSystemArchetypeRepository(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("root path must be non-empty", nameof(rootPath));
        }
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException(
                $"archetypes root does not exist: {rootPath}");
        }
        _rootFullPath = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
    }

    public IReadOnlyList<Archetype> LoadAll()
    {
        // Group every .md file by its containing directory. Each directory
        // with at least one markdown file is candidate for an archetype.
        var filesByDirectory = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(_rootFullPath, "*.md", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(_rootFullPath, StringComparison.Ordinal))
            {
                // Defensive: enumeration should never hand us an outside path,
                // but we never want to trust that assumption silently.
                throw new ArchetypeLoadException(
                    $"refusing to load file outside archetypes root: {fullPath}");
            }

            var directory = Path.GetDirectoryName(fullPath)!;
            if (!filesByDirectory.TryGetValue(directory, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.Ordinal);
                filesByDirectory[directory] = map;
            }
            map[Path.GetFileName(fullPath)] = File.ReadAllText(fullPath);
        }

        // Only directories that contain _principles.md qualify as archetypes.
        var archetypes = new List<Archetype>();
        foreach (var (directory, files) in filesByDirectory)
        {
            if (!files.ContainsKey("_principles.md")) continue;

            var archetypeId = DeriveArchetypeId(directory);
            var (archetype, rawLineCounts) = ArchetypeLoader.LoadWithLineCounts(archetypeId, files);
            ArchetypeValidator.Validate(archetype, rawLineCounts);
            archetypes.Add(archetype);
        }

        // Stable order for deterministic behavior downstream (index, tests).
        return archetypes.OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
    }

    private string DeriveArchetypeId(string fullDirectory)
    {
        var relative = Path.GetRelativePath(_rootFullPath, fullDirectory);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.Length == 0) return path;
        var last = path[^1];
        if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar) return path;
        return path + Path.DirectorySeparatorChar;
    }
}
```

- [ ] **Step 5: Run the first test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~FileSystemArchetypeRepositoryTests`
Expected: **1 passed**. If `Directory.EnumerateFiles` on Windows returns `\` separators and you see an archetype ID like `auth\password-hashing`, the `Replace` call fixes that.

- [ ] **Step 6: Add the directory-not-found test**

Add to `FileSystemArchetypeRepositoryTests`:

```csharp
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
```

- [ ] **Step 7: Run all repository tests**

Run: `dotnet test --filter FullyQualifiedName~FileSystemArchetypeRepositoryTests`
Expected: **3 passed**.

- [ ] **Step 8: Commit**

```bash
git add src/GuardCode.Content/Loading/IArchetypeRepository.cs src/GuardCode.Content/Loading/FileSystemArchetypeRepository.cs tests/GuardCode.Content.Tests/FileSystemArchetypeRepositoryTests.cs
git commit -m "content: add filesystem archetype repository

Walks the archetypes/ root, groups markdown files by directory,
delegates parsing/validation to ArchetypeLoader + ArchetypeValidator,
and returns a deterministic ordered list. Implements the path-
traversal defense from spec §6.1: every resolved path is verified
to sit under the root, and the root is normalized with a trailing
separator to prevent prefix confusion."
```

---

## Task 7: KeywordArchetypeIndex (TDD)

**Files:**
- Create: `src/GuardCode.Content/Indexing/PrepMatch.cs`
- Create: `src/GuardCode.Content/Indexing/IArchetypeIndex.cs`
- Create: `src/GuardCode.Content/Indexing/KeywordArchetypeIndex.cs`
- Create: `tests/GuardCode.Content.Tests/KeywordArchetypeIndexTests.cs`

The index builds three data structures once from a loaded corpus and answers all runtime lookups from memory:

1. **Inverted index:** `Dictionary<string keyword, HashSet<string archetypeId>>`.
2. **By-ID lookup:** `Dictionary<string archetypeId, Archetype>` so `Get` is O(1).
3. **Reverse related index:** for every archetype A that lists B in its `related_archetypes`, add A to the reverse list of B. Design spec §3.2 says `related_archetypes` in the `consult` response must be bidirectional from the LLM's point of view.

Scoring for `Search` is simple and deterministic: 1.0 per keyword match, +0.5 per substring match in title or summary, then normalize by the max possible (total tokens in the query) so scores land in [0,1].

- [ ] **Step 1: Create `PrepMatch`**

Create `src/GuardCode.Content/Indexing/PrepMatch.cs`:

```csharp
namespace GuardCode.Content.Indexing;

/// <summary>
/// One scored search hit returned by <see cref="IArchetypeIndex.Search"/>.
/// </summary>
public sealed record PrepMatch(
    string ArchetypeId,
    string Title,
    string Summary,
    double Score);
```

- [ ] **Step 2: Create `IArchetypeIndex`**

Create `src/GuardCode.Content/Indexing/IArchetypeIndex.cs`:

```csharp
using System.Collections.Generic;

namespace GuardCode.Content.Indexing;

/// <summary>
/// In-memory index over the loaded archetype corpus. One instance lives
/// for the lifetime of the process; all lookups are pure reads.
/// </summary>
public interface IArchetypeIndex
{
    IReadOnlyList<PrepMatch> Search(string intent, SupportedLanguage language, int maxResults);
    Archetype? Get(string archetypeId);
    IReadOnlyList<string> GetReverseRelated(string archetypeId);
}
```

- [ ] **Step 3: Write the first failing test — inverted index returns matches by keyword**

Create `tests/GuardCode.Content.Tests/KeywordArchetypeIndexTests.cs`:

```csharp
using System.Collections.Generic;
using GuardCode.Content;
using GuardCode.Content.Indexing;

namespace GuardCode.Content.Tests;

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
                RelatedArchetypes = new List<string>(relatedArchetypes ?? System.Array.Empty<string>())
            },
            PrinciplesBody: "body",
            LanguageFiles: new Dictionary<string, LanguageFile>());

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
}
```

- [ ] **Step 4: Run to verify compile error**

Run: `dotnet test --filter FullyQualifiedName~KeywordArchetypeIndexTests`
Expected: COMPILE ERROR — `KeywordArchetypeIndex` missing.

- [ ] **Step 5: Implement `KeywordArchetypeIndex`**

Create `src/GuardCode.Content/Indexing/KeywordArchetypeIndex.cs`:

```csharp
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace GuardCode.Content.Indexing;

/// <summary>
/// Deterministic keyword-based index over the archetype corpus.
/// Built once at startup; lookups are pure reads from precomputed
/// frozen dictionaries. No embeddings, no fuzzy matching — design
/// spec §10 explicitly defers those.
/// </summary>
public sealed class KeywordArchetypeIndex : IArchetypeIndex
{
    // Tiny static stopword list — enough to drop obvious noise words
    // without requiring an external dependency or per-locale tables.
    private static readonly FrozenSet<string> Stopwords = new[]
    {
        "a", "an", "the", "and", "or", "but", "if", "then", "else",
        "of", "in", "on", "at", "to", "for", "with", "from", "by",
        "is", "are", "was", "were", "be", "been", "being",
        "i", "im", "my", "we", "you", "your",
        "how", "do", "does", "about", "want", "need",
        "this", "that", "these", "those",
        "it", "its", "as", "so"
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly FrozenDictionary<string, Archetype> _byId;
    private readonly FrozenDictionary<string, FrozenSet<string>> _keywordIndex;
    private readonly FrozenDictionary<string, ImmutableArray<string>> _reverseRelated;

    private KeywordArchetypeIndex(
        FrozenDictionary<string, Archetype> byId,
        FrozenDictionary<string, FrozenSet<string>> keywordIndex,
        FrozenDictionary<string, ImmutableArray<string>> reverseRelated)
    {
        _byId = byId;
        _keywordIndex = keywordIndex;
        _reverseRelated = reverseRelated;
    }

    public static KeywordArchetypeIndex Build(IReadOnlyList<Archetype> archetypes)
    {
        var byIdBuilder = new Dictionary<string, Archetype>(StringComparer.Ordinal);
        var keywordBuilder = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var reverseBuilder = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var archetype in archetypes)
        {
            byIdBuilder[archetype.Id] = archetype;

            foreach (var keyword in archetype.Principles.Keywords)
            {
                var normalized = keyword.Trim().ToLowerInvariant();
                if (normalized.Length == 0) continue;
                if (!keywordBuilder.TryGetValue(normalized, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    keywordBuilder[normalized] = set;
                }
                set.Add(archetype.Id);
            }

            foreach (var related in archetype.Principles.RelatedArchetypes)
            {
                if (string.IsNullOrWhiteSpace(related)) continue;
                if (!reverseBuilder.TryGetValue(related, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    reverseBuilder[related] = set;
                }
                set.Add(archetype.Id);
            }
        }

        // Freeze everything. The index is immutable after this point —
        // every lookup on the request path goes through FrozenDictionary.
        var byId = byIdBuilder.ToFrozenDictionary(StringComparer.Ordinal);
        var keywordIndex = keywordBuilder.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToFrozenSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var reverseRelated = reverseBuilder.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.OrderBy(s => s, StringComparer.Ordinal).ToImmutableArray(),
            StringComparer.Ordinal);

        return new KeywordArchetypeIndex(byId, keywordIndex, reverseRelated);
    }

    public IReadOnlyList<PrepMatch> Search(string intent, SupportedLanguage language, int maxResults)
    {
        if (maxResults <= 0) return Array.Empty<PrepMatch>();
        var tokens = Tokenize(intent);
        if (tokens.Count == 0) return Array.Empty<PrepMatch>();

        var wireLanguage = language.ToWireString();
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            if (_keywordIndex.TryGetValue(token, out var hits))
            {
                foreach (var archetypeId in hits)
                {
                    scores.TryGetValue(archetypeId, out var current);
                    scores[archetypeId] = current + 1.0;
                }
            }
        }

        // Title/summary substring bonus.
        foreach (var archetype in _byId.Values)
        {
            var haystack = (archetype.Principles.Title + " " + archetype.Principles.Summary).ToLowerInvariant();
            var bonus = 0.0;
            foreach (var token in tokens)
            {
                if (haystack.Contains(token, StringComparison.Ordinal)) bonus += 0.5;
            }
            if (bonus > 0)
            {
                scores.TryGetValue(archetype.Id, out var current);
                scores[archetype.Id] = current + bonus;
            }
        }

        var maxPossible = tokens.Count * 1.5;
        var filtered = scores
            .Where(kvp => _byId[kvp.Key].Principles.AppliesTo.Contains(wireLanguage))
            .Where(kvp => kvp.Value > 0)
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(kvp =>
            {
                var a = _byId[kvp.Key];
                var normalized = maxPossible > 0 ? Math.Min(1.0, kvp.Value / maxPossible) : 0.0;
                return new PrepMatch(a.Id, a.Principles.Title, a.Principles.Summary, normalized);
            })
            .ToList();

        return filtered;
    }

    public Archetype? Get(string archetypeId)
        => _byId.TryGetValue(archetypeId, out var a) ? a : null;

    public IReadOnlyList<string> GetReverseRelated(string archetypeId)
        => _reverseRelated.TryGetValue(archetypeId, out var list) ? list : Array.Empty<string>();

    private static List<string> Tokenize(string intent)
    {
        if (string.IsNullOrWhiteSpace(intent)) return new List<string>();
        var result = new List<string>();
        var lower = intent.ToLowerInvariant();
        var current = new System.Text.StringBuilder();

        void Flush()
        {
            if (current.Length == 0) return;
            var token = current.ToString();
            current.Clear();
            if (token.Length < 2) return;
            if (Stopwords.Contains(token)) return;
            result.Add(token);
        }

        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch)) current.Append(ch);
            else Flush();
        }
        Flush();
        return result;
    }
}
```

- [ ] **Step 6: Run the first test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~KeywordArchetypeIndexTests`
Expected: **1 passed**.

- [ ] **Step 7: Add the language-filter and reverse-related tests**

Add inside `KeywordArchetypeIndexTests`:

```csharp
    [Fact]
    public void Search_LanguageNotInAppliesTo_FiltersOut()
    {
        // Archetype applies only to C; query language is Python.
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
    public void Get_UnknownArchetype_ReturnsNull()
    {
        var index = KeywordArchetypeIndex.Build(System.Array.Empty<Archetype>());
        index.Get("nope/nope").Should().BeNull();
    }
```

- [ ] **Step 8: Run all index tests**

Run: `dotnet test --filter FullyQualifiedName~KeywordArchetypeIndexTests`
Expected: **5 passed**.

- [ ] **Step 9: Commit**

```bash
git add src/GuardCode.Content/Indexing/ tests/GuardCode.Content.Tests/KeywordArchetypeIndexTests.cs
git commit -m "content: add keyword-based archetype index

Builds an inverted keyword index, a by-ID lookup, and a reverse
related_archetypes index from the loaded corpus. Scoring is
deterministic: 1.0 per keyword match plus a 0.5 bonus per token
that appears in title or summary, normalized to [0,1]. Filters
results by applies_to before returning. No embeddings, no fuzzy
matching — deferred per spec §10."
```

---

## Task 8: PrepService (TDD)

**Files:**
- Create: `src/GuardCode.Content/Services/PrepResult.cs`
- Create: `src/GuardCode.Content/Services/IPrepService.cs`
- Create: `src/GuardCode.Content/Services/PrepService.cs`
- Create: `tests/GuardCode.Content.Tests/PrepServiceTests.cs`

`PrepService` is a thin shim over `IArchetypeIndex.Search`. Its job is to:

1. Validate inputs (`intent` max 2000 chars, non-empty; `language` already typed; `framework` accepted but not used for filtering in MVP — design spec §3.1).
2. Call `index.Search` with `maxResults = 8`.
3. Return a typed `PrepResult` that the MCP tool layer serializes.

Keeping this as a separate service (not inlining the tool handler call to the index) matters for two reasons: the input-validation boundary is explicit, and the tool handler stays trivially thin, which makes it auditable and keeps business logic out of the MCP surface.

- [ ] **Step 1: Create `PrepResult`**

Create `src/GuardCode.Content/Services/PrepResult.cs`:

```csharp
using System.Collections.Generic;
using GuardCode.Content.Indexing;

namespace GuardCode.Content.Services;

/// <summary>
/// Typed response from <see cref="IPrepService.Prep"/>.
/// Serialized as the MCP tool response body in <c>PrepTool</c>.
/// </summary>
public sealed record PrepResult(IReadOnlyList<PrepMatch> Matches);
```

- [ ] **Step 2: Create `IPrepService`**

Create `src/GuardCode.Content/Services/IPrepService.cs`:

```csharp
namespace GuardCode.Content.Services;

public interface IPrepService
{
    PrepResult Prep(string intent, SupportedLanguage language, string? framework);
}
```

- [ ] **Step 3: Write the first failing test — valid intent returns matches**

Create `tests/GuardCode.Content.Tests/PrepServiceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using GuardCode.Content;
using GuardCode.Content.Indexing;
using GuardCode.Content.Services;

namespace GuardCode.Content.Tests;

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
                AppliesTo = new List<string>(appliesTo),
                Keywords = new List<string>(keywords)
            },
            PrinciplesBody: "body",
            LanguageFiles: new Dictionary<string, LanguageFile>());

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
}
```

- [ ] **Step 4: Run to verify compile error**

Run: `dotnet test --filter FullyQualifiedName~PrepServiceTests`
Expected: COMPILE ERROR — `PrepService` missing.

- [ ] **Step 5: Implement `PrepService`**

Create `src/GuardCode.Content/Services/PrepService.cs`:

```csharp
using System;
using GuardCode.Content.Indexing;

namespace GuardCode.Content.Services;

/// <summary>
/// Answers <c>prep</c> queries. Thin wrapper over
/// <see cref="IArchetypeIndex"/> that owns input validation and
/// caps result count per spec §3.1 (max 8 matches).
/// </summary>
public sealed class PrepService(IArchetypeIndex index) : IPrepService
{
    public const int MaxIntentLength = 2000;
    public const int MaxResults = 8;

    public PrepResult Prep(string intent, SupportedLanguage language, string? framework)
    {
        if (intent is null) throw new ArgumentNullException(nameof(intent));
        if (intent.Length == 0)
        {
            throw new ArgumentException("intent must be non-empty", nameof(intent));
        }
        if (intent.Length > MaxIntentLength)
        {
            throw new ArgumentException(
                $"intent must be {MaxIntentLength} characters or fewer (got {intent.Length})",
                nameof(intent));
        }

        // framework is accepted and validated shape-wise by the MCP tool
        // layer, but is not used for filtering in MVP per spec §3.1.
        _ = framework;

        var matches = index.Search(intent, language, MaxResults);
        return new PrepResult(matches);
    }
}
```

- [ ] **Step 6: Run the first test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~PrepServiceTests`
Expected: **1 passed**.

- [ ] **Step 7: Add input-validation and language-filter tests**

Add inside `PrepServiceTests`:

```csharp
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
```

- [ ] **Step 8: Run all PrepService tests**

Run: `dotnet test --filter FullyQualifiedName~PrepServiceTests`
Expected: **4 passed**.

- [ ] **Step 9: Commit**

```bash
git add src/GuardCode.Content/Services/PrepResult.cs src/GuardCode.Content/Services/IPrepService.cs src/GuardCode.Content/Services/PrepService.cs tests/GuardCode.Content.Tests/PrepServiceTests.cs
git commit -m "content: add PrepService

Thin input-validated wrapper over the archetype index that
implements the prep() contract from spec §3.1: max 2000 chars
of intent, caps results at 8, applies_to language filter. The
framework field is accepted and ignored for MVP per spec, which
keeps the surface forward-compatible without adding logic for
content we do not yet ship."
```

---

## Task 9: ConsultationService (TDD)

**Files:**
- Create: `src/GuardCode.Content/Services/ConsultResult.cs`
- Create: `src/GuardCode.Content/Services/IConsultationService.cs`
- Create: `src/GuardCode.Content/Services/ConsultationService.cs`
- Create: `tests/GuardCode.Content.Tests/ConsultationServiceTests.cs`

`ConsultationService` implements the `consult` tool contract from design spec §3.2. Three shapes of response share one DTO:

1. **Normal hit:** `content` = principles body + `\n\n---\n\n` + language body, plus `related_archetypes` (own + reverse index) and `references`.
2. **Redirect:** requested language is not in `applies_to`. If `equivalents_in` names an archetype for that language, suggest it; otherwise, return a redirect with a generic message and no suggestions.
3. **Not found:** archetype ID doesn't exist in the index.

We encode this as `ConsultResult` with discriminator flags rather than a sum type — design spec shows the wire-format JSON uses `redirect: true` as an explicit field, so our DTO mirrors that.

- [ ] **Step 1: Create `ConsultResult`**

Create `src/GuardCode.Content/Services/ConsultResult.cs`:

```csharp
using System.Collections.Generic;

namespace GuardCode.Content.Services;

/// <summary>
/// Typed response from <see cref="IConsultationService.Consult"/>.
/// Three shapes share one DTO, keyed by the nullable fields, so the
/// MCP tool layer can serialize whichever wire shape applies. See
/// design spec §3.2 for the three shapes (normal, redirect, not-found).
/// </summary>
public sealed record ConsultResult(
    string Archetype,
    string Language,
    string? Content,
    IReadOnlyList<string> RelatedArchetypes,
    IReadOnlyDictionary<string, string> References,
    bool Redirect,
    string? Message,
    IReadOnlyList<string> Suggested,
    bool NotFound);
```

- [ ] **Step 2: Create `IConsultationService`**

Create `src/GuardCode.Content/Services/IConsultationService.cs`:

```csharp
namespace GuardCode.Content.Services;

public interface IConsultationService
{
    ConsultResult Consult(string archetypeId, SupportedLanguage language);
}
```

- [ ] **Step 3: Write the first failing test — normal composition**

Create `tests/GuardCode.Content.Tests/ConsultationServiceTests.cs`:

```csharp
using System.Collections.Generic;
using GuardCode.Content;
using GuardCode.Content.Indexing;
using GuardCode.Content.Services;

namespace GuardCode.Content.Tests;

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
        var langMap = new Dictionary<string, LanguageFile>();
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
                RelatedArchetypes = new List<string>(relatedArchetypes ?? System.Array.Empty<string>()),
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
}
```

- [ ] **Step 4: Run to verify compile error**

Run: `dotnet test --filter FullyQualifiedName~ConsultationServiceTests`
Expected: COMPILE ERROR — `ConsultationService` missing.

- [ ] **Step 5: Implement `ConsultationService`**

Create `src/GuardCode.Content/Services/ConsultationService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GuardCode.Content.Indexing;

namespace GuardCode.Content.Services;

/// <summary>
/// Answers <c>consult</c> queries. Implements the three response shapes
/// from design spec §3.2: normal composition, unsupported-language redirect,
/// and archetype-not-found. Redirect logic lives here only — <see cref="PrepService"/>
/// returns empty results for unsupported languages so there is exactly one
/// code path per concept.
/// </summary>
public sealed class ConsultationService(IArchetypeIndex index) : IConsultationService
{
    private static readonly Regex ArchetypeIdRegex = new(
        @"^[a-z0-9\-]+(/[a-z0-9\-]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public const string BodySeparator = "\n\n---\n\n";

    public ConsultResult Consult(string archetypeId, SupportedLanguage language)
    {
        if (archetypeId is null) throw new ArgumentNullException(nameof(archetypeId));
        if (!ArchetypeIdRegex.IsMatch(archetypeId))
        {
            throw new ArgumentException(
                $"archetype id '{archetypeId}' is not a valid identifier",
                nameof(archetypeId));
        }

        var wireLanguage = language.ToWireString();
        var archetype = index.Get(archetypeId);
        if (archetype is null)
        {
            return new ConsultResult(
                Archetype: archetypeId,
                Language: wireLanguage,
                Content: null,
                RelatedArchetypes: Array.Empty<string>(),
                References: new Dictionary<string, string>(),
                Redirect: false,
                Message: $"Archetype '{archetypeId}' was not found.",
                Suggested: Array.Empty<string>(),
                NotFound: true);
        }

        if (!archetype.Principles.AppliesTo.Contains(wireLanguage, StringComparer.Ordinal))
        {
            var suggested = new List<string>();
            string? message;
            if (archetype.Principles.EquivalentsIn.TryGetValue(wireLanguage, out var equivalent))
            {
                suggested.Add(equivalent);
                message =
                    $"Archetype '{archetypeId}' does not apply to {wireLanguage}. " +
                    $"See '{equivalent}' for the equivalent guidance in {wireLanguage}.";
            }
            else
            {
                message =
                    $"Archetype '{archetypeId}' does not apply to {wireLanguage}. " +
                    "No direct equivalent is registered; consider searching with prep() for " +
                    "a related archetype in this language.";
            }

            return new ConsultResult(
                Archetype: archetypeId,
                Language: wireLanguage,
                Content: null,
                RelatedArchetypes: Array.Empty<string>(),
                References: new Dictionary<string, string>(),
                Redirect: true,
                Message: message,
                Suggested: suggested,
                NotFound: false);
        }

        if (!archetype.LanguageFiles.TryGetValue(wireLanguage, out var languageFile))
        {
            // applies_to claims the language but the file is missing. This is
            // a content-authoring error we caught too late to reject at startup
            // because applies_to and file presence can be inconsistent if a
            // contributor edits only one side. Treat as not-found at runtime
            // with a distinguishing message; validation tightening is a future
            // concern (see plan follow-ups).
            return new ConsultResult(
                Archetype: archetypeId,
                Language: wireLanguage,
                Content: null,
                RelatedArchetypes: Array.Empty<string>(),
                References: new Dictionary<string, string>(),
                Redirect: false,
                Message:
                    $"Archetype '{archetypeId}' lists {wireLanguage} in applies_to " +
                    "but no language file exists on disk.",
                Suggested: Array.Empty<string>(),
                NotFound: true);
        }

        var body = archetype.PrinciplesBody + BodySeparator + languageFile.Body;

        var related = archetype.Principles.RelatedArchetypes
            .Concat(index.GetReverseRelated(archetypeId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return new ConsultResult(
            Archetype: archetypeId,
            Language: wireLanguage,
            Content: body,
            RelatedArchetypes: related,
            References: new Dictionary<string, string>(archetype.Principles.References),
            Redirect: false,
            Message: null,
            Suggested: Array.Empty<string>(),
            NotFound: false);
    }
}
```

Note the `applies_to`-without-file branch: the existing `ArchetypeLoader` does not cross-check `applies_to` against the set of language files present. Tightening that is a reasonable follow-up but not required for MVP — the runtime branch keeps the server honest.

- [ ] **Step 6: Run the first test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ConsultationServiceTests`
Expected: **1 passed**.

- [ ] **Step 7: Add redirect and not-found tests**

Add inside `ConsultationServiceTests`:

```csharp
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
        var index = KeywordArchetypeIndex.Build(System.Array.Empty<Archetype>());
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
            KeywordArchetypeIndex.Build(System.Array.Empty<Archetype>()));

        var act = () => service.Consult("../../etc/passwd", SupportedLanguage.CSharp);

        act.Should().Throw<System.ArgumentException>()
           .WithMessage("*not a valid identifier*");
    }
```

- [ ] **Step 8: Run all consultation tests**

Run: `dotnet test --filter FullyQualifiedName~ConsultationServiceTests`
Expected: **5 passed**.

- [ ] **Step 9: Commit**

```bash
git add src/GuardCode.Content/Services/ConsultResult.cs src/GuardCode.Content/Services/IConsultationService.cs src/GuardCode.Content/Services/ConsultationService.cs tests/GuardCode.Content.Tests/ConsultationServiceTests.cs
git commit -m "content: add ConsultationService

Implements the consult() contract from spec §3.2 with the three
response shapes (normal composition, unsupported-language redirect
with or without equivalents_in suggestion, archetype-not-found).
Enforces the archetype ID regex at the service boundary as the
second line of the path-traversal defense from spec §6.1."
```

---

## Task 10: MCP Tool Handlers

**Files:**
- Create: `src/GuardCode.Mcp/Tools/PrepTool.cs`
- Create: `src/GuardCode.Mcp/Tools/ConsultTool.cs`

These are thin attribute-decorated classes that the `ModelContextProtocol` SDK reflects on to register MCP tools. They accept JSON-deserialized arguments, validate the `language` and `framework` enums, delegate to the services, and shape the result into an MCP response. All logic lives in the services — the tool handlers only translate.

The `ModelContextProtocol` SDK uses `[McpServerToolType]` on the class and `[McpServerTool, Description(...)]` on the method. If the preview API has renamed attributes, adapt to whatever shipped but keep the same responsibilities.

Tests for these live at the integration level (Task 13). Unit-testing the attribute-annotated static methods adds no signal beyond the service tests already in place.

- [ ] **Step 1: Create `PrepTool.cs`**

Create `src/GuardCode.Mcp/Tools/PrepTool.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using GuardCode.Content;
using GuardCode.Content.Services;
using ModelContextProtocol.Server;

namespace GuardCode.Mcp.Tools;

/// <summary>
/// MCP tool handler for the <c>prep</c> tool. Thin translator:
/// validates the language and framework enums, delegates to
/// <see cref="IPrepService"/>, and returns a serializable shape.
/// All scoring, filtering, and content lookup happens in the service.
/// </summary>
[McpServerToolType]
public static class PrepTool
{
    [McpServerTool(Name = "prep")]
    [Description(
        "Discover which GuardCode archetypes are relevant to an upcoming task. " +
        "Call this before writing a function or class: pass a natural-language " +
        "description of what you are about to build and the target language, " +
        "and receive up to 8 ranked archetype identifiers to consult().")]
    public static PrepToolResponse Run(
        IPrepService service,
        [Description("Free-text description of what you are about to write. Max 2000 chars.")] string intent,
        [Description("Target language. One of: csharp, python, c, go.")] string language,
        [Description("Optional framework hint. Accepted for forward compatibility; not used for filtering in MVP.")] string? framework = null)
    {
        if (!SupportedLanguageExtensions.TryParseWire(language, out var parsedLanguage))
        {
            return PrepToolResponse.Error(
                $"language '{language}' is not supported. Expected one of: csharp, python, c, go.");
        }

        var result = service.Prep(intent, parsedLanguage, framework);
        var matches = new List<PrepToolMatch>(result.Matches.Count);
        foreach (var match in result.Matches)
        {
            matches.Add(new PrepToolMatch(
                Archetype: match.ArchetypeId,
                Title: match.Title,
                Summary: match.Summary,
                Score: match.Score));
        }
        return new PrepToolResponse(matches, Error: null);
    }
}

public sealed record PrepToolMatch(string Archetype, string Title, string Summary, double Score);

public sealed record PrepToolResponse(
    IReadOnlyList<PrepToolMatch> Matches,
    string? Error)
{
    public static PrepToolResponse Error(string message)
        => new(System.Array.Empty<PrepToolMatch>(), message);
}
```

- [ ] **Step 2: Create `ConsultTool.cs`**

Create `src/GuardCode.Mcp/Tools/ConsultTool.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using GuardCode.Content;
using GuardCode.Content.Services;
using ModelContextProtocol.Server;

namespace GuardCode.Mcp.Tools;

/// <summary>
/// MCP tool handler for the <c>consult</c> tool. Translates MCP
/// arguments to service input, then reshapes the <see cref="ConsultResult"/>
/// into a wire format that matches design spec §3.2.
/// </summary>
[McpServerToolType]
public static class ConsultTool
{
    [McpServerTool(Name = "consult")]
    [Description(
        "Retrieve the full guidance document for one GuardCode archetype. " +
        "Returns principles plus language-specific implementation guidance " +
        "as a single composed markdown document. If the archetype does not " +
        "apply to the requested language, returns a redirect with a suggested " +
        "alternative when available.")]
    public static ConsultToolResponse Run(
        IConsultationService service,
        [Description("Archetype identifier, e.g. 'auth/password-hashing'.")] string archetype,
        [Description("Target language. One of: csharp, python, c, go.")] string language)
    {
        if (!SupportedLanguageExtensions.TryParseWire(language, out var parsedLanguage))
        {
            return ConsultToolResponse.ErrorResponse(
                archetype,
                language,
                $"language '{language}' is not supported. Expected one of: csharp, python, c, go.");
        }

        try
        {
            var result = service.Consult(archetype, parsedLanguage);
            return new ConsultToolResponse(
                Archetype: result.Archetype,
                Language: result.Language,
                Content: result.Content,
                Redirect: result.Redirect,
                NotFound: result.NotFound,
                Message: result.Message,
                Suggested: result.Suggested,
                RelatedArchetypes: result.RelatedArchetypes,
                References: result.References,
                Error: null);
        }
        catch (System.ArgumentException ex)
        {
            return ConsultToolResponse.ErrorResponse(archetype, language, ex.Message);
        }
    }
}

public sealed record ConsultToolResponse(
    string Archetype,
    string Language,
    string? Content,
    bool Redirect,
    bool NotFound,
    string? Message,
    IReadOnlyList<string> Suggested,
    IReadOnlyList<string> RelatedArchetypes,
    IReadOnlyDictionary<string, string> References,
    string? Error)
{
    public static ConsultToolResponse ErrorResponse(string archetype, string language, string error)
        => new(
            Archetype: archetype,
            Language: language,
            Content: null,
            Redirect: false,
            NotFound: false,
            Message: null,
            Suggested: System.Array.Empty<string>(),
            RelatedArchetypes: System.Array.Empty<string>(),
            References: new Dictionary<string, string>(),
            Error: error);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build SecureCodingMcp.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Note: `GuardCode.Mcp` still has the stub `Program.cs` from Task 1 — the build will succeed because the stub is a valid top-level-statements program. The tool types will be picked up automatically by `.WithToolsFromAssembly()` once we write the real composition root in Task 11.

If the build fails because `McpServerToolType` / `McpServerTool` attributes do not exist under those names in the pinned SDK version, the likely replacement is `[McpServerTool]` on a class and `[McpServerFunction]` on methods, or vice versa. Check the SDK sample in the NuGet package README (via `dotnet nuget locals all --list` to find the cache) and adapt the attribute names without changing the method signatures. If that happens, also update this plan header with a note.

- [ ] **Step 4: Commit**

```bash
git add src/GuardCode.Mcp/Tools/
git commit -m "mcp: add PrepTool and ConsultTool handlers

Thin attribute-decorated translators that validate the language
wire form, delegate to IPrepService and IConsultationService, and
shape the service responses into MCP wire DTOs per spec §3. No
business logic in the handlers — the services own all decisions
and remain the only thing unit-tested, so MCP SDK changes can't
break our core behavior."
```

---

## Task 11: Composition Root (Program.cs)

**Files:**
- Modify: `src/GuardCode.Mcp/Program.cs`
- Create: `src/GuardCode.Mcp/appsettings.json`

`Program.cs` wires everything together using the .NET generic host. It reads the archetypes root path from configuration (environment variable `GUARDCODE_ARCHETYPES_ROOT` or `appsettings.json`, defaulting to `./archetypes` relative to the executable), loads the corpus eagerly, builds the index, registers the services, and starts the stdio MCP event loop.

Critical: `Console.Out` must stay reserved for the MCP protocol on stdio. Logging goes to `Console.Error` (stderr) only.

- [ ] **Step 1: Create `appsettings.json`**

Create `src/GuardCode.Mcp/appsettings.json`:

```json
{
  "GuardCode": {
    "ArchetypesRoot": "archetypes"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Warning"
    }
  }
}
```

Then edit `src/GuardCode.Mcp/GuardCode.Mcp.csproj` and add the appsettings file to the output:

```xml
  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 2: Replace `Program.cs` with the real composition root**

Overwrite `src/GuardCode.Mcp/Program.cs`:

```csharp
using System;
using System.IO;
using GuardCode.Content;
using GuardCode.Content.Indexing;
using GuardCode.Content.Loading;
using GuardCode.Content.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Stdio is reserved for the MCP protocol. All logging MUST go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Resolve the archetypes root once at startup. Precedence:
// 1. Environment variable GUARDCODE_ARCHETYPES_ROOT (absolute or relative to cwd)
// 2. appsettings.json "GuardCode:ArchetypesRoot"
// 3. "archetypes" next to the executable
var configured = Environment.GetEnvironmentVariable("GUARDCODE_ARCHETYPES_ROOT")
    ?? builder.Configuration["GuardCode:ArchetypesRoot"]
    ?? "archetypes";
var archetypesRoot = Path.IsPathRooted(configured)
    ? configured
    : Path.GetFullPath(configured, AppContext.BaseDirectory);

builder.Services
    .AddSingleton<IArchetypeRepository>(_ => new FileSystemArchetypeRepository(archetypesRoot))
    .AddSingleton<IArchetypeIndex>(sp =>
    {
        var repo = sp.GetRequiredService<IArchetypeRepository>();
        return KeywordArchetypeIndex.Build(repo.LoadAll());
    })
    .AddSingleton<IPrepService, PrepService>()
    .AddSingleton<IConsultationService, ConsultationService>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

// Force-load the index synchronously before entering the event loop so that
// any content validation error aborts startup with a clear stderr message
// instead of surfacing during the first MCP call.
try
{
    _ = host.Services.GetRequiredService<IArchetypeIndex>();
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GuardCode.Startup");
    logger.LogCritical(ex, "GuardCode failed to load archetype corpus from {Root}", archetypesRoot);
    return 1;
}

await host.RunAsync().ConfigureAwait(false);
return 0;
```

- [ ] **Step 3: Build**

Run: `dotnet build SecureCodingMcp.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

If `AddMcpServer` / `WithStdioServerTransport` / `WithToolsFromAssembly` extension methods are not present, check the actual namespaces under the SDK (commonly `ModelContextProtocol.Server` or `ModelContextProtocol.Hosting`) and add the corresponding `using` directive. Do not replace the extension-method style with manual registration — the point of the SDK is that these extensions handle the MCP protocol plumbing.

- [ ] **Step 4: Smoke-start with a non-existent archetypes directory**

Run:
```bash
set GUARDCODE_ARCHETYPES_ROOT=F:\repositories\SecureCodingMcp\nonexistent-root
dotnet run --project src/GuardCode.Mcp --no-build
```

Expected: the process prints a critical-level log line on stderr saying it failed to load the archetype corpus from the non-existent root, and exits with code 1. (Do not worry about stdin — the process exits before the MCP event loop starts.)

On bash you can do the same with:
```bash
GUARDCODE_ARCHETYPES_ROOT=F:/repositories/SecureCodingMcp/nonexistent-root dotnet run --project src/GuardCode.Mcp --no-build
```

Then unset the variable so later steps aren't affected:
```bash
unset GUARDCODE_ARCHETYPES_ROOT
```

- [ ] **Step 5: Commit**

```bash
git add src/GuardCode.Mcp/Program.cs src/GuardCode.Mcp/appsettings.json src/GuardCode.Mcp/GuardCode.Mcp.csproj
git commit -m "mcp: wire composition root and stdio MCP server

Program.cs builds the .NET generic host, resolves the archetypes
root from GUARDCODE_ARCHETYPES_ROOT / appsettings.json / default,
registers the content pipeline as singletons, eager-loads the
index before entering the event loop so content errors abort
startup with a clear stderr message, and reserves stdout for
the MCP protocol by routing all logging to stderr."
```

---

## Task 12: Sample Content (3 Smoke-Test Archetypes)

**Files:**
- Create: `archetypes/auth/password-hashing/_principles.md`
- Create: `archetypes/auth/password-hashing/csharp.md`
- Create: `archetypes/auth/password-hashing/python.md`
- Create: `archetypes/io/input-validation/_principles.md`
- Create: `archetypes/io/input-validation/csharp.md`
- Create: `archetypes/io/input-validation/python.md`
- Create: `archetypes/io/input-validation/c.md`
- Create: `archetypes/errors/error-handling/_principles.md`
- Create: `archetypes/errors/error-handling/csharp.md`
- Create: `archetypes/errors/error-handling/go.md`

Three MVP archetypes with full principles + language files. These exist to (a) prove the schema works end-to-end, (b) give the integration test something real to load, and (c) serve as reference for the remaining seven archetypes that will be authored separately as a content effort. Remember:

- Each language file ≤ 200 lines total (frontmatter included).
- Reference implementation ≤ 40 non-empty code lines.
- Every required section must be present.
- Frontmatter `archetype` field must match the directory path.

The guidance itself is the *hardest* part of this task — it must be genuinely useful. Do not generate placeholder prose. When in doubt, err on the side of leaving a section short and focused rather than filling it with filler text.

- [ ] **Step 1: Write `auth/password-hashing/_principles.md`**

Create `archetypes/auth/password-hashing/_principles.md`:

```markdown
---
schema_version: 1
archetype: auth/password-hashing
title: Password Hashing
summary: Storing, verifying, and handling user passwords in any backend.
applies_to: [csharp, python, go]
keywords:
  - password
  - credential
  - login
  - hash
  - bcrypt
  - argon2
  - pbkdf2
  - kdf
related_archetypes:
  - auth/session-tokens
references:
  owasp_asvs: V2.4
  owasp_cheatsheet: Password Storage Cheat Sheet
  cwe: "916"
---

# Password Hashing — Principles

## When this applies
Any time your system stores a user-chosen password, verifies a login attempt, or transmits a credential through a component you control. This does **not** apply to API keys, access tokens, or other high-entropy machine secrets — those use different primitives (see `persistence/secrets-handling`).

## Architectural placement
Password handling lives behind a dedicated abstraction — typically a `PasswordHasher` or `CredentialService` — that HTTP handlers, CLI commands, and admin tools all go through. No route handler, data-access layer, or view should ever call a hashing library directly. This keeps algorithm selection, parameter tuning, and migration logic in exactly one place, and makes password logic independently testable and auditable.

## Principles
1. **Use a modern memory-hard KDF.** Argon2id is the current default. PBKDF2 is acceptable only when required by FIPS compliance or an existing database you can't migrate.
2. **Never invent your own scheme.** Do not "hash and salt" with SHA-256. Do not add a homegrown pepper unless you have a documented reason and a key-rotation plan.
3. **Tune cost parameters for your hardware.** Target 200–500 ms per hash on the production server. Re-tune when hardware changes.
4. **Verify in constant time.** Use the library's verify function, never a manual string comparison.
5. **Rehash on login when parameters change.** When you upgrade the cost factor, verify the old hash, then silently re-hash and update the database if the user's stored hash uses outdated parameters.
6. **Plaintext passwords live only on the stack.** Never log them. Never include them in error messages. Never serialize them. Never store them even temporarily in persistent caches.

## Anti-patterns
- Storing MD5, SHA-1, or SHA-256 hashes of passwords ("fast hash" algorithms are not password hashes).
- Concatenating a salt with SHA-256 and calling it "salted hashing."
- Building your own pepper / key-wrap scheme without a documented threat model.
- Using `==` to compare hashes (timing-attack surface).
- Logging the hashed password at debug level.
- Returning a different error for "user not found" vs "wrong password" (username enumeration).

## References
- OWASP ASVS V2.4 — Stored Credential Verifier Requirements
- OWASP Password Storage Cheat Sheet
- CWE-916 — Use of Password Hash With Insufficient Computational Effort
```

- [ ] **Step 2: Write `auth/password-hashing/csharp.md`**

Create `archetypes/auth/password-hashing/csharp.md`:

```markdown
---
schema_version: 1
archetype: auth/password-hashing
language: csharp
principles_file: _principles.md
libraries:
  preferred: Konscious.Security.Cryptography.Argon2
  acceptable:
    - BCrypt.Net-Next
  avoid:
    - name: System.Security.Cryptography.Rfc2898DeriveBytes
      reason: PBKDF2 only — acceptable for FIPS, not preferred for greenfield.
    - name: System.Security.Cryptography.SHA256
      reason: Fast hash, not a password hash. Never use for credentials.
minimum_versions:
  dotnet: "10.0"
---

# Password Hashing — C#

## Library choice
`Konscious.Security.Cryptography.Argon2` gives you Argon2id with tunable memory, iteration, and parallelism parameters. It is community-maintained but widely audited. `BCrypt.Net-Next` is acceptable if you have an existing bcrypt database to interoperate with.

## Reference implementation
```csharp
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

public sealed class Argon2PasswordHasher
{
    private const int DegreeOfParallelism = 4;
    private const int MemorySize = 65_536; // 64 MiB
    private const int Iterations = 3;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Compute(password, salt);
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={DegreeOfParallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        var salt = Convert.FromBase64String(parts[^2]);
        var expected = Convert.FromBase64String(parts[^1]);
        var actual = Compute(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Compute(string password, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySize,
            Iterations = Iterations
        };
        return argon.GetBytes(HashSize);
    }
}
```

## Language-specific gotchas
- Use `CryptographicOperations.FixedTimeEquals` for hash comparison — `SequenceEqual` leaks timing information.
- `RandomNumberGenerator.GetBytes` is the right source of salt randomness. Never `System.Random`.
- Store the encoded string (including algorithm + parameters) as the column value, not the raw hash bytes. That's what makes rehash-on-login possible when you upgrade parameters.
- Wrap this in a `sealed` class behind an `IPasswordHasher` interface so routes never take a dependency on the concrete library.

## Tests to write
- Round-trip: `Verify(password, Hash(password))` is true for a handful of inputs including unicode and long strings.
- Negative: wrong password returns false, and the verify is constant-time in shape (don't early-return).
- Parameter drift: an encoded hash with old parameters still verifies, but the service also signals a rehash is needed.
- Salt uniqueness: hashing the same password twice yields distinct encoded strings.
```

- [ ] **Step 3: Write `auth/password-hashing/python.md`**

Create `archetypes/auth/password-hashing/python.md`:

```markdown
---
schema_version: 1
archetype: auth/password-hashing
language: python
principles_file: _principles.md
libraries:
  preferred: argon2-cffi
  acceptable:
    - passlib
  avoid:
    - name: hashlib
      reason: Fast hashes (SHA-256, etc.) are not password hashes.
    - name: bcrypt
      reason: Outdated unless you need bcrypt interop with an existing DB.
minimum_versions:
  python: "3.10"
---

# Password Hashing — Python

## Library choice
`argon2-cffi` ships Argon2id with sensible defaults. `passlib` is the classic abstraction layer; it is acceptable but its own API is larger than you need. If you pick `passlib`, pin to the Argon2 scheme specifically.

## Reference implementation
```python
from argon2 import PasswordHasher
from argon2.exceptions import VerifyMismatchError, InvalidHashError

_hasher = PasswordHasher(
    time_cost=3,
    memory_cost=64 * 1024,  # 64 MiB
    parallelism=4,
    hash_len=32,
    salt_len=16,
)

def hash_password(password: str) -> str:
    """Return an encoded Argon2id hash, parameters embedded."""
    return _hasher.hash(password)

def verify_password(password: str, encoded: str) -> tuple[bool, bool]:
    """Return (is_valid, needs_rehash). Constant-time under the hood."""
    try:
        _hasher.verify(encoded, password)
    except (VerifyMismatchError, InvalidHashError):
        return False, False
    return True, _hasher.check_needs_rehash(encoded)
```

## Language-specific gotchas
- `argon2-cffi`'s `PasswordHasher` object is re-entrant and cheap to reuse — construct it once at module import, not per request.
- `check_needs_rehash` is the signal to quietly re-hash the password on a successful login after you upgrade parameters.
- Do not wrap passwords in `bytes` and then `decode()`. Pass `str` directly; the library handles encoding.
- Exception types are broader than you might expect: catch `VerifyMismatchError` and `InvalidHashError` explicitly, not bare `Exception`, so malformed hash strings in the database surface distinctly.
- If you use Django, its built-in `PBKDF2PasswordHasher` is fine for new projects but Argon2id via `django-argon2` is better.

## Tests to write
- Round-trip: `verify_password(pw, hash_password(pw))` is `(True, False)` when parameters are current.
- Negative: wrong password returns `(False, False)` and raises nothing.
- Parameter drift: construct a `PasswordHasher` with lower `time_cost`, hash, then verify against a re-tuned module — expect `needs_rehash=True`.
- Salt uniqueness: hashing the same password twice yields distinct encoded strings.
```

- [ ] **Step 4: Write `io/input-validation/_principles.md`**

Create `archetypes/io/input-validation/_principles.md`:

```markdown
---
schema_version: 1
archetype: io/input-validation
title: Input Validation
summary: Validating untrusted input at every trust boundary.
applies_to: [csharp, python, c, go]
keywords:
  - validation
  - input
  - sanitize
  - parse
  - schema
  - trust
  - boundary
  - untrusted
related_archetypes:
  - auth/api-endpoint-authentication
references:
  owasp_asvs: V5.1
  owasp_cheatsheet: Input Validation Cheat Sheet
  cwe: "20"
---

# Input Validation — Principles

## When this applies
At every boundary where data enters your trust zone from a less-trusted one: HTTP request bodies, query strings, CLI arguments, file contents, message-bus payloads, configuration files. If you can't prove the data came from code you wrote and control, it must be validated before it influences a decision.

## Architectural placement
Validation happens **as close to the edge as possible** and produces strongly-typed domain objects, not free-form dictionaries. The rest of the system receives only validated types. Leaking raw request bodies into business logic is how "validated once, then trusted everywhere" degrades into "validated nowhere in particular."

## Principles
1. **Parse, don't validate.** Convert the untrusted payload into a domain type in one step. A successfully parsed `UserRegistration` is a *proof* that every field met its invariants; downstream code then trusts the type system.
2. **Whitelist, not blacklist.** Define the set of allowed values, shapes, and patterns. Rejecting "known bad" is always incomplete.
3. **Validate *meaning*, not just syntax.** A syntactically valid email that doesn't belong to your tenant is still invalid for that operation.
4. **Fail closed.** On any validation failure, stop processing and return an error. Never try to "fix up" the input.
5. **Bound every collection and every string.** Untrusted input with no upper bound is a denial-of-service vector.
6. **Normalize before you validate.** Unicode, path separators, encoding — normalize to a canonical form before checking, or attackers will bypass your checks with equivalent-but-different byte sequences.

## Anti-patterns
- Regex-scrubbing "bad characters" from input to "make it safe."
- Accepting `dict` or `object` through multiple layers and validating "eventually."
- Validating only length, not content.
- Catching validation exceptions and continuing with defaulted values.
- Running validation twice (in a middleware and again in the handler) with different rules.

## References
- OWASP ASVS V5.1 — Input Validation Requirements
- OWASP Input Validation Cheat Sheet
- CWE-20 — Improper Input Validation
```

- [ ] **Step 5: Write `io/input-validation/csharp.md`**

Create `archetypes/io/input-validation/csharp.md`:

```markdown
---
schema_version: 1
archetype: io/input-validation
language: csharp
principles_file: _principles.md
libraries:
  preferred: FluentValidation
  acceptable:
    - System.ComponentModel.DataAnnotations
  avoid:
    - name: Manual regex in controllers
      reason: Scatters rules across the codebase; impossible to audit.
minimum_versions:
  dotnet: "10.0"
---

# Input Validation — C#

## Library choice
`FluentValidation` keeps validation rules in dedicated classes next to the domain types. `DataAnnotations` is acceptable for simple cases but gets awkward when rules depend on each other or need async work.

## Reference implementation
```csharp
using FluentValidation;

public sealed record UserRegistration(string Email, string Password, int Age);

public sealed class UserRegistrationValidator : AbstractValidator<UserRegistration>
{
    public UserRegistrationValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12)
            .MaximumLength(128);
        RuleFor(x => x.Age)
            .InclusiveBetween(13, 120);
    }
}

public static class RegistrationEndpoint
{
    public static async Task<IResult> Handle(
        UserRegistration request,
        IValidator<UserRegistration> validator,
        IUserService users)
    {
        var result = await validator.ValidateAsync(request);
        if (!result.IsValid)
        {
            return Results.ValidationProblem(result.ToDictionary());
        }
        await users.RegisterAsync(request);
        return Results.Created();
    }
}
```

## Language-specific gotchas
- Minimal APIs will happily bind a `record` from JSON even when fields are missing — `[Required]` or an equivalent `NotEmpty()` rule is still your job.
- Prefer records over classes for DTOs so you get value equality and immutability for free.
- Don't register a validator as a singleton if it depends on scoped services (e.g., a database context). Use `AddValidatorsFromAssemblyContaining<T>()` so FluentValidation picks the right lifetime.
- Set `MaximumLength` on every string, always. Unbounded strings are a DoS vector.

## Tests to write
- Round-trip: valid request validates cleanly.
- Each invalid-field variant produces a specific error message keyed by field name.
- Boundary: values exactly at min/max length and age bounds validate.
- Malicious: oversized strings (>254 for email, >128 for password) are rejected before they reach the service.
```

- [ ] **Step 6: Write `io/input-validation/python.md`**

Create `archetypes/io/input-validation/python.md`:

```markdown
---
schema_version: 1
archetype: io/input-validation
language: python
principles_file: _principles.md
libraries:
  preferred: pydantic
  acceptable:
    - attrs
  avoid:
    - name: Hand-rolled dict validation
      reason: Turns every handler into an audit liability.
minimum_versions:
  python: "3.11"
---

# Input Validation — Python

## Library choice
`pydantic` v2 gives you parse-don't-validate by default: a model instance *is* the proof that the input was valid. `attrs` with validators is acceptable if you already use it project-wide.

## Reference implementation
```python
from pydantic import BaseModel, EmailStr, Field, ValidationError

class UserRegistration(BaseModel):
    email: EmailStr = Field(max_length=254)
    password: str = Field(min_length=12, max_length=128)
    age: int = Field(ge=13, le=120)

    model_config = {
        "extra": "forbid",      # unknown fields are a validation failure
        "str_strip_whitespace": True,
    }

def register_handler(payload: dict) -> UserRegistration:
    """Validate and return the domain object, or raise.

    Callers catch ValidationError at the framework layer and
    return 400 with the formatted errors.
    """
    try:
        return UserRegistration.model_validate(payload)
    except ValidationError:
        # Re-raise so the framework maps it to a 400. Don't swallow.
        raise
```

## Language-specific gotchas
- `extra="forbid"` is load-bearing: without it, attackers can pass unexpected fields that later code silently ignores.
- `EmailStr` requires `email-validator` (install as `pydantic[email]`).
- Pydantic coerces types by default (`"42"` → `42`). If you want strict mode, use `model_config = {"strict": True}`.
- Don't pass `dict(request.json)` — pass the original dict directly. `dict()` on a dict is noise, and on a FastAPI request body, FastAPI already validated against this model if you typed the parameter.
- Resist the urge to catch `ValidationError` inside `register_handler` and return a default. Fail closed.

## Tests to write
- Round-trip: valid payload yields a populated `UserRegistration`.
- Each invalid-field variant raises `ValidationError` with a specific loc.
- `extra="forbid"`: unknown keys raise `ValidationError`.
- Boundaries: exact min/max length and age bounds accept.
- Very large payloads (>10x normal size) are rejected before deserialization at the framework level — if not, add a body size limit.
```

- [ ] **Step 7: Write `io/input-validation/c.md`**

Create `archetypes/io/input-validation/c.md`:

```markdown
---
schema_version: 1
archetype: io/input-validation
language: c
principles_file: _principles.md
libraries:
  preferred: hand-rolled parsers with explicit bounds
  acceptable:
    - cJSON
  avoid:
    - name: scanf family
      reason: Easy to misuse; no bounds on %s without explicit width.
    - name: gets
      reason: Removed from C11. Never use.
minimum_versions:
  c: "C11"
---

# Input Validation — C

## Library choice
C has no "validation framework" and you should not pretend it does. Write small, bounded parsers per input shape, check every length, and reject early. For JSON inputs, `cJSON` is acceptable but still forces you to own every bounds check manually.

## Reference implementation
```c
#include <stddef.h>
#include <stdint.h>
#include <string.h>
#include <stdbool.h>

#define MAX_USERNAME 64
#define MAX_EMAIL    254

typedef struct {
    char username[MAX_USERNAME + 1];
    char email[MAX_EMAIL + 1];
    uint8_t age;
} user_registration_t;

typedef enum {
    VALIDATION_OK = 0,
    VALIDATION_BAD_USERNAME,
    VALIDATION_BAD_EMAIL,
    VALIDATION_BAD_AGE,
    VALIDATION_OVERSIZE,
} validation_result_t;

validation_result_t parse_user_registration(
    const char *username, size_t username_len,
    const char *email, size_t email_len,
    int age_in,
    user_registration_t *out)
{
    if (username_len == 0 || username_len > MAX_USERNAME) return VALIDATION_OVERSIZE;
    if (email_len == 0 || email_len > MAX_EMAIL) return VALIDATION_OVERSIZE;
    if (age_in < 13 || age_in > 120) return VALIDATION_BAD_AGE;

    if (memchr(email, '@', email_len) == NULL) return VALIDATION_BAD_EMAIL;

    memcpy(out->username, username, username_len);
    out->username[username_len] = '\0';
    memcpy(out->email, email, email_len);
    out->email[email_len] = '\0';
    out->age = (uint8_t)age_in;
    return VALIDATION_OK;
}
```

## Language-specific gotchas
- Always pass explicit lengths. Do not rely on `strlen` on buffers coming from the network — if the sender didn't null-terminate, `strlen` runs into the weeds.
- `memcpy` instead of `strcpy`, with the length already bounds-checked.
- `out->username[username_len] = '\0'` only after the bounds check — off-by-one here is a classic overflow.
- Enum return codes, not bare `int`s. Callers can `switch` on them without guessing.
- Never use `sprintf` to build anything from user input. `snprintf` with a bounded buffer is mandatory.

## Tests to write
- Each invalid branch returns its specific enum value.
- Exact-length boundary cases (`MAX_USERNAME`, `MAX_USERNAME + 1`) behave correctly.
- Fuzz with random bytes — the parser must never read past `*_len`.
- Non-ASCII usernames are handled deliberately: either allowed (document the policy) or rejected (validate with a whitelist).
```

- [ ] **Step 8: Write `errors/error-handling/_principles.md`**

Create `archetypes/errors/error-handling/_principles.md`:

```markdown
---
schema_version: 1
archetype: errors/error-handling
title: Error Handling
summary: Structuring error paths so failures are observable, actionable, and safe.
applies_to: [csharp, python, c, go]
keywords:
  - error
  - exception
  - failure
  - result
  - panic
  - recover
  - logging
related_archetypes:
  - io/input-validation
references:
  owasp_asvs: V7.4
  cwe: "755"
---

# Error Handling — Principles

## When this applies
Every function that can fail. "Can fail" includes: I/O, parsing, network calls, arithmetic on untrusted numbers, concurrent operations, and anything that reaches into a third-party library. Error handling is not an afterthought layered on working code — it is part of the function's contract from the first line you write.

## Architectural placement
Errors move outward in clearly defined layers. Low-level code surfaces primitive failure modes (an enum, a result type, or a narrow exception). Mid-level code translates primitive failures into domain failures that carry enough context for a human to act. The edge of the system (HTTP handler, CLI, message consumer) translates domain failures into the wire format appropriate for that channel.

At every translation, you either handle the error (with a specific reason why this layer is the right place to handle it) or you wrap it with context and re-raise. "Catch, log, continue" is almost always a bug.

## Principles
1. **Every error is either handled or propagated, never both.** If you catch it and keep going, the function must document why — and that why must survive code review.
2. **Preserve the causal chain.** Wrap errors with context but do not drop the underlying cause. Debuggability depends on the chain.
3. **Errors that reach the user are sanitized; errors that reach the log are rich.** Stack traces, SQL, file paths, and internal state belong in structured logs, not in HTTP responses.
4. **Fail closed.** An operation either completes successfully or has no effect. Partial writes, half-applied migrations, and "we'll retry later" without a retry mechanism are bugs.
5. **Log the error once.** Not at every layer. The log entry lives as close to the translation boundary as possible.
6. **Distinguish expected from unexpected.** Expected failures (validation, auth denied) are flow control and should not generate alerts. Unexpected failures (null pointer, disk full) should.

## Anti-patterns
- `catch (Exception) { }` — a silent swallow.
- Returning sentinel values like `-1` or `null` to signal failure from functions that can also legitimately return those values.
- Logging and re-throwing at every level, producing a cascade of identical log entries.
- Exposing internal stack traces in error responses.
- Catching a broad exception type and re-raising a generic one that drops the original cause.
- Using exceptions for ordinary control flow — the performance hit is real, and the readability hit is worse.

## References
- OWASP ASVS V7.4 — Error Handling
- CWE-755 — Improper Handling of Exceptional Conditions
```

- [ ] **Step 9: Write `errors/error-handling/csharp.md`**

Create `archetypes/errors/error-handling/csharp.md`:

```markdown
---
schema_version: 1
archetype: errors/error-handling
language: csharp
principles_file: _principles.md
libraries:
  preferred: built-in exceptions
  acceptable:
    - OneOf
    - FluentResults
  avoid:
    - name: Custom exception hierarchies with dozens of types
      reason: Hard to maintain; usually signals insufficient domain modeling.
minimum_versions:
  dotnet: "10.0"
---

# Error Handling — C#

## Library choice
Use the built-in exception system for unexpected failures. For expected domain failures that routinely drive flow control (validation rejection, "not found", "already exists"), a result type like `OneOf<T, TError>` or `FluentResults` keeps the happy path exception-free and makes intent explicit.

## Reference implementation
```csharp
public sealed record DomainError(string Code, string Message);

public sealed class OrderService(IOrderRepository orders, ILogger<OrderService> logger)
{
    public async Task<OneOf<Order, DomainError>> SubmitAsync(SubmitOrderRequest request)
    {
        try
        {
            var existing = await orders.GetAsync(request.OrderId);
            if (existing is not null)
            {
                return new DomainError("order.exists", $"Order {request.OrderId} already submitted.");
            }

            var order = Order.Create(request);
            await orders.SaveAsync(order);
            return order;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict saving order {OrderId}", request.OrderId);
            return new DomainError("order.conflict", "Order was modified concurrently; please retry.");
        }
    }
}
```

## Language-specific gotchas
- Prefer `async Task<T>` over `async void` — exceptions in `async void` tear down the process.
- `catch (Exception)` without re-throwing should be reserved for the outermost boundary (an `ExceptionHandlerMiddleware` in ASP.NET, for example). Elsewhere, catch specific types.
- Use `logger.LogError(ex, "...")` with a message template, not `logger.LogError(ex.ToString())`. The template lets structured logging extract fields.
- `throw;` preserves the stack trace. `throw ex;` resets it and makes bugs unfindable.
- `.Result` and `.Wait()` on tasks cause deadlocks in sync-over-async contexts. Always `await`.

## Tests to write
- Happy path returns the success branch of `OneOf<Order, DomainError>`.
- Duplicate order returns `order.exists` with the expected code.
- A simulated concurrency exception returns `order.conflict` and logs at Warning.
- The service never swallows an unrecognized exception — assert it propagates.
```

- [ ] **Step 10: Write `errors/error-handling/go.md`**

Create `archetypes/errors/error-handling/go.md`:

```markdown
---
schema_version: 1
archetype: errors/error-handling
language: go
principles_file: _principles.md
libraries:
  preferred: standard library errors + fmt.Errorf wrapping
  acceptable:
    - github.com/pkg/errors (legacy codebases)
  avoid:
    - name: panic for ordinary errors
      reason: Panics are for truly unrecoverable invariants, not failed I/O.
minimum_versions:
  go: "1.22"
---

# Error Handling — Go

## Library choice
The standard library's `errors` package plus `fmt.Errorf` with `%w` covers almost every case. Sentinel errors (`var ErrNotFound = errors.New(...)`) and typed errors (implementing `error`) together let callers decide what to handle.

## Reference implementation
```go
package orders

import (
    "context"
    "errors"
    "fmt"
    "log/slog"
)

var ErrAlreadySubmitted = errors.New("order already submitted")

type Repository interface {
    Get(ctx context.Context, id string) (*Order, error)
    Save(ctx context.Context, o *Order) error
}

type Service struct {
    repo   Repository
    logger *slog.Logger
}

func (s *Service) Submit(ctx context.Context, req SubmitRequest) (*Order, error) {
    existing, err := s.repo.Get(ctx, req.OrderID)
    if err != nil && !errors.Is(err, ErrNotFound) {
        return nil, fmt.Errorf("orders: lookup %s: %w", req.OrderID, err)
    }
    if existing != nil {
        return nil, ErrAlreadySubmitted
    }
    order, err := NewOrder(req)
    if err != nil {
        return nil, fmt.Errorf("orders: build %s: %w", req.OrderID, err)
    }
    if err := s.repo.Save(ctx, order); err != nil {
        s.logger.WarnContext(ctx, "save failed",
            slog.String("order_id", req.OrderID),
            slog.String("error", err.Error()))
        return nil, fmt.Errorf("orders: save %s: %w", req.OrderID, err)
    }
    return order, nil
}
```

## Language-specific gotchas
- `%w` in `fmt.Errorf` wraps so callers can use `errors.Is` / `errors.As`. `%v` or `%s` drops the chain — don't.
- Always handle the error right after the call. Accumulating checked errors in a list and dealing with them later loses context.
- `panic` is for programmer errors, not user errors. A failed DB call is not a panic.
- `defer` for cleanup, but watch closure capture: `defer f(err)` captures `err` at defer time, not at call time.
- Log errors at the boundary that decides to convert them into user-visible responses, not at every wrap. One error, one log line.

## Tests to write
- Happy path returns the order and no error.
- Duplicate submit returns `ErrAlreadySubmitted` — use `errors.Is` in the test.
- Repo `Get` returning a generic error is wrapped with context (check `err.Error()` contains the order ID).
- Repo `Save` failing logs a warning with structured fields.
```

- [ ] **Step 11: Verify all 10 files parse, load, and validate**

Run:
```bash
dotnet build SecureCodingMcp.slnx
dotnet run --project src/GuardCode.Mcp --no-build < /dev/null
```

The second command starts the MCP server with an empty stdin. On Windows bash this sends EOF immediately, so the server should startup-load the corpus, enter the event loop, and then terminate on EOF. If loading fails (e.g., missing section, bad frontmatter), a critical log on stderr identifies the offending file — fix it and re-run. Expected outcome: the process loads all 10 files without error and exits cleanly.

On Windows CMD use `echo. | dotnet run --project src/GuardCode.Mcp --no-build` instead.

- [ ] **Step 12: Commit**

```bash
git add archetypes/
git commit -m "content: add three smoke-test archetypes

auth/password-hashing (csharp, python), io/input-validation
(csharp, python, c), errors/error-handling (csharp, go).
Ten markdown files total. These demonstrate the full content
schema end-to-end: per-language divergence inside a shared
principles frame, language-exclusive guidance (C memory safety
in io/input-validation), and realistic library/gotcha sections.
The remaining 7 MVP archetypes are deferred to a content-
authoring effort tracked separately from this plan."
```

---

## Task 13: Integration Smoke Test

**Files:**
- Create: `tests/GuardCode.Content.Tests/ContentCorpusSmokeTests.cs`

One integration test that loads the *real* `archetypes/` directory (not a fake temp directory) and exercises the full pipeline: repository → index → prep → consult. This is the first line of defense against broken content in CI.

The test needs to find the archetypes directory at runtime. Since `dotnet test` runs from `tests/GuardCode.Content.Tests/bin/Debug/net10.0`, we walk up until we find `SecureCodingMcp.slnx` and then resolve `archetypes/` beside it.

- [ ] **Step 1: Write the smoke test**

Create `tests/GuardCode.Content.Tests/ContentCorpusSmokeTests.cs`:

```csharp
using System.IO;
using GuardCode.Content;
using GuardCode.Content.Indexing;
using GuardCode.Content.Loading;
using GuardCode.Content.Services;

namespace GuardCode.Content.Tests;

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
        result.Matches[0].ArchetypeId.Should().Be("auth/password-hashing");
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
        result.Content.Should().Contain("\n\n---\n\n"); // separator
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
        result.Content.Should().NotBeNull().And.NotBeEmpty();
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
```

- [ ] **Step 2: Run the smoke tests**

Run: `dotnet test --filter FullyQualifiedName~ContentCorpusSmokeTests`
Expected: **5 passed**. If `Prep_FindsPasswordHashingForHashingIntent` fails because another archetype scores higher, relax the assertion to `.Should().Contain(m => m.ArchetypeId == "auth/password-hashing")` — content quality is hand-tuned and a specific top-1 ordering is brittle.

- [ ] **Step 3: Run the full test suite to confirm nothing regressed**

Run: `dotnet test SecureCodingMcp.slnx`
Expected: **all tests pass**. This is the first time we're asserting the whole suite green.

- [ ] **Step 4: Commit**

```bash
git add tests/GuardCode.Content.Tests/ContentCorpusSmokeTests.cs
git commit -m "tests: add content-corpus smoke test

Loads the real archetypes/ directory through the production
pipeline (FileSystemArchetypeRepository → KeywordArchetypeIndex
→ PrepService / ConsultationService) and asserts the three MVP
archetypes parse, validate, and are discoverable by realistic
queries. This is the first line of defense against broken content
in CI — unit tests alone can't catch a typo in a real markdown
file."
```

---

## Task 14: README and CONTRIBUTING

**Files:**
- Create: `README.md`
- Create: `CONTRIBUTING.md`

The README introduces GuardCode, states the GUARD expansion verbatim (spec §1), explains what the MCP does and does not do, shows a quick example of `prep` and `consult`, points to CONTRIBUTING.md for the schema, and links the design spec. CONTRIBUTING.md explains how to add a new archetype or a new language file for an existing archetype, documents the frontmatter schema, and states the line/code budgets.

- [ ] **Step 1: Write `README.md`**

Create `README.md` at the repo root:

```markdown
# GuardCode

**GUARD — Global Unified AI Rules for Development**

GuardCode is an open-source Model Context Protocol (MCP) server that gives any LLM a high-to-low-level architecture consultant it can call before writing a function or class. It ships human-authored engineering guidance — principles, architectural placement, anti-patterns, library choices, and language-specific gotchas — organized into focused **archetypes** that the LLM retrieves via two tools: `prep` and `consult`.

GuardCode is **not** a static analyzer, not a ruleset, not an LLM, and not an agent. It is a deterministic content-delivery server. The intelligence lives in the content, which is written and reviewed by humans through a PR workflow.

## Why

LLMs generate code that works but defaults to the insecure and architecturally poor path: MD5-hashed passwords, SQL string concatenation, god-functions that mix HTTP and persistence concerns, inconsistent error handling. The fix isn't to bolt more checks onto generated code — it's to give the LLM a place to consult *before* it writes the function. That's what GuardCode does.

## What it is

- A C# 14 / .NET 10 MCP server (stdio transport)
- A content corpus of markdown archetypes with YAML frontmatter
- Two tools: `prep(intent, language)` for discovery, `consult(archetype, language)` for the full guidance document
- MVP languages: C#, Python, C, Go
- MIT licensed; open to contributions

## What it is not

- Not a static analyzer (false-positive generators don't help)
- Not an LLM (the server contains no model and performs no inference)
- Not a CWE-per-line ruleset (rules can't teach architecture)
- Not opinionated about how your LLM uses it (enforcement is downstream)

## Quick example

```json
// prep request
{
  "intent": "I'm about to write a class that handles user login and returns a session token",
  "language": "python"
}

// prep response — a list of relevant archetypes
{
  "matches": [
    { "archetype": "auth/password-hashing", "title": "Password Hashing", "score": 0.87 },
    { "archetype": "auth/session-tokens", "title": "Session Tokens", "score": 0.81 }
  ]
}
```

```json
// consult request
{ "archetype": "auth/password-hashing", "language": "python" }

// consult response — principles + Python guidance composed into one document
{
  "archetype": "auth/password-hashing",
  "language": "python",
  "content": "## Password Hashing — Principles\n\n...\n\n---\n\n## Password Hashing — Python\n\n...",
  "related_archetypes": ["auth/session-tokens"],
  "references": { "owasp_asvs": "V2.4", "cwe": "916" }
}
```

## Getting started

### Run locally

```bash
dotnet run --project src/GuardCode.Mcp
```

By default the server loads `./archetypes` relative to the executable. Override with:

```bash
GUARDCODE_ARCHETYPES_ROOT=/path/to/archetypes dotnet run --project src/GuardCode.Mcp
```

### Wire into Claude Desktop or Claude Code

Add an MCP server entry pointing at the compiled `guardcode-mcp` binary. See your client's MCP configuration docs; the exact shape varies by client.

## Repository layout

```
SecureCodingMcp.slnx
├── src/
│   ├── GuardCode.Mcp/        (executable, composition root, MCP tool handlers)
│   └── GuardCode.Content/    (domain, loading, indexing, services)
├── tests/
│   └── GuardCode.Content.Tests/
├── archetypes/               (the content corpus)
└── docs/superpowers/specs/2026-04-11-guardcode-design.md
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the archetype schema, content budgets, and how to add a new archetype or a new language file for an existing archetype. The content is what matters most — contributions that improve existing guidance are as valuable as contributions that add new archetypes.

## License

MIT. See [LICENSE](LICENSE).

GuardCode is open-sourced by Ehab Hussein and the GuardCode contributors. The goal is broad, friction-free adoption across the LLM ecosystem — Claude, GPT, Cursor, and anything else that speaks MCP.
```

Confirm the phrase `GUARD — Global Unified AI Rules for Development` appears on the second line exactly as written.

- [ ] **Step 2: Write `CONTRIBUTING.md`**

Create `CONTRIBUTING.md` at the repo root:

````markdown
# Contributing to GuardCode

GuardCode's value lives in its content. The server code is small, deterministic, and unlikely to need frequent changes; the corpus under `archetypes/` is where most PRs will land. This document explains the archetype schema, the budgets the validator enforces, and how to propose a new archetype or language file.

## Archetype anatomy

Each archetype lives at `archetypes/<category>/<name>/` and contains:

- `_principles.md` — the universal, language-agnostic portion (required).
- `<language>.md` — one per supported language (`csharp.md`, `python.md`, `c.md`, `go.md`).

An archetype ID is the path under `archetypes/`, using forward slashes: for example, `auth/password-hashing`. IDs must match `^[a-z0-9\-]+(/[a-z0-9\-]+)*$` — lowercase ASCII, hyphens allowed, no spaces or underscores.

## Principles file (`_principles.md`)

### Required frontmatter

```yaml
---
schema_version: 1
archetype: category/name            # must match the directory path
title: Human-readable title
summary: One-sentence description, ≤ 140 chars.
applies_to: [csharp, python, c, go] # subset of the MVP language set
keywords: [keyword, list, for, prep]
related_archetypes: [other/id]      # optional, one-way references
equivalents_in: {python: other/id}  # optional, language redirects
references:                          # optional, authoritative pointers
  owasp_asvs: V2.4
  cwe: "916"
---
```

### Required body sections

```markdown
## When this applies
## Architectural placement
## Principles
## Anti-patterns
## References
```

`Threat model` is optional. Add it when you have the domain knowledge to write it well.

## Language file (`<language>.md`)

### Required frontmatter

```yaml
---
schema_version: 1
archetype: category/name            # same as principles
language: python                    # must match the filename (python.md → python)
framework: null                     # optional bounded enum; null in MVP
principles_file: _principles.md
libraries:
  preferred: the-one-library
  acceptable: []
  avoid:
    - name: bad-lib
      reason: one-line reason
minimum_versions:
  python: "3.11"
---
```

### Required body sections

```markdown
## Library choice
## Reference implementation
## Language-specific gotchas
## Tests to write
```

### Budgets (enforced by the validator)

- **200 lines total per file**, including frontmatter. Overruns fail validation.
- **40 non-empty code lines in the reference implementation.** More is a teaching liability — if you can't show the shape in 40 lines, the archetype is doing too much.
- **Reference implementation is for shape, not copy-paste.** Mark it as such in prose.
- **Every avoided library carries a one-line reason.** No silent blacklists.

## Writing good guidance

The hardest part is writing advice that is genuinely useful and stays useful. A few rules we enforce in review:

1. **Principles are durable rules.** Write things that will still be true in ten years. "Use Argon2id" is a principle; "use argon2-cffi 23.1.0" belongs in the language file.
2. **Anti-patterns are prose, not code.** Showing a buggy code snippet tempts readers to cargo-cult around it. Describe the pattern in words.
3. **Library choices carry reasons.** "Use LibX" is not advice. "Use LibX because it gives you Y without Z" is.
4. **One reference implementation per language file.** The implementation is the shape of the solution, not a menu of alternatives. If you want to compare approaches, do it in prose.
5. **Tests section is prose, not test code.** Describe *what* properties matter and *why*. The reader will write the test in their framework of choice.

## How to propose a new archetype

1. Open an issue first if you're unsure whether the archetype is in scope — GuardCode targets function- or class-level guidance for backend and systems code.
2. Fork the repo and create a branch.
3. Add the directory, the principles file, and at least one language file.
4. Run the tests: `dotnet test SecureCodingMcp.slnx`. The content-corpus smoke test will catch most validation errors immediately.
5. Open a PR. Describe what gap the new archetype fills and which real-world failures it helps prevent.

## How to add a new language to an existing archetype

1. Add `<language>.md` to the archetype directory.
2. Update the principles file's `applies_to` array to include the new language.
3. Run the tests.
4. Open a PR.

You do not need to modify the principles file beyond the `applies_to` list — if you find yourself wanting to, the principles file may have been under-specified originally, and that is a conversation to have in the PR.

## Running the tests

```bash
dotnet restore SecureCodingMcp.slnx
dotnet build SecureCodingMcp.slnx
dotnet test SecureCodingMcp.slnx
```

All tests must pass before a PR will be merged.
````

- [ ] **Step 3: Verify both files exist and the GUARD phrase is present**

Read back the README and confirm `GUARD — Global Unified AI Rules for Development` appears near the top. Read CONTRIBUTING.md and confirm both section templates (principles and language file) are present.

- [ ] **Step 4: Commit**

```bash
git add README.md CONTRIBUTING.md
git commit -m "docs: add README and CONTRIBUTING

README introduces GuardCode, states the required GUARD expansion
verbatim per spec §1, contrasts what the server is with what it is
not, and shows a prep/consult example pair. CONTRIBUTING documents
the archetype schema, the validator budgets, and the workflow for
adding a new archetype or language file."
```

---

## Task 15: Final Verification

**Files:** none — this task runs the whole test suite and does a final smoke-start of the server.

- [ ] **Step 1: Run the full test suite from a clean build**

Run:
```bash
dotnet clean SecureCodingMcp.slnx
dotnet build SecureCodingMcp.slnx -c Debug
dotnet test SecureCodingMcp.slnx -c Debug
```
Expected: build succeeds with 0 warnings/errors, all tests pass. Total test count should be approximately: 5 (FrontmatterParser) + 4 (ArchetypeLoader) + 5 (ArchetypeValidator) + 3 (FileSystemArchetypeRepository) + 5 (KeywordArchetypeIndex) + 4 (PrepService) + 5 (ConsultationService) + 5 (ContentCorpusSmokeTests) = **36 tests**, all passing.

- [ ] **Step 2: Release-build smoke-start**

Run:
```bash
dotnet build SecureCodingMcp.slnx -c Release
dotnet run --project src/GuardCode.Mcp -c Release --no-build < /dev/null
```

Expected: the release build compiles clean, the server loads the archetype corpus on startup with no critical logs on stderr, and exits cleanly when stdin closes. If any archetype fails validation, fix it (do not downgrade validation) and re-run.

- [ ] **Step 3: Verify the git log is clean and linear**

Run: `git log --oneline`
Expected: the commits from Tasks 1–14 appear in order on top of the original `fc51dec` commit (scaffold + LICENSE + .gitignore + design spec). No merge commits. No "WIP" or "fixup" messages.

- [ ] **Step 4: No follow-up commit required** — if Steps 1 and 2 pass, the MVP is ready. Ask the user for authorization before pushing to origin.

---

## Self-Review

### Spec coverage

- §1 Overview / problem / solution → README.md (Task 14)
- §1 GUARD expansion requirement → README.md line 3, verbatim (Task 14)
- §1.4 Non-goals → README.md "What it is not" section (Task 14)
- §2.1 Archetype as category/name with ID regex → enforced in ConsultationService (Task 9) and FileSystemArchetypeRepository (Task 6)
- §2.2 Principles file → schema + body in Task 12, validated in Task 5
- §2.3 Language file + 200-line / 40-code-line budgets → Task 5 validator, Task 12 content
- §2.4 Consultation with two tools → Tasks 8, 9, 10
- §3.1 `prep` contract → Task 8 (service) + Task 10 (tool handler), max 8 results, 2000-char intent limit, framework accepted but unused
- §3.2 `consult` contract with three shapes (normal, redirect, not-found) → Task 9
- §3.2 `related_archetypes` includes reverse index → Task 7 (KeywordArchetypeIndex.GetReverseRelated) + Task 9 (merge in ConsultationService)
- §4.1 Principles frontmatter schema → PrinciplesFrontmatter (Task 2), parser (Task 3), validator required sections (Task 5)
- §4.2 Language frontmatter schema → LanguageFrontmatter (Task 2), parser, validator
- §4.3 Directory layout → repository walks archetypes/ root (Task 6)
- §5.1 Three-project solution → Task 1
- §5.2 Data flow (startup + per-call) → Task 11 (Program.cs) + Tasks 7–9
- §5.3 Key interfaces → Tasks 2, 6, 7, 8, 9 (all four interfaces)
- §5.4 Composition with generic host DI → Task 11
- §5.5 No I/O on request path → architectural invariant, not a test; comment in Task 11
- §6.1 Path traversal defense → Task 6 repository (full-path StartsWith check) + Task 9 archetype ID regex
- §6.2 Input validation at MCP boundary → Tasks 8, 9, 10
- §6.3 Strict YAML deserialization → Task 3
- §6.4 Read-only operation → Task 11 (no filesystem writes anywhere); analyzers enabled in Directory.Build.props (Task 1)
- §6.5 Log hygiene → Task 11 logs to stderr only
- §6.6 Content validation as security boundary → Task 5 (validator) + Task 11 (abort startup on failure)
- §7.1 MVP languages csharp/python/c/go → SupportedLanguage enum (Task 2)
- §7.2 10 MVP archetypes → 3 smoke-test archetypes in Task 12; remaining 7 deferred as content effort (flagged in Task 12 commit message and in this self-review)
- §7.3 Must-have archetypes errors/error-handling and io/input-validation → both present in Task 12
- §7.4 Release definition — LICENSE, README with GUARD phrase, CONTRIBUTING, tests green, valid content, MCP reachable → Tasks 12, 13, 14, 15
- §8 Contribution model → CONTRIBUTING.md in Task 14
- §9.1 Unit tests → Tasks 3–9
- §9.2 Integration tests → Task 13
- §9.3 What we do not test (quality, LLM behavior) → implicit; intentionally out of scope
- §9.4 TDD discipline → every production file in GuardCode.Content is written test-first (Tasks 3–9)
- §10 Out-of-scope items (SAST, embeddings, validate_code, other languages, hot reload, telemetry) → none of them appear in the plan
- §11 Implementation questions (versions, stopwords) → resolved in plan header and in Task 7 (stopword list)
- §12 Acceptance checklist → satisfied by Tasks 1–15

**Gap flagged:** the plan ships only 3 of the 10 MVP archetypes. This is intentional: content authoring is a separate effort that shouldn't block the server infrastructure. The release definition in §7.4 requires all 43 content files; §7.4 compliance is therefore *not* satisfied by this plan alone, and that's called out explicitly in Task 12's commit message and in the final user handoff.

### Placeholder scan

- No "TBD", "TODO", "implement later", or "fill in details" in task steps.
- Every code step shows the actual code.
- Every test step shows the actual test.
- Every command step shows the exact command.
- "Add appropriate error handling" does not appear — error handling is specified in code.

### Type consistency

- `Archetype` record signature is identical in Task 2 (definition) and Tasks 4, 6, 7, 9 (usages).
- `LanguageFile` record: `(LanguageFrontmatter Frontmatter, string Body)` — same everywhere.
- `IArchetypeRepository.LoadAll()` — synchronous, `IReadOnlyList<Archetype>` — consistent across Task 6 and Task 11.
- `IArchetypeIndex.Search(string, SupportedLanguage, int)` returns `IReadOnlyList<PrepMatch>` — consistent across Tasks 7 and 8.
- `IPrepService.Prep(string, SupportedLanguage, string?)` returns `PrepResult` — consistent across Tasks 8 and 10.
- `IConsultationService.Consult(string, SupportedLanguage)` returns `ConsultResult` — consistent across Tasks 9 and 10.
- `SupportedLanguageExtensions.TryParseWire` — same signature in Task 2 (definition) and Task 10 (usage).
- `KeywordArchetypeIndex.Build(IReadOnlyList<Archetype>)` — used as `Build(new[] { ... })` in tests (implicit conversion from array), consistent.

### Task granularity check

Every task step is a single action in the 2–5 minute range: one file written, one test added, one command run, one commit. No step batches multiple conceptual changes.

---

## Follow-ups (not in this plan)

These are deliberate omissions, flagged here so no one assumes they were forgotten:

1. **Remaining 7 MVP archetypes** (auth/session-tokens, auth/api-endpoint-authentication, io/file-path-handling, persistence/sql-queries, persistence/secrets-handling, crypto/random-numbers, memory/safe-string-handling). These are a content-authoring effort to track separately. The server infrastructure does not block on them.
2. **Cross-check between `applies_to` and language-file presence at load time.** Currently handled at runtime in `ConsultationService` with a distinctive not-found branch. Tightening to a startup-time validation is a one-liner in `ArchetypeValidator` but would be a content-breaking change until all existing archetypes are consistent.
3. **CI pipeline (GitHub Actions).** Not in this plan; add as a follow-up PR with a standard `dotnet test` workflow plus a job that runs `dotnet format --verify-no-changes`.
4. **MCP client integration doc.** The README currently says "see your client's MCP configuration docs"; once we have canonical Claude Desktop and Claude Code configurations, add them to the README.
5. **Push to `https://github.com/ehabhussein/GuardCode`.** Remote is already configured but nothing has been pushed. Do not push without explicit user authorization.

