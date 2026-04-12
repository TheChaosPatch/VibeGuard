# VibeGuard Archetype Schema Reference

This document describes the file format, validation rules, and constraints for VibeGuard archetypes. Use it alongside the templates in this folder to create new archetypes.

## Directory structure

```
archetypes/
  {{category}}/
    {{archetype-id}}/
      _principles.md          # Required — the principles file
      csharp.md               # Optional — one per supported language
      python.md
      go.md
      ...
```

- **Category**: lowercase, no spaces (e.g. `auth`, `crypto`, `io`, `http`, `persistence`, `logging`, `memory`, `concurrency`, `errors`, `architecture`).
- **Archetype ID**: lowercase with hyphens (e.g. `password-hashing`, `sql-injection`).
- The full archetype identifier is `category/archetype-id` (e.g. `auth/password-hashing`).

## Supported languages (wire names)

Wire names are lowercase ASCII identifiers matching `^[a-z][a-z0-9\-]*$`, max 32 characters.

Default set: `c`, `csharp`, `go`, `java`, `javascript`, `kotlin`, `php`, `python`, `ruby`, `rust`, `swift`, `typescript`.

The server operator can override this via `VIBEGUARD_SUPPORTED_LANGUAGES` (env, comma-separated) or `VibeGuard:SupportedLanguages` (appsettings.json array).

## _principles.md — Frontmatter fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schema_version` | int | yes | Always `1`. |
| `archetype` | string | yes | Must match `category/id` derived from the directory path. |
| `title` | string | yes | Human-readable title. |
| `summary` | string | yes | One-sentence description used for search and display. |
| `applies_to` | string[] | yes | List of supported language wire names, or `[all]` for language-agnostic guidance. |
| `status` | enum | yes | `draft`, `stable`, or `deprecated`. |
| `author` | string | stable | GitHub username. Required when `status: stable`. |
| `reviewed_by` | string[] | stable | Non-empty list of GitHub usernames. Required when `status: stable`. |
| `stable_since` | string | stable | ISO 8601 date (`YYYY-MM-DD`). Required when `status: stable`. |
| `keywords` | string[] | yes | Terms for keyword search scoring. Use domain vocabulary. |
| `related_archetypes` | string[] | no | Cross-references to related archetype IDs. |
| `equivalents_in` | map | no | Maps language wire name to equivalent archetype ID for redirect. |
| `references` | map | no | External reference keys: `owasp_asvs`, `owasp_cheatsheet`, `cwe`. |
| `superseded_by` | string | deprecated | Required when `status: deprecated`. Points to the replacement archetype ID. |

## _principles.md — Required sections

The markdown body must contain exactly these five level-2 headings (case-sensitive, exact match):

1. `## When this applies`
2. `## Architectural placement`
3. `## Principles`
4. `## Anti-patterns`
5. `## References`

## Language file (e.g. python.md) — Frontmatter fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schema_version` | int | yes | Always `1`. |
| `archetype` | string | yes | Must match the `_principles.md` archetype field. |
| `language` | string | yes | Must match the filename without `.md` (e.g. `python`). |
| `principles_file` | string | yes | Always `_principles.md`. |
| `libraries.preferred` | string | yes | The recommended library. |
| `libraries.acceptable` | string[] | no | Alternative acceptable libraries. |
| `libraries.avoid` | object[] | no | Libraries to avoid, each with `name` and `reason`. |
| `minimum_versions` | map | no | Minimum language/runtime versions. |
| `framework` | string | no | Optional framework hint. |

## Language file — Required sections

The markdown body must contain exactly these four level-2 headings (case-sensitive, exact match):

1. `## Library choice`
2. `## Reference implementation`
3. `## Language-specific gotchas`
4. `## Tests to write`

## Validation constraints

| Rule | Limit |
|------|-------|
| Maximum file length | 200 lines (both principles and language files) |
| Maximum reference implementation code | 40 non-empty lines in the **first** code block of `## Reference implementation` |
| Status field | Must be `draft`, `stable`, or `deprecated` |
| Unknown YAML fields | Rejected (strict parsing) |
| `applies_to: [all]` | No language files allowed (principles-only archetype) |

### Line counting

- File line count: total lines in the file.
- Reference implementation code lines: non-empty lines (at least one non-whitespace character) inside the first fenced code block (``` or ~~~) in the `## Reference implementation` section. Comments count as non-empty.

### Lifecycle rules

- **draft**: No author/reviewed_by/stable_since required. `superseded_by` must be absent.
- **stable**: `author`, `reviewed_by` (non-empty list), and `stable_since` required. `superseded_by` must be absent.
- **deprecated**: `superseded_by` required (points to the replacement). Other lifecycle fields optional.

## Tips for LLM-generated archetypes

1. **Start with the principles file.** Get the security guidance right before writing language files.
2. **Use concrete, specific language.** Name algorithms, libraries, API functions. Vague advice like "use secure defaults" is not useful.
3. **Reference real standards.** Every archetype should cite OWASP ASVS sections, CWE numbers, and relevant cheat sheets.
4. **Keep code blocks tight.** 40 non-empty lines is a hard limit. Show the security-critical path, not a complete application.
5. **Cross-reference related archetypes.** Use `related_archetypes` to link naturally connected topics.
6. **Validate before submitting.** Run `dotnet test` from the repo root — the test suite loads and validates every archetype on disk.
