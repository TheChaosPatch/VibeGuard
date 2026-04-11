---
schema_version: 1
archetype: errors/error-handling
title: Error Handling
summary: Structuring error paths so failures are observable, actionable, and safe.
applies_to: [csharp, python, c, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-11"
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
