# GuardCode

**GUARD — Global Unified AI Rules for Development**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/Model_Context_Protocol-1.2.0-blue)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)

GuardCode is an open-source [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server that gives any LLM a high-to-low-level engineering consultant it can call **before** writing a function or class. It ships human-authored guidance — principles, architectural placement, anti-patterns, library choices, and language-specific gotchas — organized into focused **archetypes** that the LLM retrieves through two deterministic tools: `prep` and `consult`.

The intelligence lives in the content, not the server. Everything GuardCode knows is written by humans, reviewed through pull requests, and validated at load time. There is no model and no inference inside the server — just a fast index over a corpus of markdown.

---

## Table of contents

- [Why GuardCode](#why-guardcode)
- [How it works](#how-it-works)
- [The two tools](#the-two-tools)
- [Installation](#installation)
- [Running the server](#running-the-server)
- [Wiring into an MCP client](#wiring-into-an-mcp-client)
- [End-to-end example](#end-to-end-example)
- [What ships in the MVP corpus](#what-ships-in-the-mvp-corpus)
- [Architecture](#architecture)
- [Configuration reference](#configuration-reference)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Security](#security)
- [License](#license)

---

## Why GuardCode

LLMs generate code that runs but defaults to the insecure and architecturally poor path: MD5-hashed passwords, SQL via string concatenation, god-functions that mix HTTP and persistence concerns, inconsistent error handling, "catch `Exception` and log" as a pattern. The fix is not to bolt more static-analysis checks onto already-generated code. Static analyzers reward after-the-fact fault-finding; they do not teach design.

The fix is to give the LLM a place to consult *before* it writes the function. That is what GuardCode does. You ask `prep("I'm about to write a class that handles login", "python")` and GuardCode returns a ranked list of archetypes that apply. You then ask `consult("auth/password-hashing", "python")` and get a single composed markdown document: principles, architectural placement, reference implementation, language-specific gotchas, and tests to write. The LLM reads it and writes better code on the first try.

GuardCode is **not**:

- A static analyzer or SAST wrapper — false-positive generators do not help.
- An LLM — the server contains no model and performs no inference.
- A CWE-per-line ruleset — rules cannot teach architecture.
- Opinionated about how your LLM uses the guidance — enforcement is downstream.

## How it works

```
  LLM ──► prep(intent, language)  ──►  ranked archetype matches
  LLM ──► consult(archetype, lang) ──►  principles + language file composed into one markdown document
```

- **Archetypes** are directories under `archetypes/` containing a `_principles.md` (language-agnostic) and one markdown file per supported language. Each archetype covers one focused topic: password hashing, input validation, error handling, etc.
- **Indexing** happens once at startup. The full corpus is loaded, validated strictly (YAML frontmatter schema, body-length budgets, reference-implementation size caps, orphan-reference detection), and frozen into an immutable in-memory index. If anything fails validation the server refuses to start and writes a diagnostic to stderr.
- **`prep`** scores archetypes against the LLM's natural-language intent using keyword matching and returns up to 8 candidates, highest-scoring first. No network, no model, fully deterministic.
- **`consult`** composes the principles file with the language file into one markdown payload. If the archetype doesn't cover the requested language, it returns a redirect with a suggested alternative when one exists.

## The two tools

### `prep(intent, language, framework?)`

Call this **before** writing any non-trivial code. Pass a free-text description of what you are about to build and the target language. Returns up to eight ranked archetypes the LLM should consider consulting.

| Parameter   | Type     | Required | Description                                                    |
|-------------|----------|----------|----------------------------------------------------------------|
| `intent`    | string   | yes      | Free-text description of what you're about to write. ≤ 2000 chars. |
| `language`  | string   | yes      | One of: `csharp`, `python`, `c`, `go`.                          |
| `framework` | string   | no       | Optional framework hint. Accepted for forward compatibility; not used for filtering in the MVP. |

Response shape:

```json
{
  "matches": [
    {
      "archetype": "auth/password-hashing",
      "title": "Password Hashing",
      "summary": "Storing, verifying, and handling user passwords in any backend.",
      "score": 4.0
    }
  ],
  "error": null
}
```

### `consult(archetype, language)`

Fetch the full guidance document for one archetype.

| Parameter   | Type   | Required | Description                                             |
|-------------|--------|----------|---------------------------------------------------------|
| `archetype` | string | yes      | Archetype identifier, e.g. `auth/password-hashing`.     |
| `language`  | string | yes      | One of: `csharp`, `python`, `c`, `go`.                   |

Response shape:

```json
{
  "archetype": "auth/password-hashing",
  "language": "python",
  "content": "# Password Hashing — Principles\n...\n---\n# Password Hashing — Python\n...",
  "redirect": false,
  "notFound": false,
  "message": null,
  "suggested": [],
  "relatedArchetypes": ["auth/session-tokens"],
  "references": {
    "owasp_asvs": "V2.4",
    "owasp_cheatsheet": "Password Storage Cheat Sheet",
    "cwe": "916"
  },
  "error": null
}
```

If the archetype doesn't cover the requested language, `content` is null, `redirect` is `true`, `message` explains why, and `suggested` lists sibling archetypes that do apply.

---

## Installation

### Prerequisites

- **.NET 10 SDK** or later — download from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download).
- **Git**.
- An MCP-aware client (Claude Desktop, Claude Code, Cursor, or anything that speaks MCP stdio).

### Build from source

```bash
git clone https://github.com/ehabhussein/GuardCode.git
cd GuardCode
dotnet build -c Release
```

After a successful build the MCP server binary is at:

```
src/GuardCode.Mcp/bin/Release/net10.0/guardcode-mcp.exe   (Windows)
src/GuardCode.Mcp/bin/Release/net10.0/guardcode-mcp       (Linux / macOS)
```

The archetype corpus is copied into the output directory next to the binary, so the compiled server is self-contained and can be copied anywhere on disk.

### Run the tests

```bash
dotnet test -c Release
```

The test suite exercises the loader, validator, indexer, and both services against the real corpus. All tests should pass before you wire GuardCode into a client.

## Running the server

GuardCode speaks MCP over stdio, which means you almost never launch it by hand — the MCP client spawns it as a subprocess. But you can sanity-check the startup path:

```bash
dotnet run --project src/GuardCode.Mcp
```

On success the server loads the corpus, binds stdio, and waits silently for MCP protocol frames. Everything written to **stdout** is MCP wire format; all logs go to **stderr**. If the corpus fails to load you will see a diagnostic on stderr and the process exits with code 1. Press `Ctrl+C` to stop.

## Wiring into an MCP client

### Claude Desktop

Edit `claude_desktop_config.json` (the exact location depends on your OS — see [Anthropic's MCP quickstart](https://modelcontextprotocol.io/quickstart/user)) and add a `guardcode` entry under `mcpServers`:

```json
{
  "mcpServers": {
    "guardcode": {
      "command": "C:\\path\\to\\GuardCode\\src\\GuardCode.Mcp\\bin\\Release\\net10.0\\guardcode-mcp.exe"
    }
  }
}
```

Linux / macOS:

```json
{
  "mcpServers": {
    "guardcode": {
      "command": "/home/you/GuardCode/src/GuardCode.Mcp/bin/Release/net10.0/guardcode-mcp"
    }
  }
}
```

Restart Claude Desktop. Two tools named `prep` and `consult` should appear in the MCP tools list.

### Claude Code

Claude Code uses the same MCP server model. Add GuardCode via the CLI:

```bash
claude mcp add guardcode /absolute/path/to/guardcode-mcp
```

Or edit your user-level `mcp.json` directly with the same shape as the Claude Desktop example.

### Other MCP clients

Any client that launches MCP servers as stdio subprocesses works the same way: point it at the `guardcode-mcp` binary, no arguments needed. GuardCode does not listen on sockets, does not require a config file at runtime beyond `appsettings.json` which is copied next to the binary, and does not phone home.

---

## End-to-end example

Here is a complete interaction using an archetype that actually ships in the MVP corpus. Assume the LLM is about to write a Python login handler.

**Step 1 — discover relevant guidance.**

`prep` call:

```json
{
  "intent": "I need to write a Python function that stores a new user password and later verifies it on login",
  "language": "python"
}
```

`prep` response (abbreviated):

```json
{
  "matches": [
    {
      "archetype": "auth/password-hashing",
      "title": "Password Hashing",
      "summary": "Storing, verifying, and handling user passwords in any backend.",
      "score": 6.0
    }
  ],
  "error": null
}
```

**Step 2 — retrieve the full guidance.**

`consult` call:

```json
{ "archetype": "auth/password-hashing", "language": "python" }
```

`consult` response — `content` field (truncated here for brevity — the real payload is the full composed markdown):

````markdown
# Password Hashing — Principles

## Architectural placement
Password handling lives behind a dedicated abstraction — typically a
`PasswordHasher` or `CredentialService` — that HTTP handlers, CLI commands,
and admin tools all go through. No route handler, data-access layer, or view
should ever call a hashing library directly...

## Principles
1. Use a modern memory-hard KDF. Argon2id is the current default...
2. Never invent your own scheme...
3. Tune cost parameters for your hardware. Target 200–500 ms per hash...
4. Verify in constant time...
5. Rehash on login when parameters change...
6. Plaintext passwords live only on the stack...

---

# Password Hashing — Python

## Library choice
`argon2-cffi` ships Argon2id with sensible defaults...

## Reference implementation
```python
from argon2 import PasswordHasher
from argon2.exceptions import VerifyMismatchError, InvalidHashError

_hasher = PasswordHasher(
    time_cost=3,
    memory_cost=64 * 1024,
    parallelism=4,
    hash_len=32,
    salt_len=16,
)

def hash_password(password: str) -> str:
    return _hasher.hash(password)

def verify_password(password: str, encoded: str) -> tuple[bool, bool]:
    try:
        _hasher.verify(encoded, password)
    except (VerifyMismatchError, InvalidHashError):
        return False, False
    return True, _hasher.check_needs_rehash(encoded)
```
````

**Step 3 — the LLM writes the code.**

With the principles and reference implementation in its context window the LLM now produces a `PasswordHasher`-backed credential service on the first try, instead of reaching for `hashlib.sha256` and a homegrown salt.

---

## What ships in the MVP corpus

The MVP is deliberately small — three archetypes that prove the plumbing and demonstrate the content format. Community contributions are how the corpus grows from here.

| Archetype                  | Principles | Language files              |
|----------------------------|------------|------------------------------|
| `auth/password-hashing`    | yes        | `csharp`, `python`           |
| `io/input-validation`      | yes        | `csharp`, `python`, `c`      |
| `errors/error-handling`    | yes        | `csharp`, `go`               |

Every archetype ships a `_principles.md` file (universal architectural guidance, references to OWASP ASVS / cheat sheets / CWE) plus one language file per supported target. Adding a language file to an existing archetype is usually the easiest first contribution — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Architecture

GuardCode is three .NET projects:

```
src/
  GuardCode.Content/     Domain types, YAML loader, strict validator, keyword
                         index, prep + consult services. Pure library. No I/O
                         except the filesystem repository.
  GuardCode.Mcp/         Composition root (Generic Host + Serilog to stderr),
                         MCP tool handlers (`prep`, `consult`), stdio transport.
                         Depends on GuardCode.Content.
tests/
  GuardCode.Content.Tests/
                         xUnit + AwesomeAssertions. Unit tests for every
                         domain piece plus one real-corpus smoke test.
```

Design notes:

- **Deterministic by construction.** The index is an immutable `FrozenDictionary` built once at startup from an immutable input set. There is no runtime mutation. Same corpus in → same answers out.
- **Strict content validation.** YamlDotNet runs with no `IgnoreUnmatchedProperties`. Unknown frontmatter keys, missing required fields, body overflows, and orphan `related_archetypes` references all fail the load. If startup validation passes, the corpus is known-good.
- **File-bound DTOs.** YAML deserialization targets use C# `file`-scoped types so mutability required by the deserializer never leaks onto the public domain surface — the public records are immutable with `IReadOnlyList` / `IReadOnlyDictionary` collections.
- **Central Package Management.** Every NuGet version lives in `Directory.Packages.props`. csproj files reference packages by ID only.
- **Serilog to stderr.** stdio is reserved for MCP wire frames; logs cannot pollute it. `Serilog.Sinks.Console` is configured with `standardErrorFromLevel: Verbose` so every event is routed to stderr.
- **Source-generated log messages.** Hot-path logging uses `[LoggerMessage]` source-gen (CA1848) through the stock `ILogger` abstraction, so Serilog is a drop-in sink.

See [`docs/superpowers/specs/2026-04-11-guardcode-design.md`](docs/superpowers/specs/2026-04-11-guardcode-design.md) for the full design spec.

## Configuration reference

The server resolves the archetype root with the following precedence (first match wins):

1. **Environment variable** `GUARDCODE_ARCHETYPES_ROOT` — absolute, or relative to the current working directory.
2. **`appsettings.json`** key `GuardCode:ArchetypesRoot` — absolute, or relative to the executable.
3. **Default** — `archetypes/` next to the executable (where `dotnet build` copies the corpus).

Example `appsettings.json`:

```json
{
  "GuardCode": {
    "ArchetypesRoot": "/opt/guardcode/archetypes"
  }
}
```

Example shell override (useful for pointing Claude Desktop at a checkout of the repo without rebuilding):

```bash
GUARDCODE_ARCHETYPES_ROOT=/absolute/path/to/GuardCode/archetypes \
  dotnet run --project src/GuardCode.Mcp
```

Logging can be tuned via Serilog's standard configuration surface if you need more detail — the default is `Information` minimum level, with `Microsoft.Hosting.Lifetime` lowered to `Warning` so lifetime noise doesn't dominate stderr.

## Troubleshooting

**"The server started but the client sees no tools."**
Check the client's MCP log. Most clients surface the server's stderr there. GuardCode logs a structured `Starting corpus load` / `Corpus loaded` pair at startup. If you see neither, the client never launched the server — verify the binary path in the client config.

**`CorpusLoadFailed` on startup.**
GuardCode refuses to start with a broken corpus. The stderr diagnostic includes the file path and the validation rule that failed (unknown frontmatter key, body over budget, orphan reference, malformed YAML, etc.). Fix the content and restart. This is by design — it is much better to fail loudly at startup than to serve broken guidance during an MCP call.

**"`prep` returns no matches for an intent that should match."**
The MVP scorer is a keyword index, not an embedding model. It matches when the intent string contains words listed in the archetype's `keywords:` frontmatter or in the title/summary. If a reasonable intent returns nothing, the archetype's `keywords:` list is probably too narrow — file an issue or open a PR that adds the missing terms.

**`language 'java' is not supported`.**
The MVP ships four languages: `csharp`, `python`, `c`, `go`. Others are on the roadmap — adding a language is mostly a content task, not a code task.

**Build errors about `net10.0` not being a valid target framework.**
You need the .NET 10 SDK. `dotnet --list-sdks` should include a 10.x entry.

## Roadmap

The MVP proves the shape. The next steps are about growing the content and widening the supported targets.

- **Corpus expansion** — more archetypes: `auth/session-tokens`, `persistence/sql-access`, `concurrency/shared-state`, `logging/structured-logging`, and a long tail of topics the community cares about.
- **More languages** — JavaScript/TypeScript, Java, Rust are the obvious next targets. Each is a content PR, not a code PR.
- **Smarter prep scoring** — optional embedding-based retrieval as a sibling of the keyword scorer, gated behind a config flag so the deterministic path remains the default.
- **Framework awareness** — the `framework` parameter on `prep` is already accepted on the wire; activating it means adding per-framework sub-files or frontmatter.
- **Content review tooling** — a lightweight linter for PRs that runs the same validator the server runs at startup, so contributors see errors before pushing.

Tracked in GitHub issues. If you want to help with any of these, open an issue first so we can align on scope.

## Contributing

**GuardCode is a content project first.** The server code is small, boring by design, and unlikely to change often. The corpus is where most PRs will land and where the project's real value comes from.

### Ways to contribute

- **Improve existing guidance.** Clarify wording, fix a reference-implementation bug, add a missing anti-pattern, tighten a principle. These are the most valuable contributions because existing archetypes compound every time a new LLM consults them.
- **Add a language file to an existing archetype.** The fastest way to get started. Pick an archetype, pick a language that isn't covered yet (or an existing one you can improve), write the `<language>.md` file against the schema in [CONTRIBUTING.md](CONTRIBUTING.md), and open a PR.
- **Propose a new archetype.** Open an issue first with the category, name, and a rough outline so we can avoid overlap and keep categories coherent.
- **Extend the validator.** If you hit a content mistake the validator didn't catch, add the rule (and the test) so nobody hits it again.
- **Fix a bug or add a test.** Build errors, edge cases in the loader, index-scoring oddities — all welcome.
- **Help with docs.** Tutorials, integration guides for specific MCP clients, translations of the README into other languages.

### Before you start

1. **Read [CONTRIBUTING.md](CONTRIBUTING.md).** It documents the archetype schema, the validator budgets (body line caps, reference-implementation size caps), and what a good principles file looks like. The server will reject anything that violates the schema — knowing the rules up front saves iteration.
2. **Run the tests.** `dotnet test -c Release` should pass on a clean clone before you start making changes, and it is the first thing CI will run on your PR.
3. **Look for issues labeled `good-first-issue`.** If there are none open, file one describing what you want to do and we will help scope it.

### Pull request workflow

- Fork the repo, create a feature branch named after what it does (`content/session-tokens`, `mcp/fix-path-handling`, etc.).
- Keep commits focused. One logical change per commit; one logical PR per topic.
- Every PR runs the build and the full test suite. Red CI blocks merge.
- For content PRs, the validator runs automatically as part of the test suite — if the server refuses to load your new archetype, the test that loads the real corpus will fail with the same diagnostic you would see at startup.
- Reviewers will check that content fits the schema, reads well, cites the right references (OWASP ASVS, OWASP Cheat Sheets, CWE where relevant), and has a reference implementation that actually compiles/runs in the target language.

### Community standards

Be kind, be specific, and assume good faith. Disagreements about content are healthy and expected — treat them as a review of the idea, not the person. We follow the spirit of the [Contributor Covenant](https://www.contributor-covenant.org/).

## Security

If you discover a security issue — either in the server code itself or in guidance that would actively lead an LLM to write insecure code — please **do not** open a public issue. Email the maintainer directly (see the repository profile for contact) with a clear description and, if possible, a reproduction. You will get an acknowledgment within a reasonable window and a fix will be prioritized.

The server itself has a small attack surface by design: it speaks MCP over stdio, does no network I/O, loads only the files under its configured archetype root, and performs no code execution. The likeliest security-relevant bugs are path-handling issues in the loader — those are treated as high priority.

## License

GuardCode is released under the MIT License. See [LICENSE](LICENSE) for the full text.

The archetype content is released under the same MIT License. You are free to fork the corpus, mirror it, translate it, and use it in commercial or non-commercial projects. Attribution is appreciated but not required.

---

GuardCode is open-sourced by Ehab Hussein and the GuardCode contributors. The goal is broad, friction-free adoption across the LLM ecosystem — Claude, GPT, Cursor, and anything else that speaks MCP. If you are using GuardCode in production or in a research project, we would love to hear about it: open a discussion or file an issue tagged `showcase`.
