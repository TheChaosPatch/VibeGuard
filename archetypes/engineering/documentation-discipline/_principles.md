---
schema_version: 1
archetype: engineering/documentation-discipline
title: Documentation Discipline
summary: README explains what/why/how-to-run; ADRs capture why-not-what; docs live with code and rot if they don't.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - documentation
  - readme
  - adr
  - architecture-decision-record
  - inline-docs
  - docstring
  - changelog
  - contributing
  - onboarding
  - knowledge-transfer
  - runbook
  - docs-as-code
related_archetypes:
  - engineering/project-bootstrapping
  - engineering/naming-and-readability
  - engineering/commit-hygiene
  - engineering/api-evolution
references:
  article: "Michael Nygard — Documenting Architecture Decisions"
  book: "Docs for Developers — Bhatti, Corleissen, Lambourne, Nunez, Waters"
  article: "Divio — The Four Kinds of Documentation"
---

# Documentation Discipline -- Principles

## When this applies
From the first commit (the README is not a last-week-before-launch task). When an architectural decision is made that took non-trivial deliberation (record it). When someone asks "why is it like this?" for the second time (the answer belongs in docs, not chat history). When onboarding a new engineer takes longer than a day (docs have not kept up). Documentation discipline is the habit of writing down what is not obvious, where readers will find it, and updating it when the code moves.

## Architectural placement
Documentation is the system's narrative to the humans who operate, maintain, and extend it. Code names the concepts; docs explain why those concepts exist and how they fit together. The discipline recognizes a scale of formality: inline comments for local "why" notes, docstrings for API contracts, README for project entry, ADRs for decisions, runbooks for operations, architecture docs for system-wide understanding. Each level answers a different question; missing levels produce different failures.

## Principles
1. **Docs live with code.** A README in the repo, docstrings next to the function, ADRs in `docs/`, changelog beside the source. External wikis and Confluence spaces drift within weeks; in-repo docs are updated in the same PR as the code, reviewed in the same review. "Docs as code" is not a slogan; it is the only docs model that survives change.
2. **README answers: what, why, how to run.** First paragraph tells a stranger what the project is and is not. A "why" section explains the motivation -- what problem, what audience, what alternatives were considered. "How to run" is five commands, tested, and works from a fresh clone. A README that does not pass all three tests is incomplete.
3. **ADRs capture why-not-what.** An Architecture Decision Record is a short document (one page is plenty) that names a decision, its context, the alternatives considered, and the reasoning. ADRs are numbered, immutable once accepted, and superseded-not-edited when decisions change. They answer "why is it like this?" long after the people who decided have moved on.
4. **Write docs for readers, not for yourself.** The author already knows. The reader does not. Structure, terminology, and examples should serve someone encountering the concept for the first time. If an onboarding engineer cannot run the code from the README, the README is wrong -- not the engineer.
5. **Comments explain why, not what.** If a comment describes what the code does, the code is insufficiently clear -- fix the name, not the comment. Reserve inline comments for why: the business rule, the performance tradeoff, the bug that caused the workaround. What-comments rot; why-comments age well.
6. **Docstrings document the contract.** Public APIs have docstrings that describe inputs, outputs, invariants, error modes, and examples. Not "this function adds two numbers" but "returns the sum; raises TypeError on non-numeric input". Docstrings are API docs; they must be accurate because callers trust them.
7. **Four kinds of docs, know which you are writing.** Tutorials (learning-oriented, holding the reader's hand), how-tos (task-oriented, recipe form), reference (information-oriented, exhaustive and dry), explanation (understanding-oriented, the "why" behind the design). Mixing them (a tutorial that tries to be exhaustive) produces docs that fail every audience.
8. **Stale docs are worse than no docs.** An empty section says "I do not know"; a wrong section says "I lied." When docs drift from reality they must be updated or deleted. Treat doc bugs as bugs; a PR that changes behavior without updating docs is incomplete.
9. **Runbooks for production operations.** How to deploy, how to roll back, how to restart a service, how to drain a node, what alerts mean and how to triage them. Runbooks save incidents; they exist before the 3 AM page, not after.
10. **Changelog tracks user-visible change.** Version by version, a short bullet list of what changed, grouped by category (added, changed, deprecated, removed, fixed, security). The changelog is the API contract's human-readable form and the first thing consumers read when they upgrade.

## Anti-patterns
- A wiki or external doc site that is one year stale, contradicts the code, and no one updates because no one knows they should.
- Comments that re-state the code ("increments counter by 1"; the expression already said that) -- pure noise.
- "We'll write the README later" -- the project launches, nobody writes it, new engineers burn days on setup.
- ADRs written after the fact to rationalize a decision already made, serving as political cover rather than design documentation.
- Docs that assume the reader knows everything the author does, skipping the 30% that makes the project comprehensible.
- Auto-generated API docs that dump method signatures with no prose, claiming "the docs are there" while providing no understanding.
- Runbooks that say "see the deployment script" -- deferring to another file that also has no documentation.
- A changelog that reads "bug fixes and improvements" every release, telling consumers nothing actionable.
- Documentation reviewed by a different team than the code, guaranteeing drift the moment the review lands.
- Copy-pasted boilerplate README sections from a template, leaving placeholder text like "Describe your project here" in production.

## References
- Michael Nygard -- "Documenting Architecture Decisions" (cognitect.com/blog/2011/11/15/documenting-architecture-decisions)
- Jared Bhatti et al. -- *Docs for Developers: An Engineer's Field Guide to Technical Writing*
- Divio -- "The Documentation System" (documentation.divio.com) -- four kinds of docs
- Keep a Changelog -- https://keepachangelog.com
- Daniele Procida -- "What nobody tells you about documentation"
