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

The JSON blocks below are illustrative sketches, not a literal schema snapshot — the MVP corpus ships three archetypes (`auth/password-hashing`, `errors/error-handling`, `io/input-validation`), so `auth/session-tokens` is a forward-looking placeholder and the real wire responses carry a few more fields than shown here. For the authoritative shapes, see `src/GuardCode.Mcp/Tools/PrepTool.cs` and `src/GuardCode.Mcp/Tools/ConsultTool.cs`.

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
