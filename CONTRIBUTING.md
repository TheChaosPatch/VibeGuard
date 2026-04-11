# Contributing to VibeGuard

VibeGuard's value lives in its content. The server code is small, deterministic, and unlikely to need frequent changes; the corpus under `archetypes/` is where most PRs will land. This document explains the archetype schema, the budgets the validator enforces, and how to propose a new archetype or language file.

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
status: stable                      # draft | stable | deprecated (see Lifecycle)
author: your-handle                 # required for stable
reviewed_by: [reviewer-a]           # required non-empty for stable
stable_since: "2026-04-11"          # ISO date; required for stable
superseded_by: other/id             # required for deprecated, forbidden otherwise
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

## Archetype lifecycle

Every archetype carries an explicit lifecycle stage in its frontmatter. The stage is how VibeGuard tells work-in-progress content apart from signed-off content without fragmenting the directory layout or breaking archetype IDs when content graduates.

There are three stages:

### `draft`

A draft is an archetype that is being written but is not yet ready for LLMs to consume. Drafts:

- Are **parsed and validated** on every build, so CI catches broken drafts before they're merged.
- Are **hidden from the active corpus by default** — `prep()` will not match them and `consult()` will not resolve them. This keeps half-finished guidance from reaching users.
- Do **not** require `author`, `reviewed_by`, or `stable_since` in frontmatter. Drafts are deliberately loose.
- Must **not** carry `superseded_by` — that field is reserved for deprecations.

To see drafts locally while you're working on them, set `VIBEGUARD_INCLUDE_DRAFTS=1` before launching the MCP server. Any non-empty value enables it.

### `stable`

A stable archetype has been reviewed and is part of the active corpus. Promoting a draft to stable requires:

- `author` — the original contributor's handle.
- `reviewed_by` — a non-empty list of reviewer handles. At least one reviewer other than the author.
- `stable_since` — the ISO date (`YYYY-MM-DD`) of promotion.

Stable archetypes are the default delivery target. When an LLM calls `consult()` with no qualifier, it gets stable content.

### `deprecated`

A deprecated archetype is content that has been superseded by a better archetype. Deprecations:

- Must carry `superseded_by: <successor-archetype-id>`. The successor is not required to exist at validation time — broken successors surface at `consult()` resolution, not at load time, which keeps deprecation lightweight.
- Still serve their content on `consult()`, so existing LLM sessions don't hard-fail — but the response is prefixed with a `> **DEPRECATED**` banner naming the successor. This lets LLM clients pattern-match on the marker and steer users toward the replacement.

Deprecated archetypes are kept in-tree rather than deleted so that references from older sessions continue to resolve, and so the historical record of how the guidance evolved stays legible.

### Promotion and deprecation flow

A typical archetype's life:

1. A contributor opens a PR with a new archetype at `status: draft`. CI validates it, but it's not yet visible to users.
2. One or more reviewers leave sign-off comments. When the PR is ready, the author updates the frontmatter to `status: stable`, adds `author`, `reviewed_by`, and `stable_since`, and the PR is merged.
3. Months or years later, a better approach emerges. A new archetype is drafted and promoted through the same flow. The old archetype has its status changed to `deprecated` and gains a `superseded_by` pointer in the same PR that merges the successor.

Nothing is ever deleted. Nothing is ever renamed — archetype IDs are permanent. Content graduates in place.

## How to propose a new archetype

1. Open an issue first if you're unsure whether the archetype is in scope — VibeGuard targets function- or class-level guidance for backend and systems code.
2. Fork the repo and create a branch.
3. Add the directory, the principles file (with `status: draft`), and at least one language file. You do not need `author`, `reviewed_by`, or `stable_since` while the archetype is a draft.
4. Run the tests: `dotnet test SecureCodingMcp.slnx`. The content-corpus smoke test will catch most validation errors immediately.
5. Open a PR. Describe what gap the new archetype fills and which real-world failures it helps prevent.
6. After review, the reviewer (or the author, once a reviewer has approved) promotes the archetype to `status: stable` in the same PR, filling in `author`, `reviewed_by`, and `stable_since` before merge.

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
