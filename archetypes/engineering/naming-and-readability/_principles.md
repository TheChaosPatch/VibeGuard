---
schema_version: 1
archetype: engineering/naming-and-readability
title: Naming and Readability
summary: Names reveal intent; code is read far more often than written; optimize for the reader.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - naming
  - readability
  - ubiquitous-language
  - intention-revealing
  - clarity
  - code-style
  - self-documenting-code
  - variable-naming
  - function-naming
  - class-naming
  - domain-language
  - symbol-naming
related_archetypes:
  - engineering/module-decomposition
  - engineering/dry-and-abstraction
  - engineering/documentation-discipline
  - engineering/refactoring-discipline
references:
  book: "Clean Code — Robert C. Martin"
  book_2: "The Art of Readable Code — Boswell & Foucher"
  book_3: "Domain-Driven Design — Eric Evans (Ubiquitous Language)"
---

# Naming and Readability -- Principles

## When this applies
Every time you declare a variable, function, class, module, file, or endpoint. Every time you read existing code and struggle to understand what it means. Every time you write a comment that explains what a line does -- that comment is often a missing name. Naming is the single most impactful per-line decision: a good name erases the need for comments, documentation, and defensive reading; a bad name propagates confusion everywhere the symbol is referenced.

## Architectural placement
Names are the API of code to the humans reading it. While interface-first-design governs the shapes that cross module boundaries, naming governs the signals that flow along every line. A codebase with clear names is navigable without tools; a codebase with vague names requires IDEs, search, and tribal knowledge just to be read. Names carry the domain's ubiquitous language into the code -- or fail to, leaving the code speaking in a different vocabulary than the business that commissioned it.

## Principles
1. **Names reveal intent.** A name answers "what is this and why does it exist" without the reader needing to read the body. `processData()` reveals nothing; `recomputeTaxForRefund()` reveals purpose. The test: can a stranger guess what the name does without looking at its definition? If not, rename.
2. **Use the domain's vocabulary, not the framework's.** If the business says "subscriber", the code says `Subscriber`, not `UserEntity`. If the domain says "settle", the code says `settle()`, not `processTransactionStatus()`. Mismatched vocabulary forces every reader to translate between business talk and code talk, and the translation drifts over time.
3. **Length matches scope.** A loop counter in three lines can be `i`. A field on a long-lived class needs a full descriptive name. Names serve readers who may be looking from far away -- the further the name travels, the more work it must do.
4. **Avoid disinformation.** Do not name something `orderList` if it is a `Set`. Do not call a boolean `isComplete` if it can be true before completion. Do not reuse a name across scopes to mean different things. Disinformation is worse than vagueness because the reader trusts the name and is betrayed.
5. **Pronounceable, searchable, distinguishable.** Names should be easy to say out loud in meetings (no `genymdhms`). They should be easy to grep -- a name like `e` or `Entity` matches everything. They should differ from nearby names in more than a letter -- `getActiveUser` vs `getActiveUsers` is a bug waiting to happen; `getCurrentUser` vs `listActiveUsers` is clear.
6. **Use nouns for things, verbs for actions.** A variable or class is a noun: `invoice`, `PaymentProcessor`. A function is a verb phrase: `chargeCustomer`, `validateInput`. A boolean is a predicate: `isPaid`, `hasPermission`. Mixing categories (`runProcessor()` as a noun, `payment()` as a function) grates against reader expectations.
7. **Comments explain why, not what.** If a comment describes what the code does, the code is insufficiently clear -- fix the name. Reserve comments for why something is done: the business rule being encoded, the performance tradeoff being made, the bug that prompted the workaround. Comments that describe what rot fast; comments that describe why rot slowly.
8. **Refactor names as understanding grows.** The name chosen on day one is a hypothesis about the concept. When a better name is found, rename -- modern tools make it cheap, and leaving a stale name in place teaches every future reader the wrong concept. Rename commits are not overhead; they are distilling.
9. **Code is read many times more than written.** Every second spent choosing a clearer name saves minutes of confused reading later. The economics heavily favor the reader -- and every member of the team is a reader.

## Anti-patterns
- Hungarian notation baked into names (`strName`, `intCount`, `i_i_customer`) that duplicates type information the language already provides.
- Names that require a dictionary (`util`, `helper`, `manager`, `processor`, `handler`, `service`, `controller`, `data`, `info`) with no further qualification -- each is a confession that the concept was not pinned down.
- Abbreviations and acronyms that save three keystrokes and cost three readers their time: `cstmr`, `prdID`, `usrSvc`.
- Names that encode irrelevant implementation detail: `customerArray`, `orderHashMap`, `emailStringList` -- when the collection type changes, every name lies.
- Reusing a variable for a different purpose halfway through a function, without renaming it -- the reader tracks stale meaning through the rest of the scope.
- Boolean names that read as a question without a subject: `flag`, `status`, `mode` -- name the actual predicate: `isLocked`, `paymentStatus`, `renderMode`.
- Copy-pasting a comment block because "future me will thank past me" -- future you will renamed the code; the comment will lie.
- Classes and modules named after layers (`CustomerHelper`, `OrderManager`, `UserService`) -- the names describe generic scope without describing behavior.

## References
- Robert C. Martin -- *Clean Code* (chapters on meaningful names and functions)
- Dustin Boswell & Trevor Foucher -- *The Art of Readable Code*
- Eric Evans -- *Domain-Driven Design* (Ubiquitous Language)
- Kent Beck -- *Implementation Patterns*
- Steve McConnell -- *Code Complete* (chapters on naming conventions)
