---
schema_version: 1
archetype: architecture/api-design-security
title: API Design Security
summary: Building APIs that are secure by default through strong authentication, schema enforcement, rate limiting, and minimal surface area.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - api
  - rest
  - graphql
  - versioning
  - authentication
  - rate-limit
  - schema
  - surface-area
  - idempotency
  - throttling
related_archetypes:
  - auth/api-endpoint-authentication
  - auth/rate-limiting
references:
  owasp_asvs: V4.3
  owasp_cheatsheet: REST Security Cheat Sheet
  cwe: "285"
---

# API Design Security -- Principles

## When this applies
Any system exposing an API -- REST, GraphQL, gRPC, or event-driven -- whether public, partner-facing, or internal. The OWASP API Security Top 10 demonstrates that APIs are the primary attack surface for modern applications. If your system exposes endpoints that receive, process, or return data, this archetype applies from the first route definition.

## Architectural placement
API security spans the gateway layer (rate limiting, authentication enforcement, TLS termination), the application layer (authorization, schema validation, business logic), and the contract layer (versioning, documentation, deprecation). Security decisions made at API design time are far cheaper than those retrofitted after deployment. This archetype governs how those design-time decisions are made.

## Principles
1. **Authenticate every endpoint by default.** The baseline for any new endpoint is: authentication required. Public endpoints are an explicit, documented exception -- not the default. Authentication enforcement belongs at the gateway or framework level, not in individual route handlers.
2. **Authorize at the resource level, not just the route.** Checking that a caller is authenticated is not authorization. Every operation must verify that the caller is permitted to perform that specific action on that specific resource. Object-level authorization failures (IDOR) are the leading API vulnerability class.
3. **Enforce schema validation on all inputs.** Define the expected shape, type, length, and value range of every request field. Reject requests that deviate. Schema validation is not a documentation artifact -- it is the first line of defense against injection, mass assignment, and fuzzing.
4. **Rate-limit and throttle all endpoints.** Apply per-caller, per-endpoint, and per-operation rate limits. Unauthenticated endpoints get stricter limits than authenticated ones. Resource-intensive operations (large queries, file uploads, batch operations) get their own, tighter limits. Absence of rate limiting makes enumeration, brute-force, and denial-of-service trivial.
5. **Minimize the API surface area.** Expose only what callers actually need. Do not return full database rows when callers need three fields. Do not expose administrative operations on the same endpoint as user operations. Every exposed field and operation is an attack surface that must be defended for the lifetime of the API.
6. **Version the API and deprecate old versions on a schedule.** Unversioned APIs cannot be safely changed without breaking callers. Versioning allows old, potentially vulnerable behavior to be retired. Maintaining versions indefinitely accumulates vulnerabilities -- establish a deprecation and sunset policy from day one.
7. **Return consistent, non-leaking error responses.** Error responses must not reveal internal structure: no stack traces, no database error messages, no internal service names, no field-level "user not found" vs. "wrong password" distinctions. Differentiated error messages enable enumeration attacks. Return a correlation ID the caller can reference, not the detail itself.
8. **Protect against mass assignment.** Explicitly allowlist the fields that a caller may set in a create or update operation. Binding request payloads directly to data models without an allowlist allows callers to set fields they should not control (roles, verified status, internal flags).
9. **Use idempotency keys for mutating operations.** POST and PATCH operations that create or modify resources should support idempotency keys so that network retries do not produce duplicate state. This is both a correctness and an integrity concern.
10. **Disable unnecessary HTTP methods.** If an endpoint only supports GET, the server must reject POST, PUT, DELETE, and PATCH. Unenforced method restrictions allow attackers to probe for unintended behaviors in underlying frameworks.

## Anti-patterns
- Public endpoints that require no authentication, with the plan to "add auth later."
- Authorization checks only at the route level, not the resource level -- enabling IDOR.
- Returning full database records in API responses when callers need a subset of fields.
- No rate limiting on authentication endpoints, enabling brute-force attacks.
- Stack traces or database errors in API error responses, leaking implementation details.
- A single API version maintained indefinitely, accumulating technical and security debt.
- Mass assignment: binding raw request bodies directly to database models without field allowlisting.
- GraphQL introspection enabled in production, exposing the full schema to attackers.

## References
- OWASP ASVS V4.3 -- Access Control Design
- OWASP REST Security Cheat Sheet
- OWASP API Security Top 10 -- API1 through API10
- CWE-285 -- Improper Authorization
- CWE-20 -- Improper Input Validation
