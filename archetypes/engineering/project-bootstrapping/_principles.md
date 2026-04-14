---
schema_version: 1
archetype: engineering/project-bootstrapping
title: Project Bootstrapping
summary: Starting from the domain and the problem, not from a scaffold or a framework.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - bootstrap
  - greenfield
  - scaffolding
  - new-project
  - domain-first
  - bounded-context
  - iteration-zero
  - project-setup
  - monorepo
  - repository-structure
  - initial-commit
  - project-kickoff
  - framework-choice
  - stack-selection
  - yagni
related_archetypes:
  - engineering/walking-skeleton
  - engineering/yagni-and-scope
  - engineering/module-decomposition
  - engineering/layered-architecture
  - architecture/threat-modeling
references:
  book: "Domain-Driven Design — Eric Evans"
  book_2: "Growing Object-Oriented Software, Guided by Tests — Freeman & Pryce"
  article: "The Twelve-Factor App (12factor.net)"
---

# Project Bootstrapping -- Principles

## When this applies
Before the first commit of a new system, service, library, or feature that will grow beyond a throwaway prototype. Also when joining a team that is about to start greenfield work. Bootstrapping is the one-way door: choices made in the first week (language, framework, repo layout, dependency graph) calcify within a month and become expensive to reverse within a quarter. The goal is not to "get started quickly" -- the goal is to preserve future optionality.

## Architectural placement
Bootstrapping happens before architecture. You cannot draw a good architecture for a domain you have not modelled. This archetype sits upstream of module-decomposition, layered-architecture, and interface-first-design: those describe how to organize code; this describes what to understand before there is any code to organize. Skipping bootstrapping produces systems where the framework's vocabulary (controllers, routes, models) replaces the domain's vocabulary, and the domain model never recovers.

## Principles
1. **Start with the domain, not the stack.** Before picking a language, framework, or folder layout, write down the nouns and verbs of the problem in plain prose. Who are the actors? What are the bounded contexts? What invariants must hold? A one-page domain description is worth more than a week of framework research. When the domain drives the stack, the stack serves the domain; when the stack drives the domain, the domain is mangled to fit.
2. **Defer the framework decision.** The first commit should not be the output of a scaffolding tool. Scaffolds encode opinions you have not yet earned the right to hold. Start with a plain executable, a plain test runner, and a plain module boundary. Reach for a framework only when you hit a concrete problem the framework solves -- routing, templating, ORM -- and you can name which problem.
3. **Bound the first slice.** Define the smallest valuable increment: one user, one use case, one data flow, end-to-end. If you cannot describe the first slice in two sentences, the scope is too diffuse. The first slice is not a prototype that will be thrown away; it is the walking skeleton that every later feature grows from.
4. **Separate concerns from the first commit.** Even a tiny system benefits from a domain module, an application module, and an infrastructure module. The boundaries will feel like overhead on day one and like oxygen on day thirty. Flat structures calcify into tangles faster than layered structures calcify into rigidity.
5. **Make the build and test loop the first feature.** Before writing any domain code, prove you can build, run, and test the empty shell. If the first hour produces a failing test and a green test, the project has a pulse. If the first day produces a running binary but no tests, testing is already an afterthought and will stay one.
6. **Twelve-factor the foundations.** Config in environment variables, secrets never in source, logs to stdout/stderr, stateless process model, explicit dependencies, dev/prod parity. These are not optional for "real" projects -- they are the minimum viable engineering hygiene. Retrofitting them costs ten times what building them in costs.
7. **Decide versioning and branching before the first PR.** Semantic versioning, conventional commits, trunk-based or git-flow -- pick explicitly, document in the README, and enforce through tooling. Conventions decided by tooling on day one are followed; conventions decided by consensus in month three are not.
8. **Record the decisions that have no obvious answer.** Every choice that survived a deliberation (why Postgres not MongoDB, why REST not gRPC, why monorepo not polyrepo) gets an Architecture Decision Record. Future contributors will ask "why?" -- an ADR answers in one page; tribal knowledge answers with a week of archaeology.
9. **Refuse features that are not in the first slice.** The strongest force on a new project is the urge to add "just one more thing" before the walking skeleton walks. Every added scope before the skeleton is stable is a week of rework later. The first slice must ship, then grow.

## Anti-patterns
- Running `npx create-*` or `dotnet new <template>` as the first commit, inheriting a stack of opinions no one on the team chose deliberately.
- Picking the framework first, then retrofitting the domain into its abstractions (controllers, models, views as the only nouns the system knows).
- Deferring CI, tests, and deployment until "after the MVP" -- by then the project has enough code that adding them is a multi-week project of its own.
- A first slice that spans five services, three databases, and an event bus -- the skeleton cannot walk if it has too many joints.
- Flat repository structure ("put everything in `src/`") under the banner of "we will refactor later." Later is a year later, after the tangle has become load-bearing.
- Copying another project's structure without understanding why it is shaped that way, inheriting constraints that do not apply to the new problem.
- Writing the README last. A README written before the code forces the author to explain the system to a stranger -- if they cannot, the system is not yet understood.

## References
- Eric Evans -- *Domain-Driven Design: Tackling Complexity in the Heart of Software*
- Steve Freeman & Nat Pryce -- *Growing Object-Oriented Software, Guided by Tests*
- The Twelve-Factor App -- https://12factor.net
- Michael Nygard -- *Release It!* (on ADRs and production-readiness from day one)
- Robert C. Martin -- *Clean Architecture* (on deferring framework decisions)
