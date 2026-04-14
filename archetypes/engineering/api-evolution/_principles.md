---
schema_version: 1
archetype: engineering/api-evolution
title: API Evolution
summary: Public contracts evolve additively; breaking changes are rare, deliberate, and accompanied by deprecation windows.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - api-evolution
  - versioning
  - backward-compatibility
  - breaking-change
  - deprecation
  - semantic-versioning
  - api-stability
  - contract
  - additive-change
  - v1
  - rest-versioning
  - grpc-versioning
  - library-versioning
related_archetypes:
  - engineering/interface-first-design
  - engineering/data-migration-discipline
  - engineering/deployment-discipline
  - architecture/api-design-security
references:
  article: "Semantic Versioning 2.0.0 (semver.org)"
  book: "API Design Patterns — JJ Geewax"
  article: "Martin Fowler — Tolerant Reader"
---

# API Evolution -- Principles

## When this applies
Any time a contract consumed by code outside your direct control changes -- a public REST or gRPC endpoint, a library's exported types, a message schema on a queue, a webhook payload, a database shape read by downstream systems. Also at the design of a *new* public API, because the hardest evolution problems are caused by poor choices at v1 that force breaking changes later. Internal APIs within a single deploy unit have more freedom, but the discipline applies wherever multiple components ship on independent timelines.

## Architectural placement
APIs are the system's public treaty with its consumers. Breaking them is expensive; evolving them without breaking is slow. Good evolution discipline lets a system improve without fracturing its ecosystem: clients upgrade when ready, servers add capabilities without waiting, and the lifecycle of each version is predictable. This archetype is the operational counterpart to interface-first-design: interfaces designed for change survive evolution, while interfaces designed for the moment hard-code assumptions that become liabilities.

## Principles
1. **Additive changes are safe; others are not.** Adding a new optional field, a new endpoint, a new method, a new enum value that old clients can ignore -- these are safe. Renaming, removing, retyping, changing required fields, narrowing accepted inputs, widening returned outputs (when clients exhaustively pattern-match) -- these are breaking. Default to additive; treat breaking as a special project.
2. **Choose a versioning strategy before v1.** URL-based (`/v1/orders`), header-based (`Accept: application/vnd.foo.v1+json`), semantic versioning for libraries, message schema registries for events -- pick one and document it. Deciding versioning after v1 ships is a migration project by itself.
3. **Never break v1 without a v2.** If a change cannot be made additively, the new shape ships as a new version alongside the old. Both coexist until consumers have migrated. Silent breaking changes are incidents; announced breaking changes are projects.
4. **Deprecate before removing.** When an endpoint or field is going away, mark it deprecated, emit a warning (response header, log line, structured field), set a removal date, and tell consumers. A deprecation without a removal date is a perpetual footnote; a removal without deprecation is an outage.
5. **Postel's Law, cautiously.** "Be liberal in what you accept, strict in what you send." Accepting extra fields clients send (instead of rejecting them) helps evolution. Sending strictly-defined responses (instead of dumping the whole object) protects your future freedom to change internals. Liberal-in, strict-out is not weakness; it is disciplined flexibility.
6. **Design inputs and outputs as separate types.** The shape clients send and the shape you return should be distinct, even when they overlap 90%. Conflating them binds input and output to evolve together, which they rarely should. `CreateOrderRequest` is not `Order`; `OrderResponse` is not the entity.
7. **Version the data, not just the transport.** Event schemas, database migrations, and cached payloads outlive any single API version. Store a schema version with the data so old consumers can identify and handle (or reject) shapes they do not understand.
8. **Communicate deprecations outside the code.** A deprecation notice in a changelog, a sunset header in responses, an email to known consumers, a dashboard that counts remaining usage of deprecated paths. Relying on consumers to read code comments is how deprecations become indefinite.
9. **Test backward compatibility automatically.** Keep a library of "old client" fixtures that exercise previous versions against the current server; keep contract tests that run on every commit. Compatibility that is not tested is compatibility that has already broken.
10. **Retire versions with data, not opinion.** Decide when to remove a deprecated version based on real usage metrics -- if 2% of traffic still uses v1, removing it is an incident. Instrument every version's traffic from the day it ships.

## Anti-patterns
- Shipping v1 with fields named `data`, `info`, or `details` that are free-form maps, because "we'll firm up the shape later" -- the shape never firms, and every consumer is coupled to undocumented conventions.
- Removing a field in a point release because "no one uses it" without a deprecation window or a telemetry check.
- Changing the type of an existing field ("status was a string, now it's an enum") without a new version -- silently breaks every consumer parsing the old shape.
- Versioning by query string (`?v=2`) without committing to coexistence semantics, producing ambiguous requests that silently upgrade or downgrade.
- `/v2` endpoints that are structurally incompatible with v1 but share URL prefixes and auth tokens, forcing clients to choose between "fully migrate" and "fully revert" with no incremental path.
- Webhook payload changes that only announce "updated schema" via release notes, with no structured version marker in the payload itself.
- Declaring an API "stable" when no automated backward-compatibility tests exist.
- Breaking change migrations with no timeline ("we'll remove v1 eventually") that linger for years and double maintenance load forever.

## References
- Semantic Versioning 2.0.0 -- https://semver.org
- JJ Geewax -- *API Design Patterns*
- Martin Fowler -- "Tolerant Reader" (martinfowler.com/bliki/TolerantReader.html)
- Google AIP -- https://google.aip.dev (API design patterns, versioning guidance)
- Mark Nottingham -- "Deprecation HTTP Header Field" (RFC 8594)
