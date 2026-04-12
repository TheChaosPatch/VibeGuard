---
schema_version: 1
archetype: architecture/secure-development-lifecycle
title: Secure Development Lifecycle
summary: End-to-end guidance for building software that is architecturally sound, maintainable, and secure from project creation through deployment.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - architecture
  - solid
  - dry
  - separation-of-concerns
  - dependency-injection
  - clean-architecture
  - layered-architecture
  - security-by-design
  - project-structure
  - code-review
  - testing-strategy
  - design-patterns
  - maintainability
  - technical-debt
  - modularity
  - interface-segregation
  - single-responsibility
  - open-closed
  - liskov
  - dependency-inversion
related_archetypes:
  - errors/error-handling
  - io/input-validation
  - persistence/secrets-handling
  - persistence/dependency-management
  - logging/audit-trail
references:
  owasp_asvs: V1
  owasp_cheatsheet: Secure Product Design Cheat Sheet
  cwe: "710"
---

# Secure Development Lifecycle -- Principles

## When this applies
Every project, from the first `git init` to production deployment. This is not a checklist you bolt on after the feature works -- it is the discipline that shapes how you create, structure, build, and evolve software. If you are writing code that will be read, maintained, or depended on by anyone (including your future self), this archetype applies. It is especially critical for AI-assisted ("vibe coded") projects where plausible-looking output can mask structural rot, missing validation, and invisible coupling.

## Architectural placement
This archetype sits above every other archetype in the corpus. Where `auth/password-hashing` or `persistence/sql-injection` governs a single concern, this one governs the decisions that determine whether those concerns land in the right place, with the right boundaries, and the right tests. Think of it as the blueprint that tells you *where the walls go* before you start wiring each room. It applies at the project level (folder structure, dependency graph, deployment units) and at the module level (class boundaries, interface contracts, function signatures).

## Principles

### Phase 1 -- Project creation and structure
1. **Start with a clear dependency direction.** Dependencies point inward: UI depends on application logic, application logic depends on domain, domain depends on nothing. Never let your domain layer import an HTTP framework, a database driver, or a cloud SDK. Violations of this rule make the core of your system untestable and unportable.
2. **Organize by capability, not by technical layer.** A folder called `models/` next to `controllers/` next to `services/` forces every feature change to touch three directories. Organize by domain capability (`orders/`, `billing/`, `identity/`) so that related code lives together and changes stay local.
3. **Define explicit module boundaries.** Every module exposes a public interface (a trait, an interface, a module's `__all__`) and hides everything else. If another module needs to reach into your internals, the boundary is wrong -- widen the interface or rethink the split.
4. **Establish a testing strategy from day one.** Decide before writing production code: unit tests for business rules (fast, no I/O), integration tests for boundary adapters (database, HTTP, file system), and a thin layer of end-to-end tests for critical paths. The ratio is many-units, fewer-integrations, fewest-E2E. Untested code is unfinished code.

### Phase 2 -- Design principles in practice
5. **Single Responsibility (SRP).** A module changes for exactly one reason. If a class handles both "validate an order" and "send a confirmation email," a change to email templates forces a redeploy of validation logic. Split them. The test for SRP: can you describe what the module does without using the word "and"?
6. **Open/Closed (OCP).** Extend behavior through new implementations, not by editing existing code. Use interfaces, strategy patterns, or composition so that adding a new payment provider does not require modifying the payment processing core.
7. **Liskov Substitution (LSP).** Every implementation of an interface must honor the contract the caller expects. If a `ReadOnlyRepository` subclass throws on `save()`, callers that depend on `Repository` will break at runtime. Do not inherit to reuse code -- inherit to fulfill a contract.
8. **Interface Segregation (ISP).** No client should be forced to depend on methods it does not use. A `UserService` with 20 methods is not an interface -- it is a liability. Split into `UserAuthenticator`, `UserProfileReader`, `UserAdmin` so each consumer depends only on what it calls.
9. **Dependency Inversion (DIP).** High-level policy should not depend on low-level detail. Both should depend on abstractions. The order processor depends on an `IPaymentGateway` interface; the Stripe adapter implements it. Swapping Stripe for another provider changes one file, not fifty.
10. **DRY means knowledge, not characters.** Do not repeat *decisions* -- a tax rate, a validation rule, a retry policy should live in one place. But two functions that happen to look similar are not duplication if they change for different reasons. Premature abstraction is worse than a little repetition.

### Phase 3 -- Security by design
11. **Validate at every trust boundary.** User input, API payloads, message-queue messages, file contents, and third-party responses all cross a trust boundary. Validate shape, type, length, and range before the data touches business logic. Never rely on the caller to have validated already (see `io/input-validation`).
12. **Default to deny.** Access control, feature flags, configuration: the safe default is "no." A missing permission check grants access. A missing rate limit allows abuse. Every endpoint, every operation, every resource starts locked and is explicitly opened.
13. **Keep secrets out of code and history.** No credentials in source, no tokens in logs, no keys in environment variables baked into images. Secrets come from a secrets manager and are loaded at startup through a single provider (see `persistence/secrets-handling`).
14. **Fail closed and fail informatively.** When something goes wrong, the system stops doing the dangerous thing and reports enough to diagnose the failure without leaking internals to the caller. A stack trace in a 500 response is a security bug. A swallowed exception is a reliability bug. Both are design failures (see `errors/error-handling`).
15. **Minimize blast radius.** Principle of least privilege everywhere: each service gets only the permissions it needs, each module accesses only the data it owns, each deployment unit fails independently. If one component's compromise gives an attacker everything, the architecture is flat, not layered.

### Phase 4 -- Building and evolving
16. **Every change has a test that would have caught the bug.** Before committing a fix, write a test that fails without the fix and passes with it. Before committing a feature, write a test that exercises the contract. If you cannot write a test, the code is too coupled -- fix the design, then test.
17. **Commit small, commit often.** Each commit is a single logical change that compiles, passes tests, and could be reverted independently. "Implement entire feature" is not a commit message -- it is a confession that the work was not broken down.
18. **Manage dependencies deliberately.** Pin versions, audit transitive dependencies, update regularly. A dependency you have not read the changelog for in six months is technical debt. A dependency you pulled in for one utility function is a liability (see `persistence/dependency-management`).
19. **Treat warnings as errors.** Compiler warnings, linter findings, and static-analysis alerts are not noise -- they are defects you have not fixed yet. A codebase with 200 suppressed warnings will hide the 201st one that matters.
20. **Refactor under test coverage.** Never restructure code that is not covered by tests. Write the tests first, then refactor. If the tests still pass, the refactor is safe. If you skip this, you are not refactoring -- you are rewriting and hoping.

## Anti-patterns
- A flat project with every file in the root directory and no module boundaries ("we'll organize later").
- A `Utils` or `Helpers` class that grows to 2,000 lines because no one knows where to put new code.
- Copying a function into a second service instead of extracting a shared module with a defined interface.
- Writing all production code first and adding tests "before release" (they never get written).
- A single god object or service that orchestrates every operation in the system.
- Accepting all AI-generated code without reviewing it against the design or running the tests.
- Skipping input validation because "the frontend already checks it."
- A deployment that requires manual steps documented in a wiki page no one updates.
- Using inheritance to share code between unrelated types instead of composition.
- A 500-line function with nested conditionals instead of small functions with clear names.
- Suppressing linter or compiler warnings to make CI green.

## References
- OWASP ASVS V1 -- Architecture, Design, and Threat Modeling
- OWASP Secure Product Design Cheat Sheet
- CWE-710 -- Improper Adherence to Coding Standards
- Robert C. Martin, *Clean Architecture* (2017)
- Martin Fowler, *Refactoring* (2018)
