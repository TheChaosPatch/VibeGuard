# VibeGuard

**GUARD — Global Unified AI Rules for Development**

Website: [guardvibe.codes](https://guardvibe.codes)

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/Model_Context_Protocol-1.2.0-blue)](https://modelcontextprotocol.io/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)

VibeGuard is an open-source [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server that gives any LLM a high-to-low-level engineering consultant it can call **before** writing a function or class. It ships human-authored guidance — principles, architectural placement, anti-patterns, library choices, and language-specific gotchas — organized into focused **archetypes** that the LLM retrieves through two deterministic tools: `prep` and `consult`.

The intelligence lives in the content, not the server. Everything VibeGuard knows is written by humans, reviewed through pull requests, and validated at load time. The server bundles a small ONNX embedding model (bge-small-en-v1.5, ~133 MB) for semantic search but performs no generative inference — the model only produces vector similarity scores to rank archetypes against intent.

> **You don't call `prep` or `consult` yourself.** VibeGuard ships an instruction prompt to every MCP-aware client during the protocol handshake. Compliant clients (Claude Desktop, Claude Code, Cursor, VS Code) surface that prompt to the LLM as a system message telling the model to reach for VibeGuard on its own — before writing any security- or architecture-sensitive code. Once VibeGuard is installed in your MCP client, **just code normally**. The LLM consults it automatically. The `prep` / `consult` sections below are an API reference for the LLM (and for MCP client developers), not a set of commands you need to type.

---

## Table of contents

- [Why VibeGuard](#why-vibeguard)
- [How it works](#how-it-works)
- [The two tools](#the-two-tools)
- [Installation](#installation)
- [Running the server](#running-the-server)
- [Wiring into an MCP client](#wiring-into-an-mcp-client)
- [End-to-end example](#end-to-end-example)
- [What ships in the corpus](#what-ships-in-the-corpus)
- [Architecture](#architecture)
- [Configuration reference](#configuration-reference)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)
- [Contributing](#contributing)
- [Security](#security)
- [License](#license)

---

## Why VibeGuard

LLMs generate code that runs but defaults to the insecure and architecturally poor path: MD5-hashed passwords, SQL via string concatenation, god-functions that mix HTTP and persistence concerns, inconsistent error handling, "catch `Exception` and log" as a pattern. The fix is not to bolt more static-analysis checks onto already-generated code. Static analyzers reward after-the-fact fault-finding; they do not teach design.

The fix is to give the LLM a place to consult *before* it writes the function. That is what VibeGuard does. The LLM calls `prep("I'm about to write a class that handles login", "python")` and VibeGuard returns a ranked list of archetypes that apply. The LLM then calls `consult("auth/password-hashing", "python")` and gets a single composed markdown document: principles, architectural placement, reference implementation, language-specific gotchas, and tests to write. It reads the guidance and writes better code on the first try. The calls are driven by the system prompt VibeGuard ships during the MCP handshake — the human developer does not orchestrate them.

VibeGuard is **not**:

- A static analyzer or SAST wrapper — false-positive generators do not help.
- An LLM — the server performs no generative inference. It bundles a small embedding model for search ranking only.
- A CWE-per-line ruleset — rules cannot teach architecture.
- Opinionated about how your LLM uses the guidance — enforcement is downstream.

## How it works

```
  LLM ──► prep(intent, language)  ──►  ranked archetype matches
  LLM ──► consult(archetype, lang) ──►  principles + language file composed into one markdown document
```

- **Archetypes** are directories under `archetypes/` containing a `_principles.md` (language-agnostic) and one markdown file per supported language. Each archetype covers one focused topic: password hashing, input validation, error handling, etc.
- **Lifecycle** is explicit in every archetype's frontmatter: `draft`, `stable`, or `deprecated`. Drafts are validated on every build but hidden from the active corpus by default, so half-finished guidance never reaches an LLM. Stable content is the default delivery target. Deprecated archetypes still serve their content but prepend a `> **DEPRECATED**` banner that names the successor, so clients can steer users toward the replacement without hard-failing older sessions.
- **Indexing** happens once at startup. The full corpus is loaded, validated strictly (YAML frontmatter schema, body-length budgets, reference-implementation size caps, orphan-reference detection, lifecycle field requirements), and frozen into an immutable in-memory index. If anything fails validation the server refuses to start and writes a diagnostic to stderr.
- **Server instructions** are sent to the LLM during the MCP handshake. Any MCP-aware client surfaces them as a system-prompt fragment that tells the model *when* to call `prep` — before writing any security-sensitive or architecturally-interesting function — so it reaches for VibeGuard on its own instead of waiting to be told.
- **`prep`** scores archetypes against the LLM's natural-language intent using hybrid search — keyword matching (30% weight) blended with semantic similarity via a local ONNX embedding model (70% weight) — and returns up to 15 candidates, highest-scoring first. Scores are in the 0–1 range. No network calls; all inference runs locally.
- **`consult`** composes the principles file with the language file into one markdown payload. Language-agnostic archetypes (`applies_to: [all]`) return principles only — architectural guidance without code examples. If a language-specific archetype doesn't cover the requested language, it returns a redirect with a suggested alternative when one exists.

## The two tools

### `prep(intent, language, framework?)`

The LLM calls this **before** writing any non-trivial code, passing a free-text description of what it is about to build and the target language. VibeGuard returns up to fifteen ranked archetypes to consider consulting. End users do not invoke `prep` directly — the MCP server instructions tell the model when to reach for it.

| Parameter   | Type     | Required | Description                                                    |
|-------------|----------|----------|----------------------------------------------------------------|
| `intent`    | string   | yes      | Free-text description of what the LLM is about to write. ≤ 2000 chars. |
| `language`  | string   | yes      | Lowercase wire name of the target language. The default server set is `csharp`, `python`, `c`, `go`, `rust`; operators can override the set via `VIBEGUARD_SUPPORTED_LANGUAGES` or `appsettings.json`. An unsupported value yields an error that lists the currently configured set. |
| `framework` | string   | no       | Optional framework hint. Accepted for forward compatibility; not used for filtering in the MVP. |

Response shape:

```json
{
  "matches": [
    {
      "archetype": "auth/password-hashing",
      "title": "Password Hashing",
      "summary": "Storing, verifying, and handling user passwords in any backend.",
      "score": 0.82
    }
  ],
  "error": null
}
```

### `consult(archetype, language)`

Fetches the full guidance document for one archetype. Called by the LLM after `prep` has ranked candidates — again, driven by the handshake instructions, not by the human developer. Language-agnostic archetypes (those with `applies_to: [all]`) return principles only — architectural guidance without code examples. Language-specific archetypes compose principles with a reference implementation, library choices, and gotchas.

| Parameter   | Type   | Required | Description                                             |
|-------------|--------|----------|---------------------------------------------------------|
| `archetype` | string | yes      | Archetype identifier, e.g. `auth/password-hashing`.     |
| `language`  | string | yes      | Lowercase wire name of the target language. Must be in the server's configured supported-language set (defaults: `csharp`, `python`, `c`, `go`, `rust`). |

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

If the archetype doesn't cover the requested language, `content` is null, `redirect` is `true`, `message` explains why, and `suggested` lists sibling archetypes that do apply. Language-agnostic archetypes always return their principles regardless of the requested language.

---

## Installation

### Prerequisites

- An MCP-aware client (Claude Desktop, Claude Code, Cursor, or anything that speaks MCP stdio or Streamable HTTP).
- For the **pre-built binary** path: nothing else. Releases are self-contained.
- For the **build from source** path: **.NET 10 SDK** or later — download from [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) — and **Git**.

### Install a pre-built binary (recommended)

Grab the latest release from the [GitHub Releases page](https://github.com/ehabhussein/VibeGuard/releases). Each release ships self-contained single-file binaries for six platforms, with the archetype corpus bundled alongside the executable:

| Platform          | Archive                                       |
|-------------------|-----------------------------------------------|
| Windows x64       | `vibeguard-mcp-<version>-win-x64.zip`         |
| Windows ARM64     | `vibeguard-mcp-<version>-win-arm64.zip`       |
| Linux x64         | `vibeguard-mcp-<version>-linux-x64.tar.gz`    |
| Linux ARM64       | `vibeguard-mcp-<version>-linux-arm64.tar.gz`  |
| macOS x64 (Intel) | `vibeguard-mcp-<version>-osx-x64.tar.gz`      |
| macOS ARM64       | `vibeguard-mcp-<version>-osx-arm64.tar.gz`    |

Download the archive for your platform, extract it to a stable location (e.g. `C:\Tools\vibeguard` on Windows, `~/.local/share/vibeguard` on Linux/macOS), and wire the full path to `vibeguard-mcp` / `vibeguard-mcp.exe` into your MCP client — see [Wiring into an MCP client](#wiring-into-an-mcp-client).

On Linux and macOS, mark the binary as executable after extracting:

```bash
chmod +x ~/.local/share/vibeguard/vibeguard-mcp
```

The Unix archives (`.tar.gz`) preserve the execute bit; the Windows `.zip` archives do not need to because `.exe` is executable by extension.

No runtime dependencies. The binary includes the .NET runtime via self-contained publish, so you do not need the .NET SDK installed to run it.

### Build from source

```bash
git clone https://github.com/ehabhussein/VibeGuard.git
cd VibeGuard
dotnet build -c Release
```

After a successful build the MCP server binary is at:

```
src/VibeGuard.Mcp/bin/Release/net10.0/vibeguard-mcp.exe   (Windows)
src/VibeGuard.Mcp/bin/Release/net10.0/vibeguard-mcp       (Linux / macOS)
```

The archetype corpus is copied into the output directory next to the binary, so the compiled server is self-contained and can be copied anywhere on disk.

### Run the tests

```bash
dotnet test -c Release
```

The test suite exercises the loader, validator, indexer, and both services against the real corpus. All tests should pass before you wire VibeGuard into a client.

## Running the server

VibeGuard supports two MCP transports — **stdio** (default) and **Streamable HTTP** — over the same tool implementations. Pick the one that fits your deployment.

### Stdio (default) — one process per client

The default mode. The MCP client spawns VibeGuard as a subprocess and talks to it over stdin/stdout. This is the right choice for individual developers using Claude Desktop, Claude Code, Cursor, or any other MCP-aware IDE.

```bash
dotnet run --project src/VibeGuard.Mcp
```

On success the server loads the corpus, binds stdio, and waits silently for MCP protocol frames. Everything written to **stdout** is MCP wire format; all logs go to **stderr**. Press `Ctrl+C` to stop.

**Why stdio is the default:**

- **Zero configuration.** No port to pick, no firewall rule, no TLS cert. The client spawns a subprocess and pipes frames across stdin/stdout.
- **No daemon.** The server only exists while the client is running.
- **Local by construction.** A stdio server cannot be reached from another process, let alone another machine.

### Streamable HTTP — one server, many clients

For teams, CI pipelines, and shared deployments where multiple clients need the same VibeGuard instance. The server starts a Kestrel HTTP listener and serves MCP over Streamable HTTP.

```bash
VIBEGUARD_TRANSPORT=http dotnet run --project src/VibeGuard.Mcp
```

Or on Windows PowerShell:

```powershell
$env:VIBEGUARD_TRANSPORT = 'http'
dotnet run --project src/VibeGuard.Mcp
```

The server listens on port **3001** by default (configurable via `VIBEGUARD_HTTP_PORT` or `appsettings.json`). Logs go to stdout/stderr normally (Kestrel does not use stdout as a protocol channel). The server logs `VibeGuard HTTP transport listening on port 3001` when ready.

**When to use HTTP:**

- A shared team server so everyone consults the same corpus version.
- A CI pipeline agent that calls `prep`/`consult` as part of a code-review step.
- A deployment behind a reverse proxy or load balancer.

**Note:** HTTP mode binds to `0.0.0.0` by default. For production deployments, put it behind a reverse proxy with TLS and authentication — the MCP server itself does not provide either.

## Wiring into an MCP client

### Stdio transport (default)

#### Claude Desktop

Edit `claude_desktop_config.json` (the exact location depends on your OS — see [Anthropic's MCP quickstart](https://modelcontextprotocol.io/quickstart/user)) and add a `vibeguard` entry under `mcpServers`:

```json
{
  "mcpServers": {
    "vibeguard": {
      "command": "C:\\path\\to\\VibeGuard\\src\\VibeGuard.Mcp\\bin\\Release\\net10.0\\vibeguard-mcp.exe"
    }
  }
}
```

Linux / macOS:

```json
{
  "mcpServers": {
    "vibeguard": {
      "command": "/home/you/VibeGuard/src/VibeGuard.Mcp/bin/Release/net10.0/vibeguard-mcp"
    }
  }
}
```

Restart Claude Desktop. Two tools named `prep` and `consult` should appear in the MCP tools list.

#### Claude Code

Claude Code uses the same MCP server model. Add VibeGuard via the CLI:

```bash
claude mcp add vibeguard /absolute/path/to/vibeguard-mcp
```

Or edit your user-level `mcp.json` directly with the same shape as the Claude Desktop example.

#### Other MCP clients (stdio)

Any client that launches MCP servers as stdio subprocesses works the same way: point it at the `vibeguard-mcp` binary, no arguments needed.

### HTTP transport (shared/team deployments)

Start the server in HTTP mode first (see [Running the server — Streamable HTTP](#streamable-http--one-server-many-clients)), then point your MCP client at the running instance.

#### Claude Code

Add VibeGuard as an HTTP MCP server by placing a `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "vibeguard": {
      "type": "http",
      "url": "http://your-server:3001/"
    }
  }
}
```

#### Claude Desktop

```json
{
  "mcpServers": {
    "vibeguard": {
      "type": "http",
      "url": "http://your-server:3001/"
    }
  }
}
```

#### Other MCP clients (HTTP)

Any client that supports MCP Streamable HTTP transport can connect by pointing at the server's root URL (e.g. `http://localhost:3001/`). The server maps the MCP endpoint to `/` by default.

---

## End-to-end example

This is an under-the-hood illustration of how the LLM uses VibeGuard during a real request. **You do not type any of these calls.** Once VibeGuard is installed in your MCP client, the handshake instruction prompt tells the model to do this on its own whenever it is about to write security- or architecture-sensitive code. The example below shows what that conversation looks like from the tool side so you can see why the output code is better.

Assume the LLM is about to write a Python login handler.

**Step 1 — the LLM discovers relevant guidance.**

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
      "score": 0.91
    }
  ],
  "error": null
}
```

**Step 2 — the LLM retrieves the full guidance.**

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

## What ships in the corpus

The corpus ships **77 stable archetypes** across 11 categories and **198 language files** spanning 11 languages. All archetypes are visible to LLM clients by default.

| Category        | Archetype                           | Language files                                                      |
|-----------------|-------------------------------------|---------------------------------------------------------------------|
| **auth**        | `auth/password-hashing`             | `csharp`, `python`                                                  |
|                 | `auth/api-endpoint-authentication`  | `csharp`, `python`                                                  |
|                 | `auth/authorization`                | `csharp`, `python`                                                  |
|                 | `auth/session-tokens`               | `csharp`, `go`, `python`                                            |
|                 | `auth/mfa`                          | `csharp`, `go`, `python`                                            |
|                 | `auth/oauth-integration`            | `csharp`, `go`, `python`                                            |
|                 | `auth/jwt-handling`                 | `csharp`, `go`, `java`, `javascript`, `kotlin`, `php`, `python`, `ruby`, `typescript` |
|                 | `auth/password-reset`               | `csharp`, `go`, `java`, `javascript`, `php`, `python`, `ruby`, `typescript` |
|                 | `auth/rate-limiting`                | `csharp`, `go`, `java`, `javascript`, `php`, `python`, `ruby`, `typescript` |
| **crypto**      | `crypto/symmetric-encryption`       | `csharp`, `python`                                                  |
|                 | `crypto/random-number-generation`   | `csharp`, `go`, `python`                                            |
|                 | `crypto/tls-configuration`          | `csharp`, `go`, `python`                                            |
|                 | `crypto/key-management`             | `csharp`, `go`, `python`                                            |
|                 | `crypto/asymmetric-encryption`      | `csharp`, `go`, `java`, `javascript`, `kotlin`, `python`, `rust`, `typescript` |
|                 | `crypto/hashing-integrity`          | `c`, `csharp`, `go`, `java`, `javascript`, `kotlin`, `python`, `rust`, `typescript` |
| **http**        | `http/ssrf`                         | `csharp`, `python`                                                  |
|                 | `http/xss`                          | `csharp`, `go`, `python`                                            |
|                 | `http/csrf`                         | `csharp`, `go`, `python`                                            |
|                 | `http/security-headers`             | `csharp`, `go`, `python`                                            |
|                 | `http/cors`                         | `csharp`, `go`, `python`                                            |
|                 | `http/content-security-policy`      | `csharp`, `go`, `java`, `javascript`, `php`, `python`, `ruby`, `typescript` |
|                 | `http/request-smuggling`            | `csharp`, `go`, `java`, `php`, `python`, `ruby`                     |
| **io**          | `io/input-validation`               | `c`, `csharp`, `python`                                             |
|                 | `io/path-traversal`                 | `csharp`, `python`                                                  |
|                 | `io/unsafe-deserialization`         | `csharp`, `python`                                                  |
|                 | `io/command-injection`              | `csharp`, `go`, `python`                                            |
|                 | `io/file-upload`                    | `csharp`, `go`, `python`                                            |
|                 | `io/email-injection`                | `csharp`, `go`, `java`, `javascript`, `php`, `python`, `ruby`       |
|                 | `io/xml-injection`                  | `csharp`, `go`, `java`, `kotlin`, `php`, `python`, `ruby`           |
|                 | `io/regex-dos`                      | `csharp`, `go`, `java`, `javascript`, `kotlin`, `php`, `python`, `ruby`, `typescript` |
| **persistence** | `persistence/secrets-handling`      | `csharp`, `python`                                                  |
|                 | `persistence/sql-injection`         | `csharp`, `python`, `rust`                                          |
|                 | `persistence/orm-security`          | `csharp`, `go`, `python`                                            |
|                 | `persistence/dependency-management` | `csharp`, `go`, `python`                                            |
|                 | `persistence/database-connections`  | `csharp`, `go`, `java`, `javascript`, `kotlin`, `php`, `python`, `ruby`, `typescript` |
|                 | `persistence/nosql-injection`       | `csharp`, `go`, `java`, `javascript`, `php`, `python`, `ruby`, `typescript` |
| **errors**      | `errors/error-handling`             | `csharp`, `go`                                                      |
| **logging**     | `logging/sensitive-data`            | `csharp`, `python`                                                  |
|                 | `logging/audit-trail`               | `csharp`, `go`, `python`                                            |
|                 | `logging/log-injection`             | `csharp`, `go`, `java`, `javascript`, `kotlin`, `php`, `python`, `ruby`, `typescript` |
| **memory**      | `memory/buffer-overflow`            | `c`, `go`, `rust`                                                   |
|                 | `memory/use-after-free`             | `c`, `rust`                                                         |
| **concurrency** | `concurrency/race-conditions`       | `csharp`, `go`, `python`                                            |
|                 | `concurrency/deadlock-prevention`   | `c`, `csharp`, `go`, `java`, `kotlin`, `python`, `rust`             |
|                 | `concurrency/resource-exhaustion`   | `csharp`, `go`, `java`, `javascript`, `kotlin`, `python`, `typescript` |
| **architecture**| `architecture/secure-development-lifecycle` | `all` (principles only)                                     |
|                 | `architecture/threat-modeling`              | `all` (principles only)                                     |
|                 | `architecture/defense-in-depth`             | `all` (principles only)                                     |
|                 | `architecture/secure-ci-cd`                 | `all` (principles only)                                     |
|                 | `architecture/data-classification`          | `all` (principles only)                                     |
|                 | `architecture/incident-response`            | `all` (principles only)                                     |
|                 | `architecture/resilience-patterns`          | `all` (principles only)                                     |
|                 | `architecture/api-design-security`          | `all` (principles only)                                     |
|                 | `architecture/container-security`           | `all` (principles only)                                     |
|                 | `architecture/least-privilege`              | `all` (principles only)                                     |
|                 | `architecture/microservice-security`        | `all` (principles only)                                     |
|                 | `architecture/privacy-by-design`            | `all` (principles only)                                     |
|                 | `architecture/secure-configuration`         | `all` (principles only)                                     |
|                 | `architecture/supply-chain-security`        | `all` (principles only)                                     |
|                 | `architecture/zero-trust`                   | `all` (principles only)                                     |
| **engineering** | `engineering/project-bootstrapping`         | `all` (principles only)                                     |
|                 | `engineering/walking-skeleton`              | `all` (principles only)                                     |
|                 | `engineering/yagni-and-scope`               | `all` (principles only)                                     |
|                 | `engineering/module-decomposition`          | `all` (principles only)                                     |
|                 | `engineering/layered-architecture`          | `all` (principles only)                                     |
|                 | `engineering/interface-first-design`        | `all` (principles only)                                     |
|                 | `engineering/naming-and-readability`        | `all` (principles only)                                     |
|                 | `engineering/dry-and-abstraction`           | `all` (principles only)                                     |
|                 | `engineering/api-evolution`                 | `all` (principles only)                                     |
|                 | `engineering/data-migration-discipline`     | `all` (principles only)                                     |
|                 | `engineering/refactoring-discipline`        | `all` (principles only)                                     |
|                 | `engineering/testing-strategy`              | `all` (principles only)                                     |
|                 | `engineering/continuous-integration`        | `all` (principles only)                                     |
|                 | `engineering/observability`                 | `all` (principles only)                                     |
|                 | `engineering/deployment-discipline`         | `all` (principles only)                                     |
|                 | `engineering/commit-hygiene`                | `all` (principles only)                                     |
|                 | `engineering/documentation-discipline`      | `all` (principles only)                                     |

Every archetype ships a `_principles.md` file (language-agnostic architectural guidance, references to OWASP ASVS / cheat sheets / CWE) plus one markdown file per supported language. Some archetypes use `applies_to: [all]` to deliver principles-only guidance that applies regardless of language — these return architectural advice without code examples.

Adding a language file to an existing archetype is usually the easiest first contribution — see [CONTRIBUTING.md](CONTRIBUTING.md). New archetypes land as `draft` first and graduate through review — see the **Archetype lifecycle** section in [CONTRIBUTING.md](CONTRIBUTING.md).

## Architecture

VibeGuard is three .NET projects:

```
src/
  VibeGuard.Content/     Domain types, YAML loader, strict validator, keyword
                         and embedding indexes, hybrid search, prep + consult
                         services. Pure library. Bundles bge-small-en-v1.5 ONNX
                         model as an embedded resource for local inference.
  VibeGuard.Mcp/         Composition root (Generic Host / WebApplication +
                         Serilog), MCP tool handlers (`prep`, `consult`),
                         dual transport (stdio + Streamable HTTP).
                         Depends on VibeGuard.Content.
tests/
  VibeGuard.Content.Tests/
                         xUnit + AwesomeAssertions. Unit tests for every
                         domain piece plus one real-corpus smoke test.
```

Design notes:

- **Immutable at runtime.** Both the keyword index and the embedding index are immutable `FrozenDictionary` instances built once at startup. The ONNX embedding model (bge-small-en-v1.5, 384-dim) runs locally with no network calls. Hybrid scoring blends keyword hits (30%) with cosine similarity (70%) to surface archetypes even when the intent phrasing doesn't contain exact keywords.
- **Strict content validation.** YamlDotNet runs with no `IgnoreUnmatchedProperties`. Unknown frontmatter keys, missing required fields, body overflows, and orphan `related_archetypes` references all fail the load. If startup validation passes, the corpus is known-good.
- **File-bound DTOs.** YAML deserialization targets use C# `file`-scoped types so mutability required by the deserializer never leaks onto the public domain surface — the public records are immutable with `IReadOnlyList` / `IReadOnlyDictionary` collections.
- **Central Package Management.** Every NuGet version lives in `Directory.Packages.props`. csproj files reference packages by ID only.
- **Dual transport.** The same tool implementations serve over stdio (one process per client, for local dev) or Streamable HTTP (one server for many clients, for teams/CI). Transport mode is resolved at startup from `VIBEGUARD_TRANSPORT` / `appsettings.json` / default (`stdio`). In stdio mode, Serilog routes all logs to stderr so stdout stays reserved for MCP wire frames. In HTTP mode, logs go to stdout/stderr normally.
- **Source-generated log messages.** Hot-path logging uses `[LoggerMessage]` source-gen (CA1848) through the stock `ILogger` abstraction, so Serilog is a drop-in sink.

See [`docs/superpowers/specs/2026-04-11-vibeguard-design.md`](docs/superpowers/specs/2026-04-11-vibeguard-design.md) for the full design spec.

## Configuration reference

### Transport mode

VibeGuard resolves the transport with the following precedence (first match wins):

1. **Environment variable** `VIBEGUARD_TRANSPORT` — `stdio` or `http`.
2. **`appsettings.json`** key `VibeGuard:Transport`.
3. **Default** — `stdio`.

### HTTP port

When running in HTTP transport mode, the listening port is resolved with the following precedence:

1. **Environment variable** `VIBEGUARD_HTTP_PORT` — a numeric port value.
2. **`appsettings.json`** key `VibeGuard:HttpPort`.
3. **Default** — `3001`.

### Archetype root

The server resolves the archetype root with the following precedence (first match wins):

1. **Environment variable** `VIBEGUARD_ARCHETYPES_ROOT` — absolute, or relative to the current working directory.
2. **`appsettings.json`** key `VibeGuard:ArchetypesRoot` — absolute, or relative to the executable.
3. **Default** — `archetypes/` next to the executable (where `dotnet build` copies the corpus).

### Supported languages

The set of languages VibeGuard will accept on `prep` and `consult` is configurable. The default set — `csharp`, `python`, `c`, `go`, `rust` — is what ships, but an operator can narrow it, widen it, or swap entries without recompiling the server. Resolution precedence (first match wins):

1. **Environment variable** `VIBEGUARD_SUPPORTED_LANGUAGES` — a comma-separated list of lowercase wire names (e.g. `csharp,python,rust`). Whitespace around commas is trimmed.
2. **`appsettings.json`** key `VibeGuard:SupportedLanguages` — a JSON string array.
3. **Default** — `csharp, python, c, go, rust`.

Wire names must match `^[a-z][a-z0-9\-]*$` — lowercase ASCII, may contain digits and hyphens, must start with a letter, up to 32 characters. Duplicates are collapsed. An empty or malformed set fails startup with a diagnostic on stderr.

The configured set is the allowlist that the archetype loader enforces. At load time, every `applies_to` entry in a principles file and every language-filename stem (e.g. the `rust` in `rust.md`) must be in the configured set; anything else fails the load. This means narrowing the set on a given deployment will reject archetypes that claim support for languages you have excluded, rather than silently skipping them.

Adding a language to a VibeGuard deployment is therefore a content decision, not a code decision: extend the configured set, ship the corresponding language files in the corpus, and the server accepts them on the next startup.

### Lifecycle filter

| Variable                   | Default  | Effect                                                                                             |
|----------------------------|----------|----------------------------------------------------------------------------------------------------|
| `VIBEGUARD_TRANSPORT`      | *(unset, defaults to `stdio`)* | Set to `http` to start the Streamable HTTP server instead of stdio. |
| `VIBEGUARD_HTTP_PORT`      | *(unset, defaults to `3001`)* | Port for the HTTP transport. Ignored in stdio mode. |
| `VIBEGUARD_INCLUDE_DRAFTS` | *(unset)* | Override the `IncludeDrafts` setting. Set to any non-empty value to include draft archetypes; leave unset to use the `appsettings.json` value (default: `true`). |
| `VIBEGUARD_SUPPORTED_LANGUAGES` | *(unset)* | Comma-separated list of lowercase wire names that overrides the default supported-language set. See [Supported languages](#supported-languages). |

Example `appsettings.json`:

```json
{
  "VibeGuard": {
    "ArchetypesRoot": "/opt/vibeguard/archetypes",
    "Transport": "stdio",
    "HttpPort": 3001,
    "IncludeDrafts": true,
    "SupportedLanguages": ["csharp", "python", "rust"]
  }
}
```

Example shell override (useful for pointing Claude Desktop at a checkout of the repo without rebuilding):

```bash
VIBEGUARD_ARCHETYPES_ROOT=/absolute/path/to/VibeGuard/archetypes \
  dotnet run --project src/VibeGuard.Mcp
```

Logging can be tuned via Serilog's standard configuration surface if you need more detail — the default is `Information` minimum level, with `Microsoft.Hosting.Lifetime` lowered to `Warning` so lifetime noise doesn't dominate stderr.

## Troubleshooting

**"The server started but the client sees no tools."**
Check the client's MCP log. Most clients surface the server's stderr there. VibeGuard logs a structured `Starting corpus load` / `Corpus loaded` pair at startup. If you see neither, the client never launched the server — verify the binary path in the client config.

**`CorpusLoadFailed` on startup.**
VibeGuard refuses to start with a broken corpus. The stderr diagnostic includes the file path and the validation rule that failed (unknown frontmatter key, body over budget, orphan reference, malformed YAML, etc.). Fix the content and restart. This is by design — it is much better to fail loudly at startup than to serve broken guidance during an MCP call.

**"`prep` returns no matches for an intent that should match."**
The scorer uses hybrid search: keyword matching (30%) blended with semantic similarity from a local ONNX embedding model (70%). If a reasonable intent returns nothing, the archetype's `keywords:` list may be too narrow or the summary may not be semantically close enough to the intent — file an issue or open a PR that improves the terms or summary.

**`language '<x>' is not supported. Expected one of: ...`.**
The language set is configurable. By default it is `csharp`, `python`, `c`, `go`, `rust` — the error message lists whichever set the running server was configured with, so a deployment that narrowed it will say so. To add a language, extend the set via `VIBEGUARD_SUPPORTED_LANGUAGES` or `VibeGuard:SupportedLanguages` in `appsettings.json`, ship the matching language files in the corpus, and restart the server. See [Configuration reference — Supported languages](#supported-languages).

**HTTP transport: client gets 404 or connection refused.**
The MCP endpoint is mapped to `/` (root path), not `/mcp`. Make sure your client URL is `http://host:port/` with a trailing slash. If the server isn't running, start it with `VIBEGUARD_TRANSPORT=http`. Check that the port matches (`VIBEGUARD_HTTP_PORT` or the default `3001`).

**HTTP transport: 406 Not Acceptable.**
Streamable HTTP requires the `Accept: application/json, text/event-stream` header. MCP-aware clients send this automatically. If you're testing with `curl`, include `-H "Accept: application/json, text/event-stream"`.

**Build errors about `net10.0` not being a valid target framework.**
You need the .NET 10 SDK. `dotnet --list-sdks` should include a 10.x entry.

## Roadmap

The corpus has grown from 3 to 60 archetypes across 10 categories and 11 languages (198 language files). The next steps are about deepening coverage and widening the supported targets.

- **Corpus depth** — fill language gaps (Swift has zero coverage; Rust, C, and Kotlin are thinner than C#/Python/Go), and add new archetypes as the community identifies topics. VibeGuard's value scales with corpus depth. A `template/` folder ships with schema documentation and LLM-ready generation prompts for contributors creating new archetypes.
- **Framework awareness** — the `framework` parameter on `prep` is already accepted on the wire; activating it means adding per-framework sub-files or frontmatter.
- **Content review tooling** — a lightweight linter for PRs that runs the same validator the server runs at startup, so contributors see errors before pushing.

Tracked in GitHub issues. If you want to help with any of these, open an issue first so we can align on scope.

## Contributing

**VibeGuard is a content project first.** The server code is small, boring by design, and unlikely to change often. The corpus is where most PRs will land and where the project's real value comes from.

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

The server itself has a small attack surface by design: it speaks MCP over stdio or HTTP, does no outbound network I/O (the ONNX embedding model runs locally), loads only the files under its configured archetype root, and performs no code execution. The likeliest security-relevant bugs are path-handling issues in the loader — those are treated as high priority.

## License

VibeGuard is released under the MIT License. See [LICENSE](LICENSE) for the full text.

The archetype content is released under the same MIT License. You are free to fork the corpus, mirror it, translate it, and use it in commercial or non-commercial projects. Attribution is appreciated but not required.

---

VibeGuard is open-sourced by Ehab Hussein and the VibeGuard contributors. The goal is broad, friction-free adoption across the LLM ecosystem — Claude, GPT, Cursor, and anything else that speaks MCP. If you are using VibeGuard in production or in a research project, we would love to hear about it: open a discussion or file an issue tagged `showcase`.
