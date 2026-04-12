---
schema_version: 1
archetype: auth/authorization
title: Authorization
summary: Deciding whether an already-authenticated caller is allowed to do a specific thing to a specific resource.
applies_to: [csharp, python]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - authorization
  - authz
  - access
  - control
  - permission
  - role
  - rbac
  - abac
  - policy
  - idor
related_archetypes:
  - auth/api-endpoint-authentication
  - io/input-validation
references:
  owasp_asvs: V4.2
  owasp_cheatsheet: Authorization Cheat Sheet
  cwe: "285"
---

# Authorization — Principles

## When this applies
Every operation that reads, writes, or side-effects data that isn't globally public. Authentication (see `auth/api-endpoint-authentication`) answers "who is the caller." Authorization answers "is this caller allowed to do *this specific thing* to *this specific resource*." The distinction matters because the most common production failure isn't "unauthenticated request got through" — it's "authenticated user A fetched user B's records by changing the id in the URL." That bug is called IDOR, and authorization is the defense.

## Architectural placement
Authorization decisions happen in one layer — an `IAuthorizationService` / `authorize()` function — that takes three inputs: the `CurrentUser` populated by authentication, the action being attempted (`"order.read"`, `"order.update"`, `"payment.refund"`), and the *resource* being acted on (an actual loaded entity, not just an id). It returns either success or `ForbiddenException`. Handlers call `authz.require(user, "order.read", order)` after loading the order but before returning it. Decisions are never scattered across handlers as ad-hoc `if user.IsAdmin` checks — those are impossible to audit, impossible to test, and drift between endpoints. The authorization layer is also the single place that implements resource-level scoping (tenant isolation, organization membership, ownership), so handlers can't accidentally skip the check.

## Principles
1. **Deny by default.** If the authorization service has no rule for a given (user, action, resource) triple, the answer is no. "We forgot to add a rule for this endpoint" should produce 403, not 200.
2. **Check on the object, not the id.** Load the resource first, *then* authorize. `authz.require(user, "read", order)` — the `order` is the loaded entity. Checking by id is how IDORs ship: the code sees `orderId` in the URL, runs an ownership check against a *re-derived* owner, and misses the case where the row actually belongs to a different tenant.
3. **Authentication is not authorization.** A valid JWT proves identity, not entitlement. Don't short-circuit to 200 just because the caller is "logged in." Don't conflate "authenticated" with "staff" or "admin" because they share a token scheme.
4. **Authorize every layer, not just the edge.** If a handler calls a service, and the service makes its own DB query, the service does its own authorization check. Defense in depth — edge middleware can be bypassed by an internal refactor that skips it.
5. **Stable, explicit action names.** `"order.read"` and `"order.update"` are real action identifiers used in code and in tests. Not `"can_do_stuff"`, not a boolean `isAuthorized`, not implicit coupling to HTTP verb. The action is data you can reason about.
6. **Resource scoping is a first-class concept, not a WHERE clause.** Multi-tenant systems: every query is either explicitly global (rare) or scoped by tenant. The scoping happens in the repository layer, and authorization checks confirm the scoping *actually* happened — don't rely on a developer remembering to add `WHERE tenant_id = ?` to every query.
7. **403 for "authenticated but not allowed," 401 for "not authenticated."** The HTTP contract matters: 401 tells the client "log in," 403 tells the client "you're logged in, but this isn't yours." Returning 401 for an authorization failure invites retry loops.

## Anti-patterns
- `if (user.Id == request.UserId)` scattered across handlers. Untestable, un-auditable, and wrong the first time someone adds a "view on behalf of" feature.
- `[Authorize(Roles = "Admin")]` at the edge and no check deeper in the call stack — the service method is reachable from other handlers that don't have the attribute.
- Loading a resource by id and *then* checking "does the caller's tenant_id match the row's tenant_id" — correct if you actually do it, but almost always missed for at least one of `GET`, `PATCH`, `DELETE`, and the background worker.
- Using the HTTP method as the action: "GET is read, POST is write, done." IDORs happen on GET routinely.
- Returning 404 for "record exists but you can't see it" in the same shape as "record doesn't exist" — sometimes correct for privacy, but must be a deliberate choice, not an accident from catching a `ForbiddenException` and rethrowing as `NotFoundException`.
- Caching authorization decisions per-user without a clear invalidation story. Permission changes then take hours to propagate.
- Embedding the authorization policy in the database as opaque bits with no tests. "We have a `permissions` column" is not an authorization strategy.

## References
- OWASP ASVS V4.2 — Operation Level Access Control
- OWASP Authorization Cheat Sheet
- CWE-285 — Improper Authorization
