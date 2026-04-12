---
schema_version: 1
archetype: http/csrf
title: Cross-Site Request Forgery Defense
summary: Ensuring state-changing requests originate from your own application, not an attacker's page.
applies_to: [csharp, python, go]
status: draft
keywords:
  - csrf
  - xsrf
  - cross-site
  - request-forgery
  - token
  - samesite
  - double-submit
  - anti-forgery
  - state-changing
  - origin
  - referer
  - cookie
related_archetypes:
  - http/cors
  - http/security-headers
  - auth/api-endpoint-authentication
references:
  owasp_asvs: V4.2
  owasp_cheatsheet: Cross-Site Request Forgery Prevention Cheat Sheet
  cwe: "352"
---

# Cross-Site Request Forgery Defense — Principles

## When this applies
Every state-changing endpoint — any handler that processes POST, PUT, PATCH, or DELETE — in a web application that uses cookie-based authentication (session cookies, persistent auth cookies, any ambient credential the browser sends automatically). The classic case: a form that transfers money, changes an email address, or deletes an account. The non-obvious cases: a GET endpoint that triggers a side effect (a "confirm" link that activates an account), a JSON API consumed by a same-origin SPA that relies on cookies for auth, a file-upload endpoint, a webhook subscription manager. If the browser sends a credential automatically and the endpoint changes state, CSRF applies.

## Architectural placement
CSRF protection is a **cross-cutting middleware concern**, not something individual handlers implement. The framework provides a token-generation and token-validation pipeline that runs before any handler executes. Every state-changing route is covered by default; safe methods (GET, HEAD, OPTIONS) are excluded. The middleware either validates a synchronizer token (form field or custom header) or verifies the `Origin` / `Referer` header against an allowlist. The handler never has to think about CSRF — if the request reached the handler, it has already passed the check. Token-based APIs that do not use cookies are naturally immune and should be explicitly excluded from the CSRF middleware to avoid confusion.

## Principles
1. **Use the framework's built-in anti-forgery system.** ASP.NET Core, Django, and most frameworks have battle-tested CSRF middleware. Configure it; do not build your own. A custom implementation will miss edge cases that the framework authors have already fixed.
2. **Synchronizer Token Pattern is the primary defense.** The server generates a cryptographically random token, embeds it in the page (hidden form field or meta tag), and validates it on every state-changing request. The token must be tied to the user's session and unpredictable to an attacker on another origin.
3. **Set `SameSite=Lax` (minimum) on all session cookies.** `SameSite=Lax` prevents the browser from sending the cookie on cross-site POST requests, which blocks the most common CSRF vector. Use `SameSite=Strict` for highly sensitive cookies where top-level navigation from external sites is not needed.
4. **Verify `Origin` and `Referer` headers as a secondary check.** If the `Origin` header is present, verify it matches your expected origin(s). This is a defense-in-depth layer — not a replacement for tokens, because `Origin` can be absent on some requests and `Referer` can be stripped.
5. **Never perform state changes on GET requests.** GET must be safe and idempotent. If GET triggers a side effect, `SameSite=Lax` does not protect it because Lax allows GET on top-level navigation.
6. **For SPAs using cookie auth, require a custom request header.** A custom header like `X-Requested-With` cannot be set cross-origin without CORS approval. The server validates its presence. This is the "double-submit variant" simplified for fetch/XHR.
7. **Exclude token-authenticated APIs from CSRF middleware.** Endpoints authenticated by `Authorization: Bearer <token>` headers are not vulnerable to CSRF because the browser does not send the token automatically. Including them in CSRF protection adds complexity with no security benefit.

## Anti-patterns
- Relying on `SameSite=Lax` as the sole CSRF defense. It does not cover all browsers, all request types (GET with side effects), or subdomains.
- Checking the `Referer` header alone without a token. The `Referer` can be stripped by the referring page via `Referrer-Policy: no-referrer` or privacy extensions.
- Using a per-session CSRF token that never rotates. Rotate tokens on privilege changes (login, password change) at minimum.
- Excluding POST endpoints from anti-forgery validation because "it's just an internal API." If a cookie authenticates it, CSRF applies regardless of the intended consumer.
- Storing the CSRF token in a cookie without a corresponding form/header check (the cookie alone does not prove the request came from your page).
- Disabling CSRF protection globally because AJAX requests "don't need it." AJAX with cookie auth absolutely needs it.
- Accepting GET requests for state-changing operations because "it's simpler."
- Using predictable tokens (sequential IDs, timestamps) instead of cryptographically random values.

## References
- OWASP ASVS V4.2 — Session Management
- OWASP Cross-Site Request Forgery Prevention Cheat Sheet
- CWE-352 — Cross-Site Request Forgery
