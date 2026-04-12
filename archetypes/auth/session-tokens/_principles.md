---
schema_version: 1
archetype: auth/session-tokens
title: Session Token Management
summary: Generating, storing, rotating, and invalidating session tokens for authenticated users.
applies_to: [csharp, python, go]
status: draft
keywords:
  - session
  - token
  - cookie
  - jwt
  - opaque
  - csrf
  - logout
  - invalidation
  - rotation
  - timeout
  - httponly
  - samesite
related_archetypes:
  - auth/password-hashing
  - auth/api-endpoint-authentication
  - persistence/secrets-handling
references:
  owasp_asvs: V3.5
  owasp_cheatsheet: Session Management Cheat Sheet
  cwe: "384"
---

# Session Token Management -- Principles

## When this applies
Any time your system issues a credential that proves "this request belongs to an already-authenticated user." This covers both browser sessions (cookies) and API sessions (bearer tokens). It does **not** cover the initial authentication step (see `auth/api-endpoint-authentication`) or long-lived API keys (see `persistence/secrets-handling`). The moment a user proves identity, you issue a session token -- and how you generate, transport, store, and kill that token is what this archetype governs.

## Architectural placement
Session management lives in a dedicated `SessionService` or equivalent middleware layer. Route handlers never generate tokens, set cookies, or touch the session store directly. The session layer has exactly three responsibilities: issue a new session after successful authentication, validate an inbound session on every request, and destroy a session on logout or timeout. This isolation means the cookie flags, the token format, and the storage backend can change in one place without touching handlers. The session store itself is a server-side data structure (database row, Redis entry) keyed by an opaque identifier -- or a signed JWT if you accept the trade-offs documented below.

## Principles
1. **Generate tokens with CSPRNG, minimum 128 bits of entropy.** Use the platform's cryptographic random generator. UUIDs (v4) give you 122 bits -- acceptable but not preferred. Never `Math.random()`, never timestamp-based, never sequential.
2. **Transport only over HTTPS with HttpOnly, Secure, SameSite=Lax (or Strict) cookies.** These three flags close the three most common exfiltration vectors: XSS reads the cookie (HttpOnly), network sniffing (Secure), cross-site request forgery (SameSite). If your session is a bearer token in an Authorization header, store it in memory or an HttpOnly cookie -- never in localStorage or sessionStorage.
3. **Prefer server-side session storage over JWTs for session state.** An opaque token with server-side lookup lets you revoke instantly. A JWT cannot be revoked before expiry without a server-side denylist, which negates its stateless advantage. Use JWTs for short-lived access tokens in a token pair (access + refresh), not as the session itself.
4. **Rotate the session identifier on privilege escalation.** When a user logs in, elevates to admin, or completes MFA, issue a new session ID and invalidate the old one. This prevents session fixation: an attacker who planted a session ID before login cannot ride the escalation.
5. **Enforce both absolute and idle timeouts.** Absolute timeout caps total session lifetime (e.g., 8 hours). Idle timeout caps time since last activity (e.g., 30 minutes). Both are server-enforced, never client-only. When either fires, destroy the server-side session -- do not just clear the cookie.
6. **Invalidate fully on logout.** Delete the server-side session record, clear the cookie with an expired `Max-Age`, and if using JWTs, add the token's `jti` to a denylist until natural expiry. "Logout" that only clears the client-side cookie is not logout.
7. **Bind sessions to the user agent when practical.** Store a fingerprint (IP prefix, User-Agent hash) at session creation and reject requests that drift significantly. This is defense-in-depth against stolen tokens, not a primary control.

## Anti-patterns
- Generating session IDs with `Math.random()`, `random.randint()`, or any non-cryptographic PRNG.
- Storing session tokens in localStorage or sessionStorage (XSS-accessible).
- Setting cookies without `HttpOnly`, `Secure`, or `SameSite` flags.
- Using a JWT as the session and having no way to revoke it before expiry.
- Not rotating the session ID after login (session fixation).
- Implementing "logout" by clearing the cookie on the client without destroying the server-side session.
- Setting idle timeout only on the client via JavaScript timers.
- Embedding sensitive data (roles, permissions, PII) in a JWT payload that travels on every request and is readable by any browser extension.

## References
- OWASP ASVS V3.5 -- Token-based Session Management
- OWASP Session Management Cheat Sheet
- CWE-384 -- Session Fixation
