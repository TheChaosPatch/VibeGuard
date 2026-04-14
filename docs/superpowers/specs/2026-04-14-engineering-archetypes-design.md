# Engineering Archetypes — Design

**Date:** 2026-04-14
**Status:** Approved
**Author:** ehabhussein + VibeGuard working session

## Goal

Introduce a new top-level archetype category `engineering/` containing 17 language-agnostic
principles that give an LLM the instincts to build a project from scratch and evolve it
responsibly. These are not security archetypes — they are engineering fundamentals that
complement the existing security corpus.

This expands VibeGuard beyond "defensive posture" into "holistic engineering guidance"
— matching the project's original vision: SOLID/DRY/architecture guidance, not a SAST
wrapper.

## Scope

### In scope
- New top-level folder `archetypes/engineering/` with 17 archetypes.
- Each archetype is a single `_principles.md` file (language-agnostic, `applies_to: [all]`).
- Frontmatter, principles, anti-patterns, and references follow the existing corpus format
  (see `architecture/threat-modeling/_principles.md` as the reference template).
- README corpus table gets a new row for `engineering` category (17 archetypes).
- `related_archetypes` cross-links between engineering archetypes and existing
  architecture/auth/errors/logging archetypes where the topics touch.

### Out of scope
- Language-specific reference implementations (these are principles-only archetypes).
- Corpus / binary split — that decision is deferred until we see the corpus shape after
  this expansion.
- Changes to the `prep` or `consult` tool behavior. The new archetypes surface through
  the existing IDF-keyword + semantic search pipeline. Keyword-tuning in frontmatter is
  the primary lever.
- A new "genesis vs operational" axis in the schema. All archetypes stay on the existing
  `applies_to` axis.

## The 17 Archetypes

Organized by stage of work. Every archetype is `applies_to: [all]`, `status: stable`.

### Stage 1 — Genesis
1. **engineering/project-bootstrapping** — domain-first not framework-first; bounded
   context before stack choice; resist premature scaffolding; the first commit is a
   domain model, not a `create-react-app`.
2. **engineering/walking-skeleton** — ship the thinnest end-to-end slice (one request,
   one database call, one deploy) before breadth; the skeleton proves the integration
   spine works; only then fill in features.
3. **engineering/yagni-and-scope** — build what's required now, not speculative futures;
   rule of three before abstracting; every speculative feature is a liability that must
   be maintained, tested, documented, and eventually removed.

### Stage 2 — Structure
4. **engineering/module-decomposition** — one responsibility per module; high cohesion,
   low coupling; dependencies point inward toward stable abstractions; cyclic dependencies
   are always a design smell; files that change together live together.
5. **engineering/layered-architecture** — domain core independent of infrastructure;
   application layer orchestrates; infrastructure lives at the edge; dependencies point
   toward the domain, never outward.
6. **engineering/interface-first-design** — contracts before implementations; stable
   abstractions at the core, volatile details at the edge (Stable Dependencies Principle);
   program to interfaces so implementations can be replaced.

### Stage 3 — Code craft
7. **engineering/naming-and-readability** — names reveal intent; ubiquitous language
   shared between code and domain experts; optimize for the reader not the writer; a
   good name removes a comment.
8. **engineering/dry-and-abstraction** — rule of three before abstracting; premature
   abstraction is worse than duplication; WET (write everything twice) until a pattern
   proves itself; wrong abstractions are harder to remove than duplicated code.

### Stage 4 — Evolution
9. **engineering/api-evolution** — additive changes only; versioning strategy chosen
   before v1; deprecation windows with machine-readable warnings; every public contract
   is a promise.
10. **engineering/data-migration-discipline** — forward-only schema changes; migrations
    are code (reviewed, tested, versioned); backfills idempotent and resumable; schema
    changes decoupled from code deploys.
11. **engineering/refactoring-discipline** — small safe steps, each reversible; tests
    as safety net; never mix refactor with feature work in the same commit; refactors
    should not change behavior, ever.

### Stage 5 — Quality
12. **engineering/testing-strategy** — test pyramid shape (many unit, fewer integration,
    few end-to-end); test behavior not internals; deterministic tests only; tests are
    living specification; shared mutable fixtures are a bug.
13. **engineering/continuous-integration** — green main branch is a rule, not an
    aspiration; fast feedback loop (<10 min ideal); automated gates, never "it works on
    my machine"; every commit builds and tests.

### Stage 6 — Operations
14. **engineering/observability** — structured logs, metrics, traces, correlation IDs,
    health endpoints — from day one, never retrofitted; errors with context, not just
    stack traces; the three pillars (logs, metrics, traces) answer different questions.
15. **engineering/deployment-discipline** — small frequent deploys beat big rare ones;
    feature flags decouple deploy from release; safe rollback is non-negotiable;
    blue/green or canary for risky changes.

### Stage 7 — Process hygiene
16. **engineering/commit-hygiene** — small focused commits; intent (not mechanics) in
    messages; atomic and reversible; one logical change per commit; no "WIP" in main
    history.
17. **engineering/documentation-discipline** — README answers "what, why, how to run";
    ADRs (Architecture Decision Records) capture why-not-what; documentation lives with
    code; stale docs are worse than no docs.

## Archetype Format

Each archetype is a single `_principles.md` file at
`archetypes/engineering/<name>/_principles.md`, following the existing template:

```
---
schema_version: 1
archetype: engineering/<name>
title: <Title Case Name>
summary: <one-line description>
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - <8-20 IDF-tuned keywords>
related_archetypes:
  - <cross-links to other archetypes>
references:
  <citations to books, papers, standards>
---

# <Title> -- Principles

## When this applies
<paragraph describing trigger conditions>

## Architectural placement
<paragraph describing where this fits in the engineering lifecycle>

## Principles
1. **<Principle>.** <explanation>
2. ...
(7-10 principles each, bold lead + explanation)

## Anti-patterns
- <bullet list of 5-8 anti-patterns>

## References
- <book/paper/standard references>
```

## Cross-linking Strategy

Every new archetype's `related_archetypes` should link to:
- Other engineering archetypes in the same or adjacent stage.
- Existing security archetypes where the topic overlaps.

Examples:
- `engineering/module-decomposition` → links `architecture/least-privilege`,
  `architecture/defense-in-depth`.
- `engineering/observability` → links `logging/audit-trail`, `logging/sensitive-data`,
  `errors/error-handling`.
- `engineering/api-evolution` → links `architecture/api-design-security`.
- `engineering/data-migration-discipline` → links `persistence/sql-injection`,
  `architecture/data-classification`.
- `engineering/deployment-discipline` → links `architecture/secure-ci-cd`,
  `architecture/supply-chain-security`.
- `engineering/documentation-discipline` → links `architecture/threat-modeling`
  (threat models are living docs).

Cross-links are bidirectional — when we add engineering archetypes, we update the
`related_archetypes` of the security archetypes that point back.

## README Update

Add a new row to the corpus table:

| Category | Archetypes | Scope |
|---|---|---|
| engineering | 17 | language-agnostic engineering fundamentals |

Also update the total count: 60 → 77 archetypes.

## Prep Tool Integration

No code changes. The new archetypes surface through the existing search pipeline.
Frontmatter `keywords` are the tuning knob. Suggested keyword themes per archetype:

- **project-bootstrapping**: bootstrap, greenfield, scaffolding, new-project,
  domain-first, bounded-context
- **walking-skeleton**: walking-skeleton, thin-slice, end-to-end, integration-spine,
  iteration-zero
- **yagni-and-scope**: yagni, scope, speculative, over-engineering, rule-of-three,
  minimum-viable
- **module-decomposition**: module, cohesion, coupling, separation-of-concerns,
  single-responsibility, dependency-direction
- **layered-architecture**: layered, hexagonal, clean-architecture, onion, ports-adapters,
  domain-driven-design
- **interface-first-design**: interface, contract, abstraction, stable-dependencies,
  dependency-inversion
- **naming-and-readability**: naming, readability, ubiquitous-language, intention-revealing,
  clarity
- **dry-and-abstraction**: dry, duplication, abstraction, rule-of-three, premature-abstraction
- **api-evolution**: api-versioning, backward-compatibility, deprecation, breaking-change,
  additive
- **data-migration-discipline**: migration, schema-change, backfill, forward-only,
  idempotent
- **refactoring-discipline**: refactoring, behavior-preserving, small-steps, safety-net
- **testing-strategy**: test-pyramid, unit-test, integration-test, deterministic,
  test-as-spec
- **continuous-integration**: ci, green-main, fast-feedback, automated-build,
  continuous-integration
- **observability**: observability, logging, metrics, tracing, correlation-id, health-check
- **deployment-discipline**: deployment, feature-flag, canary, blue-green, rollback,
  release
- **commit-hygiene**: commit, atomic, version-control, git-hygiene, changelog
- **documentation-discipline**: documentation, readme, adr, architecture-decision-record,
  inline-docs

## Rollout Plan (executed by writing-plans)

1. Scaffold the 17 directory skeleton under `archetypes/engineering/`.
2. Write archetype content — one `_principles.md` per archetype, following the template.
   Likely batched in stage groups (7 batches of 2-3 archetypes each) for manageable
   review.
3. Add cross-links: update `related_archetypes` in the affected existing archetypes to
   point back to the new ones.
4. Update README corpus table (60 → 77) and any website copy.
5. Bump version to 0.8.0 (meaningful corpus expansion).
6. Build, publish binaries, create v0.8.0 release on both ehabhussein/VibeGuard and
   TheChaosPatch/VibeGuard.
7. Post-release: revisit the corpus/binary split question with the new corpus size in hand.

## Success Criteria

- `prep("starting a new Go service")` surfaces engineering/project-bootstrapping,
  walking-skeleton, and yagni-and-scope in the top results.
- `prep("refactoring a large module")` surfaces engineering/refactoring-discipline and
  engineering/module-decomposition.
- `prep("deploying to production")` surfaces engineering/deployment-discipline and
  engineering/observability.
- No existing security archetype result quality regresses — the engineering additions
  should not crowd security archetypes out of relevant queries.

## Open Questions (Deferred)

- **Corpus/binary split**: revisit after v0.8.0 ships. If the corpus keeps growing past
  ~100 archetypes, the split becomes compelling.
- **Language-specific reference sections**: some engineering archetypes (observability,
  testing-strategy) could gain language-specific appendices later (e.g., Go's
  `testing` vs Rust's `#[cfg(test)]`). Out of scope for v1; add if users request.
- **"Genesis vs operational" metadata axis**: could add a new frontmatter field to
  distinguish archetypes that apply at project inception vs mid-life. Deferred until we
  see whether search relevance actually suffers without it.
