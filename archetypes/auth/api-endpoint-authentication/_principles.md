---
schema_version: 1
archetype: auth/api-endpoint-authentication
title: API Endpoint Authentication
summary: Requiring and verifying caller identity on every HTTP endpoint that isn't explicitly public.
applies_to: [csharp, python]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - auth
  - authentication
  - endpoint
  - api
  - http
  - jwt
  - bearer
  - middleware
  - authorize
related_archetypes:
  - auth/password-hashing
  - persistence/secrets-handling
  - io/input-validation
references:
  owasp_asvs: V4.1
  owasp_cheatsheet: Authentication Cheat Sheet
  cwe: "287"
---

# API Endpoint Authentication — Principles

## When this applies
Every HTTP endpoint your service exposes. Health checks and explicitly-public marketing routes are the only exceptions, and they must be on a documented allowlist. "I'll add auth later" is how endpoints ship to production unauthenticated — the correct default is authenticated, and the opt-outs are the exceptional case you write down.

## Architectural placement
Authentication runs as middleware at the framework's request-pipeline layer, *before* the route handler sees the request. The middleware is the single place in the codebase that validates credentials and populates a trusted `CurrentUser` (or equivalent) on the request context. Handlers read `CurrentUser`; they do not parse tokens, check signatures, or look up users. The allowlist of unauthenticated routes lives in one named location so a reviewer can audit it in thirty seconds. This structural choice — "authenticated by default, opted out explicitly" — is what keeps a four-line handler from silently skipping auth when a developer forgets an attribute.

## Principles
1. **Authenticated by default.** Configure the framework so that every route requires authentication unless explicitly marked public. Opt-in-to-auth is a footgun that guarantees missed endpoints.
2. **Verify before reading the body.** Rejecting an unauthenticated request should happen before the handler deserializes input. Unauthenticated callers can send megabytes of JSON otherwise.
3. **Validate credentials fully — signature, expiry, issuer, audience, claims.** A JWT that parses is not a JWT that's valid. Use a vetted library's verification API, never a manual split-and-decode.
4. **Fail with a minimal, uniform response.** `401 Unauthorized` with a short body — no stack traces, no reason codes that help an attacker enumerate. "Wrong password" and "unknown user" must look identical over the wire.
5. **Separate authentication from authorization.** Authentication answers "who is this caller." Authorization answers "is this caller allowed to do this." Mix them and the access-control logic becomes untestable. Authenticated users without permission get `403`, not `401`.
6. **Never trust client-supplied identity claims.** A `User-Id` header is not authentication. A JWT you did not verify is not authentication. If the only thing proving who the caller is is a value the caller sent, you have no authentication.
7. **Rate-limit the authentication endpoint itself.** The login or token-refresh route is the one place an attacker can ask "is this credential valid" at scale. Cap attempts per IP and per account.

## Anti-patterns
- Adding `[Authorize]` / `@login_required` per-endpoint and relying on every contributor to remember. One missed decorator is one unauthenticated endpoint in production.
- Decoding a JWT with a base64 split and trusting the claims without verifying the signature.
- Returning different error messages for "token expired," "token invalid," and "user unknown."
- Reading an `X-User-Id` header from an upstream load balancer without validating that the header came from the load balancer and not from the client directly.
- Catching an authentication exception in the global error handler and falling through to the handler anyway.
- Logging the bearer token on authentication failure "for debugging."
- Using `==` to compare an HMAC or token digest — timing leaks.

## References
- OWASP ASVS V4.1 — General Access Control Design
- OWASP Authentication Cheat Sheet
- CWE-287 — Improper Authentication
