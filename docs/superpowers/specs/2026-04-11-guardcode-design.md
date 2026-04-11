# GuardCode — Design Specification

**Project:** GuardCode
**Acronym:** GUARD — **Global Unified AI Rules for Development**
**Public repository:** https://github.com/ehabhussein/GuardCode
**Local working path:** `F:\repositories\SecureCodingMcp`
**License:** MIT
**Date:** 2026-04-11
**Status:** Draft — pending user review
**Author:** Ehab Hussein (with Claude as collaborator)

> **README requirement:** the phrase "GUARD — Global Unified AI Rules for Development" must appear verbatim near the top of the README when it is written. This expansion is fixed and is not to be substituted with any alternate wording.

---

## 1. Overview

### 1.1 The problem

LLMs generate code that works but defaults to the insecure and architecturally poor path. They:

- hash passwords with MD5 or SHA-256, or skip hashing entirely
- concatenate SQL strings
- inline authentication logic in route handlers
- mix HTTP, domain, and persistence concerns into 400-line god-functions
- treat error handling as an afterthought
- log secrets
- duplicate logic across handlers in violation of DRY
- ignore SOLID when structuring classes

This is not because LLMs are "unsafe." It is because their training data is overwhelmingly buggy Stack Overflow snippets and tutorial code, and they have no mechanism to consult authoritative guidance before writing a function. The "vibe coder" who asks an LLM to build an API does not know the code is insecure, and the LLM does not know either.

### 1.2 The solution

GuardCode is an open-source **Model Context Protocol (MCP) server** that acts as a **high-to-low level architecture consultant** any LLM can consult *before* writing a function or class. Given a free-text intent and a language, the server returns curated, human-authored engineering guidance — principles, architectural placement, anti-patterns, library choices, and gotchas — drawn from a community-maintained corpus.

GuardCode is **not** an LLM, not an agent, not a SAST tool, and not a ruleset. It is a deterministic content delivery server. Its intelligence lives in the *content*, which is written and reviewed by humans. The server's job is to retrieve the right piece of content for the right query.

### 1.3 Philosophy

GuardCode is built on three non-negotiable commitments:

1. **Holistic engineering guidance, not rule lookup.** We do not ship CWE-per-line checks. We ship function-/class-level architectural thinking — the kind a senior engineer would give when reviewing a junior's design. Security emerges from good architecture; it cannot be bolted on as a list of banned calls.
2. **The MCP embodies what it teaches.** The server itself is written in SOLID, DRY, secure C# — because any project that preaches these principles while shipping spaghetti has no moral authority. Every design decision in this spec is also a decision about what the server's own code looks like.
3. **Human-authored content, not model-generated.** The MCP never contains an LLM. It never generates text at runtime. All guidance is written, reviewed, and versioned by humans through a PR workflow — which is what gives it auditability, stability, and a credible path to OWASP-style community governance.

### 1.4 Non-goals

GuardCode explicitly does **not**:

- Run static analysis (Semgrep, CodeQL, Bandit) — static analyzers are false-positive generators at scale and duplicate existing tooling.
- Validate or grade submitted code (no `validate_code` tool). The MCP is prescriptive, not reactive.
- Contain an LLM or embedding model. No vectors, no RAG, no hidden inference.
- Target frontend developers or JavaScript/Java ecosystems in MVP. The MVP targets backend and systems engineers (C#, Python, C, Go).
- Enforce anything. LLMs are free to ignore GuardCode's output. Enforcement is a downstream concern (editor integrations, DeceptiCode-style review agents, CI).
- Offer a GUI, web dashboard, or REST API in MVP. Stdio MCP only.
- Ship compliance auto-mappings (PCI/HIPAA/SOC2) in MVP. Those are phase 2.

---

## 2. Core concepts

### 2.1 Archetype

An **archetype** is one unit of guidance — the smallest self-contained answer to the question "I am about to write a function or class that does X. How should I approach it?" Examples: `auth/password-hashing`, `io/input-validation`, `errors/error-handling`, `memory/safe-string-handling`.

Each archetype has a canonical identifier of the form `category/name`, matching the regex `^[a-z0-9\-]+(/[a-z0-9\-]+)*$`. The identifier is also the directory name on disk.

### 2.2 Principles file

Every archetype has exactly one `_principles.md` file in its directory. This file contains the **universal, language-agnostic** portion of the guidance:

- When the archetype applies
- Architectural placement (where this belongs in a well-designed system)
- Principles (durable rules that are still true in 10 years)
- Anti-patterns (described in prose, never in code)
- References to authoritative sources (OWASP, CWE, NIST)

The principles file is the *why*. Contributors who update the Python file do not need to touch the principles; contributors who refresh the principles do not need to retest every language implementation.

### 2.3 Language file

Each archetype has zero or more language files — `python.md`, `csharp.md`, `c.md`, `go.md` — containing the **language-specific implementation guidance**:

- Library choice (preferred, acceptable, avoid — with one-line reasons)
- A single compact reference implementation (≤ 40 lines of code), grounded in real syntax, explicitly marked as "for shape, not for copy-paste"
- Language-specific gotchas (ecosystem pitfalls, standard library traps)
- Tests to write (described in prose — what properties matter, not test code)

Language files are small and focused — **hard upper bound of 200 lines** per file, including frontmatter. If a language file grows past that, it gets split (for example, `python.md` + `python-django.md`).

### 2.4 Consultation

The LLM calls the MCP before writing a function or class. This is called a **consultation**. Two tools drive it: `prep` and `consult`.

- `prep(intent, language)` takes a natural-language description of what the LLM is about to write and returns a short list of relevant archetype identifiers.
- `consult(archetype, language)` takes one of those identifiers and returns the composed guidance document (principles + language file, concatenated with a separator).

The LLM is the only agent in the loop. It decides when to call `prep`, which archetypes from the returned list to drill into, and how to apply the guidance to the user's request. GuardCode never reasons. It retrieves.

---

## 3. MCP tool contract

### 3.1 `prep`

**Purpose:** discover which archetypes are relevant to an upcoming task.

**Input:**

```json
{
  "intent": "I'm about to write a class that handles user login — takes username and password, verifies against a database, and returns a session token",
  "language": "python",
  "framework": "flask"
}
```

- `intent`: free-text, required. Max 2000 characters. The LLM describes what it is about to write.
- `language`: required. One of `csharp | python | c | go` in MVP.
- `framework`: optional. One of a bounded enum per language, or omitted.

**Output:**

```json
{
  "matches": [
    {
      "archetype": "auth/password-hashing",
      "title": "Password Hashing",
      "summary": "Storing, verifying, and handling user passwords in any backend.",
      "score": 0.87
    },
    {
      "archetype": "auth/session-tokens",
      "title": "Session Tokens",
      "summary": "Choosing, issuing, and verifying session tokens for web backends.",
      "score": 0.81
    },
    {
      "archetype": "auth/api-endpoint-authentication",
      "title": "API Endpoint Authentication",
      "summary": "Wiring authentication into HTTP route handlers with clean separation of concerns.",
      "score": 0.74
    },
    {
      "archetype": "io/input-validation",
      "title": "Input Validation",
      "summary": "Validating untrusted input at trust boundaries.",
      "score": 0.58
    }
  ]
}
```

- Maximum 8 matches per call.
- Archetypes whose `applies_to` does not include the requested `language` are filtered out. Zero results is a valid response — the LLM interprets an empty `matches` list as "no guidance available for this language and intent," which is truthful. Redirects live in `consult`, not `prep`, so there is exactly one code path per concept.
- Scoring is deterministic keyword-match — same query produces the same result.
- **MVP behavior of `framework`:** the field is accepted and validated against the per-language enum, but is not used for result filtering in MVP, because none of the 10 MVP archetypes specify a framework. The field is reserved for future framework-specific content (see §7 and §10). Clients may pass it today and it is forward-compatible.

### 3.2 `consult`

**Purpose:** retrieve the full guidance document for one archetype.

**Input:**

```json
{
  "archetype": "auth/password-hashing",
  "language": "python"
}
```

- `archetype`: required, must match the archetype ID regex.
- `language`: required.

**Output:**

```json
{
  "archetype": "auth/password-hashing",
  "language": "python",
  "content": "## Password Hashing — Principles\n\n...\n\n---\n\n## Password Hashing — Python\n\n...",
  "related_archetypes": [
    "auth/session-tokens",
    "auth/login-endpoint",
    "persistence/user-repo"
  ],
  "references": {
    "owasp_asvs": "V2.4",
    "owasp_cheatsheet": "Password Storage Cheat Sheet",
    "cwe": 916,
    "nist_ssdf": "PW.6.1"
  }
}
```

- `content` is the principles file body concatenated with the language file body, separated by `\n\n---\n\n`. Frontmatter is stripped from both before concatenation.
- If the requested language is not in the archetype's `applies_to`, the server returns a graceful redirect:

```json
{
  "archetype": "memory/safe-string-handling",
  "language": "python",
  "redirect": true,
  "message": "This archetype applies to C. In Python, string handling is memory-safe at the runtime level and this archetype is unnecessary. Consider `io/input-validation` for trust-boundary validation instead.",
  "suggested": ["io/input-validation"]
}
```

- `related_archetypes` combines the archetype's own `related_archetypes` list with the reverse index computed at startup, so the relationship is always bidirectional from the LLM's point of view.

### 3.3 Auxiliary tools

**MVP: none.** Two tools cover the entire contract surface. Additional tools like `list_archetypes`, `get_schema`, or `health` may be added later but are explicitly out of MVP scope.

---

## 4. Content schema

### 4.1 Principles file

Location: `archetypes/<category>/<name>/_principles.md`

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
related_archetypes:
  - auth/session-tokens
  - auth/login-endpoint
  - persistence/user-repo
equivalents_in:
  c: crypto/key-derivation
references:
  owasp_asvs: V2.4
  owasp_cheatsheet: Password Storage Cheat Sheet
  cwe: 916
  nist_ssdf: PW.6.1
---

# Password Hashing — Principles

## When this applies
...

## Architectural placement
...

## Principles
...

## Anti-patterns
...

## Threat model
*(optional section — add when you have the domain knowledge to write it well)*

## References
...
```

**Frontmatter fields:**

| Field | Required | Type | Notes |
|---|---|---|---|
| `schema_version` | yes | int | Currently `1`. Permission slip for future schema evolution. |
| `archetype` | yes | string | Must match the directory path. |
| `title` | yes | string | Human-readable display name. |
| `summary` | yes | string | One sentence, ≤ 140 chars, used by `prep` matching and result display. |
| `applies_to` | yes | array of strings | Subset of `[csharp, python, c, go]`. Drives `prep` filtering and `consult` redirects. |
| `keywords` | yes | array of strings | Used by `prep` keyword matching. Aim for 5–15 keywords, each lowercase. |
| `related_archetypes` | no | array of archetype ids | One-way references. Server computes reverse index at startup. |
| `equivalents_in` | no | map<lang, archetype id> | For unsupported-language redirects. |
| `references` | no | map<string, string | int> | Named pointers into authoritative sources. |

**Required body sections:** `When this applies`, `Architectural placement`, `Principles`, `Anti-patterns`, `References`.
**Optional body sections:** `Threat model`.

**Validation:** the content loader rejects any principles file missing a required section or required frontmatter field at startup, with a clear error message pointing to the offending file. This is a hard fail — the server does not start until content is valid.

### 4.2 Language file

Location: `archetypes/<category>/<name>/<language>.md`

```markdown
---
schema_version: 1
archetype: auth/password-hashing
language: python
framework: null
principles_file: _principles.md
libraries:
  preferred: argon2-cffi
  acceptable: []
  avoid:
    - name: hashlib
      reason: Fast hashes are not password hashes. Designed for a different purpose.
    - name: bcrypt
      reason: Outdated unless you specifically need bcrypt hash interop.
minimum_versions:
  python: "3.10"
---

# Password Hashing — Python

> Principles auto-prepended by the MCP. This file covers Python-specific
> implementation only.

## Library choice
...

## Reference implementation
*(≤ 40 lines of code, for shape not copy-paste)*

```python
...
```

## Language-specific gotchas
...

## Tests to write
*(prose description of what to test and why — not test code)*
...
```

**Frontmatter fields:**

| Field | Required | Type | Notes |
|---|---|---|---|
| `schema_version` | yes | int | Must match the principles file. |
| `archetype` | yes | string | Must match parent directory. |
| `language` | yes | string | One of `csharp | python | c | go` in MVP. |
| `framework` | no | string or null | Bounded enum per language. |
| `principles_file` | yes | string | Always `_principles.md` in MVP. Future-proofing for cross-archetype principle reuse. |
| `libraries.preferred` | yes | string | The one library the LLM should default to. |
| `libraries.acceptable` | no | array of strings | Runner-up libraries with valid use cases. |
| `libraries.avoid` | no | array of `{name, reason}` | Each avoided library MUST carry a one-line reason. |
| `minimum_versions` | no | map<string, string> | Language/runtime minimums. |

**Required body sections:** `Library choice`, `Reference implementation`, `Language-specific gotchas`, `Tests to write`.

**Budget:** 200 lines total per file (including frontmatter). Reference implementation ≤ 40 lines of code. Files exceeding the budget fail validation.

### 4.3 Directory layout

```
archetypes/
├── auth/
│   ├── password-hashing/
│   │   ├── _principles.md
│   │   ├── csharp.md
│   │   ├── python.md
│   │   └── go.md
│   ├── session-tokens/
│   │   ├── _principles.md
│   │   ├── csharp.md
│   │   ├── python.md
│   │   └── go.md
│   └── ...
├── io/
│   ├── input-validation/
│   └── file-path-handling/
├── persistence/
├── crypto/
├── errors/
└── memory/
    └── safe-string-handling/
        ├── _principles.md
        └── c.md
```

Categories are flat (one level deep). Archetype names are flat under each category. No arbitrary nesting.

---

## 5. Architecture

### 5.1 Solution layout

```
SecureCodingMcp.sln
├── src/
│   ├── GuardCode.Mcp/                (executable, composition root)
│   │   ├── Program.cs
│   │   ├── Tools/
│   │   │   ├── PrepTool.cs
│   │   │   └── ConsultTool.cs
│   │   └── appsettings.json
│   └── GuardCode.Content/            (class library, content domain)
│       ├── Archetype.cs
│       ├── PrinciplesFrontmatter.cs
│       ├── LanguageFrontmatter.cs
│       ├── Loading/
│       │   ├── IArchetypeRepository.cs
│       │   ├── FileSystemArchetypeRepository.cs
│       │   ├── ArchetypeLoader.cs
│       │   └── FrontmatterParser.cs
│       ├── Indexing/
│       │   ├── IArchetypeIndex.cs
│       │   └── KeywordArchetypeIndex.cs
│       ├── Services/
│       │   ├── IPrepService.cs
│       │   ├── PrepService.cs
│       │   ├── IConsultationService.cs
│       │   └── ConsultationService.cs
│       └── Validation/
│           ├── ArchetypeValidator.cs
│           └── ArchetypeValidationException.cs
├── tests/
│   └── GuardCode.Content.Tests/      (xUnit + FluentAssertions)
│       ├── FrontmatterParserTests.cs
│       ├── ArchetypeLoaderTests.cs
│       ├── KeywordArchetypeIndexTests.cs
│       ├── PrepServiceTests.cs
│       ├── ConsultationServiceTests.cs
│       └── ArchetypeValidatorTests.cs
└── archetypes/                        (the content corpus, plain markdown)
    └── ...
```

Three projects. Server, content library, tests. The `archetypes/` directory is a sibling folder — it is data, not a C# project, and it ships alongside the binary.

### 5.2 Data flow

**Startup:**

1. `Program.cs` builds a generic host, registers services, calls `AddMcpServer()`.
2. `FileSystemArchetypeRepository` walks `archetypes/`, reads every `.md` file.
3. `FrontmatterParser` extracts YAML frontmatter from each file using `YamlDotNet` with a strict, typed deserializer.
4. `ArchetypeLoader` groups files by directory into `Archetype` aggregates (one principles file + zero or more language files per directory).
5. `ArchetypeValidator` runs structural and schema validation on every archetype. Any failure aborts startup with a descriptive error.
6. `KeywordArchetypeIndex` builds:
   - An inverted index: `keyword → set<archetype id>`.
   - A reverse `related_archetypes` index.
7. The host enters the MCP event loop on stdio.

**Per `prep` call:**

1. Tokenize `intent` (lowercase, split on whitespace and punctuation, strip stopwords from a small static list).
2. For each token, look up matching archetype IDs in the inverted index.
3. Score each candidate by `(token matches × 1.0) + (summary/title substring match × 0.5)`.
4. Filter out archetypes whose `applies_to` does not include the requested language. If this leaves zero results, fall back to returning redirects via `equivalents_in`.
5. Sort descending, return top 8.

**Per `consult` call:**

1. Validate archetype ID against the regex.
2. Look up the archetype in the in-memory store.
3. If not found, return a structured "not found" error.
4. If the requested language is not in `applies_to`, return a redirect response (see §3.2).
5. Otherwise, concatenate the stripped principles body + separator + stripped language file body, return with metadata.

### 5.3 Key interfaces

```csharp
// Content domain
public sealed record Archetype(
    string Id,
    PrinciplesFrontmatter Principles,
    string PrinciplesBody,
    IReadOnlyDictionary<string, LanguageFile> LanguageFiles);

public sealed record LanguageFile(
    LanguageFrontmatter Frontmatter,
    string Body);

// Loading
public interface IArchetypeRepository
{
    // Synchronous by design: content loading is a one-shot startup operation,
    // executed on the composition-root thread before the MCP event loop starts.
    // Async here would buy nothing and would force `.GetAwaiter().GetResult()`
    // at the call site, which we want to avoid.
    IReadOnlyList<Archetype> LoadAll();
}

// Indexing
public interface IArchetypeIndex
{
    IReadOnlyList<PrepMatch> Search(string intent, SupportedLanguage language, int maxResults);
    Archetype? Get(string archetypeId);
    IReadOnlyList<string> GetReverseRelated(string archetypeId);
}

// Services
public interface IPrepService
{
    PrepResult Prep(string intent, SupportedLanguage language, string? framework);
}

public interface IConsultationService
{
    ConsultResult Consult(string archetypeId, SupportedLanguage language);
}
```

Each interface has exactly one production implementation in MVP. Each interface also has at least one test double. The interfaces exist to enable testing in isolation and to make future swaps (e.g., in-memory repository for tests, embeddings-based index for phase 2) cheap — not to enable variation that will never happen.

### 5.4 Composition

`Program.cs` wires everything with the built-in .NET generic host DI:

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddSingleton<IArchetypeRepository, FileSystemArchetypeRepository>()
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

await builder.Build().RunAsync();
```

No Autofac, no Scrutor, no module scanning, no abstract factories. Services are `sealed class` with primary constructors where possible. The content repository, the index, and the two services are all singletons because content is loaded once at startup and never mutates.

### 5.5 Optimization

All optimization is implicit in the architecture:

- **No I/O on the request path.** Every `prep` and `consult` call is a pure in-memory operation after startup.
- **Keyword index is a precomputed `Dictionary<string, HashSet<string>>`.** Lookups are O(1) per token.
- **Reverse `related_archetypes` index is computed once at startup.**
- **Content bodies are stored as strings, not re-read from disk on each consult.**
- **No allocations per request beyond the response object itself.** Token lists, match lists, and scoring tables are stack-friendly.

A request-path profile target is < 1 ms wall-clock for `consult` and < 5 ms for `prep`, excluding MCP protocol overhead. This is achievable by construction with the current design.

---

## 6. Security posture

GuardCode's threat model is unusual: the server ships with a community-authored content corpus, processes LLM-generated requests, and is often run on developer laptops or CI. The risks are:

1. **Malicious or malformed content files** (PR from a bad actor, corrupted file, injected YAML).
2. **Path traversal in archetype IDs** (LLM or client sends `../../etc/passwd`).
3. **Resource exhaustion via crafted requests** (oversized intent strings, deeply nested YAML).
4. **Information disclosure in error messages** (leaking absolute paths, internal state).

Defenses, all first-class in the design:

### 6.1 Path traversal defense

Archetype IDs are validated with the regex `^[a-z0-9\-]+(/[a-z0-9\-]+)*$` at the MCP tool boundary. The regex forbids `..`, backslashes, absolute paths, and any non-lowercase-ASCII content.

Additionally, once a filesystem path is resolved from an archetype ID during startup, the resolved absolute path is verified to be strictly under the archetypes root using `Path.GetFullPath` followed by a `StartsWith` check. Any file outside the root is rejected with a validation error.

### 6.2 Input validation at the MCP tool boundary

- `intent`: required, ≤ 2000 characters. Strings longer than this are rejected with a structured error.
- `language`: required, must parse to the `SupportedLanguage` enum (`csharp | python | c | go`).
- `framework`: optional, must be null or match a per-language bounded enum.
- `archetype`: required for `consult`, must match the archetype ID regex.

Invalid input produces a structured MCP error, never an exception trace.

### 6.3 Strict YAML deserialization

`YamlDotNet` is configured with:
- A statically typed deserializer (`DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)`).
- `IgnoreUnmatchedProperties()` disabled — unknown fields in frontmatter are a validation failure, not a silent accept.
- No tag mappings. No polymorphism. No dynamic type resolution.

This prevents YAML deserialization exploits by construction — the deserializer can only populate known C# record types with known primitive fields.

### 6.4 Read-only operation

The server performs no filesystem writes, no shell invocations, no network calls, no process spawns, and no eval of any kind. The .NET project should be built with analyzers (`Microsoft.CodeAnalysis.NetAnalyzers`) configured to fail on `CA3075` (XML/XXE), `CA5369` (unsafe deserialization), and the file/process/network-related CA rules, to make this enforceable rather than aspirational.

### 6.5 Log hygiene

Structured logging via `Microsoft.Extensions.Logging`. Logs include archetype IDs, match counts, and latencies. Logs never include:
- Request body contents in full
- Absolute filesystem paths (content root is configurable and anonymized in logs)
- Stack traces in non-debug builds

### 6.6 Content validation as a security boundary

Because the content corpus is open to PR contributions, the loader treats content files as untrusted input. Every file is parsed, validated, and rejected if it fails any of:
- Required frontmatter fields missing
- Body sections missing
- File exceeds the 200-line budget
- Reference implementation exceeds the 40-line code budget
- Archetype ID mismatch between frontmatter and directory path
- YAML contains unknown fields or incorrect types

Validation errors abort startup. The server does not run with invalid content.

---

## 7. MVP scope

### 7.1 Languages

**`csharp`, `python`, `c`, `go`.** Deliberately not JavaScript, not Java. GuardCode targets backend and systems engineers.

### 7.2 Archetypes

10 archetypes for first release:

| # | Archetype | Category | `applies_to` |
|---|---|---|---|
| 1 | `auth/password-hashing` | Auth & identity | csharp, python, go |
| 2 | `auth/session-tokens` | Auth & identity | csharp, python, go |
| 3 | `auth/api-endpoint-authentication` | Auth & identity | csharp, python, go |
| 4 | `io/input-validation` | I/O & trust boundaries | csharp, python, c, go |
| 5 | `io/file-path-handling` | I/O & trust boundaries | csharp, python, c, go |
| 6 | `persistence/sql-queries` | Persistence | csharp, python, go |
| 7 | `persistence/secrets-handling` | Persistence | csharp, python, c, go |
| 8 | `crypto/random-numbers` | Cryptography | csharp, python, c, go |
| 9 | `errors/error-handling` | Errors & reliability | csharp, python, c, go |
| 10 | `memory/safe-string-handling` | Memory safety | c |

Content file count: 10 principles files + 33 language files = **43 markdown files at MVP ship.**

### 7.3 Must-have archetypes

**`errors/error-handling` and `io/input-validation` are non-negotiable for MVP.** They are the archetypes that demonstrate the value of the principles+implementation split across divergent language idioms. If either is cut, the MVP does not prove the model.

### 7.4 Release definition

The MVP is releasable when:

1. All 43 content files exist, pass validation, and have been reviewed by the author.
2. `prep` and `consult` tools are implemented and pass integration tests.
3. The content loader rejects malformed files with clear error messages.
4. The MCP server runs successfully against Claude Desktop and Claude Code with both tools callable.
5. A README exists explaining GuardCode, containing the phrase "GUARD — Global Unified AI Rules for Development" verbatim.
6. Contribution guidelines (CONTRIBUTING.md) explain the archetype schema and how to add a new archetype or language file.
7. A `LICENSE` file exists at the repo root with the standard MIT license text, attributed to Ehab Hussein and the GuardCode contributors.

---

## 8. Contribution model

GuardCode is open source from day one. The contribution workflow is:

1. **New archetype:** a contributor opens a PR adding a new directory under `archetypes/`, with at least `_principles.md` and one language file. The `_principles.md` must pass schema validation. The PR is reviewed for quality (principles are sound, anti-patterns are real, references are authoritative) and merged.

2. **New language for an existing archetype:** a contributor adds `<language>.md` to an existing archetype directory. They do not touch the principles file. Review focuses on language-specific accuracy.

3. **Updating principles:** a contributor updates `_principles.md`. This triggers a review of all existing language files in that archetype to ensure they remain consistent with the updated principles. CI flags this as a cross-file review.

4. **Governance:** target state is an OWASP-hosted project with a core maintainer group and domain leads per language. Initial state is single-maintainer (Ehab) with community contributions. The governance model is explicitly out of MVP scope but should be kept in mind when designing contribution tooling.

---

## 9. Testing strategy

### 9.1 Unit tests (`GuardCode.Content.Tests`)

- **`FrontmatterParserTests`:** valid frontmatter parses to typed records; malformed YAML rejected; unknown fields rejected; missing required fields rejected.
- **`ArchetypeLoaderTests`:** directory tree correctly grouped into archetype aggregates; mismatched archetype IDs rejected; orphaned language files (no principles) rejected.
- **`ArchetypeValidatorTests`:** each required section/field violation produces a specific error; valid content passes; line-budget violations rejected; reference-implementation code-size violations rejected.
- **`KeywordArchetypeIndexTests`:** inverted index correctness; `applies_to` filtering; reverse `related_archetypes` index correctness.
- **`PrepServiceTests`:** intent tokenization; scoring; language filtering; redirect fallbacks; max-result truncation.
- **`ConsultationServiceTests`:** principles + language composition; redirect responses for unsupported languages; archetype-not-found handling.

### 9.2 Integration tests

- **Content corpus smoke test:** load the real `archetypes/` directory, assert all 10 MVP archetypes load without validation errors. This test runs in CI and is the first line of defense against broken content.
- **End-to-end MCP calls:** spawn the server in-process, issue `prep` and `consult` tool calls via the MCP client library, assert shapes and contents.

### 9.3 What we do *not* test

- The *quality* of the guidance content. Quality is a human review concern, not an automated test concern. CI validates *structure* (schema, budgets, required sections); humans validate *correctness* (is this actually good advice).
- LLM behavior on the other side of the MCP. That is downstream.

### 9.4 TDD discipline

The implementation plan will be written assuming test-first development: for every service and validator, tests come before implementation. The loader, index, and services are all testable in isolation because of the interface boundaries in §5.3.

---

## 10. Out of scope (explicitly)

These are ideas that came up during brainstorming and are deliberately **not** in MVP. They are captured here so future contributors know they were considered:

- **Static analysis integration (Semgrep/CodeQL/Bandit).** False positive generators at scale; would recreate existing tooling without differentiation.
- **Embedding-based retrieval for `prep`.** Adds a model dependency and non-determinism. Plain keyword match is sufficient for a few hundred archetypes; if retrieval quality degrades at scale, revisit behind `IArchetypeIndex`.
- **`validate_code` tool.** GuardCode is prescriptive, not reactive. The MCP tells the LLM how to build things, not how to grade what it already built.
- **JavaScript, Java, Ruby, PHP, Rust, Swift, Kotlin, TypeScript.** The MVP language set is intentionally narrow. New languages are phase 2 contributions from language experts.
- **Frameworks as a separate axis.** `framework` is an optional frontmatter field; framework-specific archetype files (`python-django.md`) are allowed when truly necessary but not required in MVP.
- **Compliance auto-mappings (PCI, HIPAA, SOC2, NIST 800-53).** Phase 2. MVP references authoritative sources but does not claim compliance mappings.
- **Web dashboard, REST API, GraphQL API, WebSocket API.** MCP stdio only. Anything else is a different project.
- **Runtime content hot-reload.** Content is loaded at startup. Adding a file requires a server restart. This is fine for the stdio process model.
- **Metrics, telemetry, usage reporting.** Privacy first. No telemetry in MVP.
- **Authentication on the MCP server.** Stdio transport has no network surface; auth is not meaningful. If a network transport is added later, auth becomes in-scope.

---

## 11. Open questions deferred to the implementation plan

These are implementation-level questions that do not belong in the design spec but must be answered before writing code:

- **Which `YamlDotNet` version and deserializer configuration exactly?** Pinned in the implementation plan.
- **Which `ModelContextProtocol` SDK version?** Pinned in the implementation plan.
- **CI pipeline choice:** GitHub Actions vs alternatives. GitHub Actions is the default unless there's a reason otherwise.
- **Exact stopword list for `prep` tokenization.** A small static English list (≤ 50 words) is sufficient. Defined in the implementation plan.
- **Test framework version pins.** xUnit, FluentAssertions, specific versions pinned in the implementation plan.

---

## 12. Acceptance checklist

Before this design is considered implementable, verify:

- [ ] All non-goals are explicit and uncontroversial.
- [ ] The MCP tool contract (§3) is complete enough to implement against.
- [ ] The content schema (§4) has no ambiguity about required vs optional fields.
- [ ] The architecture (§5) has no missing interfaces or dangling references.
- [ ] The security posture (§6) addresses the four listed threats with concrete defenses.
- [ ] MVP scope (§7) matches the author's intent: 4 languages, 10 archetypes, 43 files.
- [ ] The "GUARD — Global Unified AI Rules for Development" expansion is called out for the README (§1, top of document).
- [ ] The implementation embodies the principles it teaches: SOLID, DRY, security-by-design, not over-engineered.

---

## 13. Next step

Once this design is approved, invoke the `superpowers:writing-plans` skill to produce a detailed, test-driven implementation plan broken into independently-committable tasks.
