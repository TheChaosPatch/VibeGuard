---
schema_version: 1
archetype: engineering/layered-architecture
title: Layered Architecture
summary: Domain core independent of infrastructure; application layer orchestrates; infrastructure lives at the edge.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - layered-architecture
  - clean-architecture
  - hexagonal
  - onion-architecture
  - ports-and-adapters
  - domain-driven-design
  - dependency-inversion
  - infrastructure
  - application-layer
  - domain-layer
  - framework-independence
  - testability
  - boundaries
related_archetypes:
  - engineering/module-decomposition
  - engineering/interface-first-design
  - engineering/project-bootstrapping
  - architecture/api-design-security
references:
  book: "Clean Architecture — Robert C. Martin"
  book_2: "Implementing Domain-Driven Design — Vaughn Vernon"
  article: "Alistair Cockburn — Hexagonal Architecture"
---

# Layered Architecture -- Principles

## When this applies
From the first commit of any non-trivial system -- a web service, a worker, a CLI tool with persistence, a library with external integrations. The boundaries should be drawn before the first feature, not retrofitted. Also when inheriting a codebase where business logic is tangled with HTTP handlers or ORM calls: introducing a layered boundary is the highest-leverage refactor for making the system testable, portable, and comprehensible.

## Architectural placement
Layered architecture defines the direction of dependencies across the whole system. It sits above module-decomposition (which splits code into units) by saying *what kind* of unit each module is: domain, application, or infrastructure. Every system has these layers whether named or not; naming them makes the rules enforceable. The rule is simple and absolute: outer layers know about inner layers, never the reverse. When the rule holds, the domain is testable without Postgres, swappable from REST to gRPC, and readable without framework background knowledge.

## Principles
1. **Three layers at minimum.** Domain (pure business rules, entities, value objects, domain services), application (orchestration, use cases, transaction boundaries), infrastructure (HTTP, database, message queues, external APIs, filesystem). More layers occasionally justify themselves; fewer than three almost always mean two have been conflated.
2. **Dependencies point inward.** Infrastructure depends on application; application depends on domain; domain depends on nothing outside itself (not even its own language's standard library for IO). Every import statement that violates this direction is a bug waiting to compound.
3. **Domain is pure.** The domain layer contains no HTTP types, no ORM annotations, no logging framework calls, no framework base classes, no JSON serialization concerns. A domain entity is a value with invariants and behavior, not a row in a table. When the domain is pure, unit tests need no mocks, and the domain survives framework migrations untouched.
4. **Application orchestrates; it does not decide.** Use cases coordinate domain objects to execute a scenario: load these entities, invoke these methods, persist the result, publish this event. The application layer owns transaction boundaries and the sequence of steps. It does not contain business rules -- those belong in the domain -- and it does not talk directly to infrastructure internals -- it talks to ports.
5. **Ports and adapters, not direct infrastructure calls.** The application layer declares interfaces (ports) for what it needs: `IOrderRepository`, `IPaymentGateway`, `IEmailSender`. The infrastructure layer provides adapters that implement these ports. The application does not know whether the repository is Postgres, MongoDB, or an in-memory list; that decision is composition, not code.
6. **Composition root at the edge.** Dependency injection wires adapters to ports at the outermost layer -- the entry point, the `Program.cs`, the `main()`. The composition root is the only place that knows the concrete infrastructure. Inner layers never construct infrastructure directly.
7. **Cross-cutting concerns as infrastructure.** Logging, authentication, caching, retry policies -- these live in infrastructure and attach to application code through middleware, decorators, or aspects. The domain does not log; the application does not cache. If a cross-cutting concern seeps into the domain, the abstraction has failed.
8. **Test each layer at its own boundary.** Domain tests are pure unit tests with no mocks. Application tests use fake or in-memory adapters to verify orchestration. Infrastructure tests hit real systems to verify adapters actually work. Mixed-layer tests drift toward slow, flaky, high-mock messes that test nothing clearly.
9. **Framework is detail, not foundation.** The web framework, the ORM, and the DI container are plugins into the application, not the architecture. If swapping them would require rewriting business logic, the layers have bled together.

## Anti-patterns
- Entity classes with ORM annotations (`[Table]`, `@Entity`), JSON attributes (`[JsonPropertyName]`), or validation framework attributes spread across the domain -- every such annotation binds the domain to a framework.
- Controllers that contain business logic (computing totals, deciding eligibility, validating state transitions) -- controllers are infrastructure and should translate protocols only.
- Repositories implemented as domain-layer interfaces that return ORM types like `IQueryable<Order>` -- the leak pulls the ORM through the whole stack.
- A "services" layer that touches both the database and HTTP and contains the business rules, collapsing all three layers into one.
- Application use cases that take `HttpContext` or `IDbConnection` as parameters -- the application should receive domain inputs, not infrastructure handles.
- `using Microsoft.EntityFrameworkCore` in a domain entity file -- the namespace alone is the violation.
- Circular dependencies between domain and infrastructure resolved with reflection or late binding -- the cycle is real even when hidden.

## References
- Robert C. Martin -- *Clean Architecture*
- Alistair Cockburn -- "Hexagonal Architecture" (alistair.cockburn.us/hexagonal-architecture)
- Vaughn Vernon -- *Implementing Domain-Driven Design*
- Jeffrey Palermo -- "The Onion Architecture"
- Eric Evans -- *Domain-Driven Design* (layered architecture chapter)
