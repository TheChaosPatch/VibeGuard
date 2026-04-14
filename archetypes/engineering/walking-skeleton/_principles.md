---
schema_version: 1
archetype: engineering/walking-skeleton
title: Walking Skeleton
summary: Ship the thinnest end-to-end slice first; prove the spine works before growing features.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - walking-skeleton
  - thin-slice
  - end-to-end
  - integration-spine
  - iteration-zero
  - vertical-slice
  - steel-thread
  - tracer-bullet
  - mvp
  - first-deploy
  - continuous-delivery
  - minimum-viable
  - incremental-delivery
related_archetypes:
  - engineering/project-bootstrapping
  - engineering/yagni-and-scope
  - engineering/continuous-integration
  - engineering/deployment-discipline
  - engineering/testing-strategy
references:
  book: "Growing Object-Oriented Software, Guided by Tests — Freeman & Pryce"
  book_2: "The Pragmatic Programmer — Hunt & Thomas (tracer bullets)"
  article: "Alistair Cockburn — Walking Skeleton"
---

# Walking Skeleton -- Principles

## When this applies
At the start of any new system, service, or major feature. The walking skeleton is the second step after bootstrapping: once the domain is understood and the project shell exists, build the thinnest possible end-to-end path before growing breadth. Also when resurrecting a stalled project that has deep partial implementations but nothing running end-to-end -- retrofit a skeleton before resuming feature work.

## Architectural placement
The walking skeleton establishes the *integration spine* -- the connective tissue between all layers and systems that matter: frontend to backend to database to deploy to production. It is deliberately minimal in functionality and deliberately complete in structure. Every downstream feature grows by *extending* the skeleton, never by building parallel untested pipes. Without a skeleton, teams accumulate modules that work in isolation but have never met; integration becomes a late, painful, all-hands crisis.

## Principles
1. **Thinnest slice, full stack.** The skeleton exercises every layer the production system will have -- UI (if any), API, domain logic, persistence, authentication, logging, deploy pipeline, monitoring -- but does so with the most trivial possible behavior. One endpoint, one database row, one deployed instance, one log line. Width, not depth.
2. **End-to-end from day one.** The skeleton must reach production (or production-like) infrastructure on the first day it exists. A skeleton that only runs on a developer laptop is not a skeleton; it is a prototype. The deploy pipeline is part of the skeleton, not a follow-up project.
3. **Automated on every commit.** The skeleton builds, tests, and deploys automatically. No manual steps. No "I'll hook up CI after the prototype." Manual steps calcify into tribal knowledge; the first missed deploy destroys trust in the pipeline.
4. **Real services, not mocks.** The skeleton hits a real database, a real queue, a real auth provider -- not in-memory stand-ins. Mocks hide integration bugs; the skeleton exists to expose them. Use cheap test-tier instances if cost is a concern, but use the real technology.
5. **Observable from the first request.** The skeleton emits structured logs, a request metric, and a trace on its one endpoint. Observability retrofitted after features is always incomplete; observability baked into the skeleton propagates naturally to every feature that extends it.
6. **Grow by extension, never by parallel pipe.** Every new feature modifies or extends the skeleton's path -- same logging, same metrics, same deploy pipeline, same error-handling conventions. When someone proposes a new module with its own logging setup, its own deploy script, its own error types: refuse. One spine, many branches.
7. **Refuse features until the skeleton walks.** The skeleton is "walking" when a commit to main reaches production and emits telemetry without human intervention. Do not add the second feature until the first can do this. The temptation to add "one quick feature" before the spine is proven produces the hardest bugs later -- bugs that only appear under the specific combination of real deploy + real data + real user.
8. **Keep the skeleton visible.** The first deploy should have a URL, a dashboard, a test in CI that any engineer can look at and see "the spine is alive." Visibility builds confidence and makes regressions obvious. An invisible skeleton is a dead skeleton.

## Anti-patterns
- Building the database layer, the API layer, and the frontend in isolation, then integrating "at the end" -- the end never arrives, and integration reveals fundamental incompatibilities.
- A skeleton that works in staging but has no production deploy, because "production is risky" -- the risk compounds with every un-deployed week.
- Mocking external services in the skeleton ("we'll wire up the real auth provider later") -- the mock never graduates, and the first real integration is a crisis.
- Growing the skeleton wide before it is end-to-end ("let's get all the domain models right first, then we'll deploy") -- you now have a large unverified codebase.
- Deferring CI and automated deploy until the skeleton "is worth automating" -- by then the team has forgotten how to deploy from scratch.
- Treating the skeleton as prototype code to throw away -- the skeleton *is* production code, because every later line rests on it.
- Parallel pipelines: "the billing team has its own deploy, its own logs" -- the spine fractures and observability becomes a reconciliation problem.

## References
- Alistair Cockburn -- *Walking Skeleton* (alistair.cockburn.us)
- Steve Freeman & Nat Pryce -- *Growing Object-Oriented Software, Guided by Tests* (chapter on walking skeletons)
- Andrew Hunt & David Thomas -- *The Pragmatic Programmer* (tracer bullet development)
- Jez Humble & David Farley -- *Continuous Delivery*
- Martin Fowler -- "StranglerFigApplication" (on growing systems from thin starts)
