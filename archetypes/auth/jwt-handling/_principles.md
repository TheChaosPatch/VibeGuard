---
schema_version: 1
archetype: auth/jwt-handling
title: JWT Handling
summary: Creating, validating, and revoking JSON Web Tokens securely across any backend or API gateway.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - jwt
  - token
  - jose
  - jws
  - jwe
  - claims
  - bearer
  - rsa
  - hmac
  - ecdsa
  - jwks
  - expiry
related_archetypes:
  - auth/api-endpoint-authentication
  - auth/session-tokens
  - auth/oauth-integration
  - persistence/secrets-handling
references:
  owasp_asvs: V3.5
  owasp_cheatsheet: JSON Web Token for Java Cheat Sheet
  cwe: "347"
---

# JWT Handling — Principles

## When this applies
Any time your system issues or consumes a JSON Web Token — as an access token in an OAuth 2.0 / OIDC flow, as a stateless session credential, or as an inter-service authorization token. This archetype governs signature algorithms, claim validation, key management, and expiry policy. It does not cover the session cookie layer (see `auth/session-tokens`) or the OAuth redirect flow (see `auth/oauth-integration`).

## Architectural placement
JWT issuance lives behind a `TokenService` or equivalent that owns the signing key, the claim set, and the expiry policy. No route handler constructs a JWT directly. JWT validation runs as middleware before any handler sees the request — the middleware sets a trusted `CurrentUser` on the context, and handlers read that context rather than parsing tokens themselves. The JWKS endpoint (if you publish public keys) is served from a dedicated, cacheable route. This separation means algorithm selection and key rotation happen in one place.

## Principles
1. **Always verify the signature before reading claims.** Parse-and-decode without verification is not authentication. Use a library's verification API; never split the token on `.` and base64-decode the payload without first validating the signature.
2. **Prefer RS256 or ES256 over HS256 for multi-service systems.** HMAC-based HS256 requires sharing the signing secret with every verifier — every service that validates tokens also holds the ability to forge them. Asymmetric algorithms (RS256/ES256) let verifiers hold only the public key.
3. **Explicitly configure the accepted algorithm list.** Many libraries once defaulted to `alg: none` acceptance or dynamically selected the algorithm from the token header. Allowlist only the algorithm(s) you issue. Reject tokens with any other `alg`, including `none`.
4. **Validate all required claims: `iss`, `aud`, `exp`, `nbf`, and `iat`.** A JWT that is not expired is not a JWT that is valid for this audience and this issuer. Check every claim your threat model requires. If you issue `jti`, track consumed `jti` values if you need single-use guarantees.
5. **Keep access token lifetimes short — 5 to 15 minutes.** JWTs cannot be revoked before expiry without a denylist. Short expiry reduces the window of abuse for a leaked token. Use refresh tokens (opaque, server-side) to issue new access tokens without requiring re-authentication.
6. **Rotate signing keys on a schedule and support JWKS.** Publish public keys at a JWKS endpoint with `kid` (key ID) in the JWT header so verifiers can select the correct key during rotation. Never hard-code a signing key in application source.
7. **Never put sensitive data in the payload unless the token is encrypted (JWE).** A signed JWT (JWS) payload is base64url-encoded — not encrypted. Any party that holds the token can read the claims. Do not put PII, permissions that reveal business logic, or internal IDs the user should not know about in an unencrypted payload.

## Anti-patterns
- Decoding the payload with base64 split and reading claims without verifying the signature.
- Accepting `alg: none` tokens because the library defaulted to it.
- Using HS256 with a shared secret across microservices that don't all need signing authority.
- Issuing access tokens with expiry measured in days or "never."
- Storing the JWT secret in source code, environment variables without secret management, or configuration files committed to git.
- Putting a role list or permission set in the JWT payload and treating it as authoritative without re-checking against the database on sensitive operations.
- Using the `kid` header value to construct a filesystem path or SQL query (header injection).
- Ignoring `nbf` (not-before) — a token valid in the future should not be accepted now.

## References
- OWASP ASVS V3.5 — Token-based Session Management
- OWASP JSON Web Token for Java Cheat Sheet (principles apply across languages)
- CWE-347 — Improper Verification of Cryptographic Signature
- RFC 7519 — JSON Web Token
- RFC 7517 — JSON Web Key (JWKS)
