---
schema_version: 1
archetype: engineering/interface-first-design
title: Interface-First Design
summary: Define the contract before the implementation; stable abstractions at the core, volatile details at the edge.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - interface
  - contract
  - abstraction
  - dependency-inversion
  - stable-dependencies
  - programming-to-interfaces
  - api-design
  - public-surface
  - encapsulation
  - port
  - seam
  - testability
related_archetypes:
  - engineering/module-decomposition
  - engineering/layered-architecture
  - engineering/api-evolution
  - engineering/testing-strategy
references:
  book: "Clean Architecture — Robert C. Martin"
  book_2: "A Philosophy of Software Design — John Ousterhout"
  book_3: "Design Patterns — Gang of Four"
---

# Interface-First Design -- Principles

## When this applies
When adding a new module, service, external integration, or any unit that other code will depend on. Also when extracting a seam to make legacy code testable, when two modules need to communicate without coupling to each other's internals, and when planning a refactor where multiple implementations may eventually coexist. Interface-first is the discipline of writing down *what* a thing does before writing *how* it does it.

## Architectural placement
Interfaces are the joints of the system. They decide what clients can rely on, what implementations can vary, and what tests can substitute. A well-designed interface is smaller than its implementation -- it hides complexity, names responsibilities, and promises behavior. Interface-first design is the tool that makes module-decomposition enforceable (you cannot couple to what you cannot see) and layered-architecture possible (the inner layer exposes interfaces the outer layer depends on).

## Principles
1. **Write the interface first.** Before writing an implementation, write the interface as if you were its consumer. What method names feel natural at the call site? What parameters and return types make the caller's code read well? The interface that emerges from "how would I want to use this?" is almost always better than the interface extracted from "what does my implementation happen to expose?".
2. **Depend on abstractions, not concretions.** When a module needs to send email, it depends on an `IEmailSender` interface, not on a specific SMTP library. The concrete choice lives at the composition root; the dependent module is shielded from it. This is the Dependency Inversion Principle, and it is what makes code testable and swappable.
3. **Stable Dependencies Principle.** Point dependencies toward stability. A module is "stable" when many things depend on it and it depends on few things -- it is expensive to change. Stable modules should not depend on volatile ones. When you see a stable module importing from a volatile one, reverse the arrow with an interface owned by the stable side.
4. **Interfaces should be small and focused.** The Interface Segregation Principle: clients should not be forced to depend on methods they do not use. A fat `IRepository` interface with fifty methods couples every client to the whole surface; three focused interfaces (`IOrderWriter`, `IOrderReader`, `IOrderEventPublisher`) let clients depend only on what they call.
5. **Name interfaces for the role, not for the implementation.** `IEmailSender` describes a responsibility; `ISmtpClient` describes a mechanism. Callers should read as if they are asking for a capability, not for a specific tool. When the mechanism changes, the role remains.
6. **Document behavior, not mechanics.** The interface contract states invariants ("returns null if the key does not exist", "throws on empty input", "is thread-safe") -- the guarantees callers can rely on. The mechanics (what database, what lock, what algorithm) are implementation detail that the contract deliberately does not expose.
7. **Design for one client first, then generalize.** An interface designed for a hypothetical "any possible client" tends to be bloated and shape-less. Design the interface for the first real client; expand it only when a second real client reveals a missing capability. The interface is a revealed preference, not a speculative forecast.
8. **Keep the interface smaller than the implementation.** A deep abstraction carries complexity behind a narrow door; a shallow one barely wraps its guts. If the interface is as large as the implementation, you have added indirection without reducing complexity. Fold shallow interfaces away; keep deep ones.
9. **Mocks are a smell when interfaces leak.** If tests need elaborate mock setups to exercise a class, the interfaces it consumes probably expose too much. Good interfaces yield tests with simple fakes, not multi-step mock choreography.

## Anti-patterns
- Declaring an interface that has exactly one implementation and no expected second implementation, "for testability" -- duplicates the surface with no real benefit when the concrete type already has a testable seam.
- Interfaces that return implementation-specific types (`IQueryable`, `SqlDataReader`, `HttpResponseMessage`) and leak the mechanism to callers.
- "Header interfaces" that mechanically mirror every method of a single implementation -- the interface says the same thing as the class, without adding abstraction.
- Interfaces stuffed with every method any client might want, forcing every consumer to depend on a bloated surface.
- Naming interfaces after their implementation (`ISmtpEmailSender`, `IMySqlOrderRepository`) -- the role vanishes and the abstraction becomes ceremonial.
- Contracts that do not document failure modes, leaving every caller to discover through bugs what "null" or "throws" actually mean.
- Designing interfaces in isolation from a real consumer, producing abstractions that no real code ever wants to call.

## References
- Robert C. Martin -- *Clean Architecture* (Dependency Inversion, Stable Dependencies, Interface Segregation)
- John Ousterhout -- *A Philosophy of Software Design* (deep modules and narrow interfaces)
- Erich Gamma et al. -- *Design Patterns: Elements of Reusable Object-Oriented Software* ("program to interfaces, not implementations")
- Joshua Bloch -- *Effective Java* (API design chapters)
- Steve Freeman & Nat Pryce -- *Growing Object-Oriented Software, Guided by Tests* (roles, responsibilities, collaborators)
