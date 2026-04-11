---
schema_version: 1
archetype: io/input-validation
title: Input Validation
summary: Validating untrusted input at every trust boundary.
applies_to: [csharp, python, c, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-11"
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
