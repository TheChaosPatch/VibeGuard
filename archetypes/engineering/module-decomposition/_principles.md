---
schema_version: 1
archetype: engineering/module-decomposition
title: Module Decomposition
summary: Split code into small focused units with high cohesion, low coupling, and dependencies pointing toward stable abstractions.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - module
  - decomposition
  - cohesion
  - coupling
  - separation-of-concerns
  - single-responsibility
  - dependency-direction
  - package-structure
  - namespace
  - bounded-context
  - encapsulation
  - information-hiding
  - solid
related_archetypes:
  - engineering/layered-architecture
  - engineering/interface-first-design
  - engineering/dry-and-abstraction
  - engineering/project-bootstrapping
  - architecture/least-privilege
references:
  book: "A Philosophy of Software Design — John Ousterhout"
  book_2: "Clean Architecture — Robert C. Martin"
  book_3: "Designing Data-Intensive Applications — Martin Kleppmann"
---

# Module Decomposition -- Principles

## When this applies
Whenever you are choosing where a new piece of code lives, when a file or class has grown past comfort, when adding a feature requires changes in more than three disparate files, or when circular dependencies appear in the build graph. Also during periodic refactoring: module boundaries that felt right at 500 lines often feel wrong at 5000 and need to be redrawn as the system's real joints become visible.

## Architectural placement
Module decomposition is the shape of the codebase. It determines which changes are local and which ripple across the system, which teams can work without stepping on each other, and which parts of the system can be understood in isolation. Good decomposition makes most changes affect one module; bad decomposition makes every change touch many modules. It sits below layered-architecture (which decides the kinds of modules) and above interface-first-design (which decides how modules expose themselves).

## Principles
1. **High cohesion, low coupling.** Code inside a module should be strongly related; code across modules should be weakly related. Cohesion is the test: if the module had to split, would the split feel natural or violent? Coupling is the test: if the module's internals changed, how many other modules would need to change? Both tests are continuous, not binary -- aim for fewer, thicker seams.
2. **One reason to change, per module.** The Single Responsibility Principle at the module scale: a module should have one axis of variation. If an HTTP-handling change, a database-schema change, and a business-rule change all force edits in the same module, that module has three responsibilities masquerading as one.
3. **Dependencies point inward toward stability.** Volatile code (HTTP adapters, database drivers, UI) depends on stable code (domain rules, entities, policies); stable code never depends on volatile code. Violations look like domain entities that import ORM types or business rules that reach for a specific web framework. Reverse the arrows and the system becomes testable and portable.
4. **Cyclic dependencies are design bugs, always.** When module A depends on B and B depends on A, one of them is not a real module -- the boundary is imaginary. Break the cycle by extracting a third module, by inverting the dependency (interface in the depended-on module, implementation in the depending one), or by merging. Never paper over a cycle with late binding.
5. **Deep modules beat shallow ones.** A deep module has a small interface and a large hidden implementation. A shallow module has a large interface that barely wraps its implementation. Deep modules carry complexity so callers don't have to; shallow modules leak complexity while pretending to hide it. Prefer one deep `Encryptor` over a facade that exposes every step.
6. **Files that change together live together.** If two files consistently appear in the same commits, they probably belong in the same module. If a module's files rarely move together, the module is probably too broad. The commit history is a revealed preference about real module boundaries.
7. **Size is a smell, not a rule.** A 2000-line file is probably too big; a 200-file module is probably too big; a system with a hundred modules each containing one file is probably too fragmented. Use size as a prompt to examine cohesion, not as a mechanical cutoff.
8. **Name the module after what it does, not what it is.** `OrderPricing` is a module; `PricingHelpers`, `OrderUtils`, `Common` are not. Names that describe behavior make boundaries obvious; names that describe layers ("helpers", "utilities", "managers") become junk drawers that accumulate anything that does not fit elsewhere.
9. **Hide the hard parts.** Information hiding is the primary job of a module: the module owns a hard decision (which algorithm, which data structure, which protocol) and callers never need to know. If callers must know the internals to use the module correctly, the abstraction has failed.

## Anti-patterns
- A `Common`, `Util`, `Helpers`, or `Shared` module that everything depends on -- it is a vortex that accretes code with no real cohesion and guarantees every change ripples everywhere.
- Organization by technical layer ("all controllers here, all services here, all repositories here") at the top level of the codebase, separating code that changes together and clustering code that never changes together.
- Circular dependencies silenced by interfaces, dynamic loading, or reflection, without fixing the underlying design.
- One gigantic "application" module that contains the domain, the HTTP layer, the database layer, and the tests -- and can only be worked on by one person at a time.
- Modules exposing mutable state (global objects, singletons with setters) that every other module reaches into.
- Names like `OrderManager`, `UserService`, `DataHandler` that describe scope without describing behavior, hiding the real responsibility.
- "Just put it in the bottom of the file for now" -- the beginning of a shallow module that never gets properly factored.

## References
- John Ousterhout -- *A Philosophy of Software Design* (deep vs shallow modules, information hiding)
- Robert C. Martin -- *Clean Architecture* (Stable Dependencies Principle, SRP)
- David Parnas -- "On the Criteria To Be Used in Decomposing Systems into Modules" (1972)
- Eric Evans -- *Domain-Driven Design* (bounded contexts as module boundaries)
- Michael Feathers -- *Working Effectively with Legacy Code* (seams and dependency breaking)
