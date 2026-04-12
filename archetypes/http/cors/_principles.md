---
schema_version: 1
archetype: http/cors
title: Cross-Origin Resource Sharing Configuration
summary: Configuring CORS to allow legitimate cross-origin requests without opening the door to unauthorized access.
applies_to: [csharp, python, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - cors
  - cross-origin
  - origin
  - preflight
  - access-control
  - allow-origin
  - allow-credentials
  - allow-methods
  - allow-headers
  - options
  - fetch
  - same-origin
  - wildcard
related_archetypes:
  - http/csrf
  - http/security-headers
  - auth/api-endpoint-authentication
references:
  owasp_asvs: V14.5
  owasp_cheatsheet: CORS Cheat Sheet (referenced within REST Security Cheat Sheet)
  cwe: "942"
---

# Cross-Origin Resource Sharing Configuration — Principles

## When this applies
Any API or web endpoint that receives requests from JavaScript running on a different origin (scheme + host + port). The classic case: a SPA at `https://app.example.com` calling an API at `https://api.example.com`. The non-obvious cases: a development server on `localhost:3000` calling an API on `localhost:5000` (different ports are different origins), a CDN-served static site calling your backend, a partner's embedded widget calling your public API, a mobile app's WebView making fetch requests. If the browser sends an `Origin` header and the response needs `Access-Control-Allow-Origin` for the request to succeed, CORS configuration applies.

## Architectural placement
CORS is a **middleware concern** configured once at the application entry point, not in individual handlers. The middleware inspects the `Origin` header on incoming requests, checks it against a configured allowlist of origins, and sets the appropriate `Access-Control-*` response headers. Preflight `OPTIONS` requests are handled entirely by the middleware — they never reach your business logic. The allowlist is explicit (no wildcards for credentialed endpoints) and lives in configuration so it can be audited and changed without code modifications.

## Principles
1. **Never use `Access-Control-Allow-Origin: *` with credentials.** The wildcard origin combined with `Access-Control-Allow-Credentials: true` is a specification violation that browsers reject, but reflecting the `Origin` header verbatim achieves the same dangerous effect. If you need credentials, enumerate every allowed origin explicitly.
2. **Allowlist origins, do not reflect the `Origin` header.** A common anti-pattern is reading the `Origin` header and echoing it back in `Access-Control-Allow-Origin`. This turns CORS into a no-op — any origin is allowed. Compare the `Origin` against a static list and only set the header if the origin is in the list.
3. **Restrict `Access-Control-Allow-Methods` to what the endpoint actually supports.** Do not allow `PUT`, `PATCH`, or `DELETE` globally if only `GET` and `POST` are needed. Overly broad method allowlists expand the attack surface if CSRF or other cross-origin vulnerabilities exist.
4. **Restrict `Access-Control-Allow-Headers` to the headers your client actually sends.** Allowing `*` or a broad list of headers enables cross-origin requests to send headers that your server might interpret as trusted (e.g., `X-Forwarded-For`, custom auth headers).
5. **Set a reasonable `Access-Control-Max-Age` for preflight caching.** Preflight requests add latency. Cache them for a reasonable duration (e.g., 3600 seconds) so the browser does not repeat the `OPTIONS` request on every call. Do not set it to zero or omit it — this creates unnecessary load and latency.
6. **Return `Vary: Origin` when the response depends on the `Origin` header.** If different origins get different CORS headers (or no CORS headers), the response varies by origin and must include `Vary: Origin` to prevent CDN or browser cache poisoning.
7. **Do not set CORS headers on responses that should not be cross-origin accessible.** Not every endpoint needs CORS. Internal endpoints, admin panels, and health checks should not include `Access-Control-Allow-Origin` at all. Absence of the header is the default-deny policy.

## Anti-patterns
- Reflecting the `Origin` header back as `Access-Control-Allow-Origin` without checking it against an allowlist. This is functionally equivalent to `*` but worse — it also works with credentials.
- Setting `Access-Control-Allow-Origin: *` and `Access-Control-Allow-Credentials: true` together (the browser rejects this, but developers keep trying and then "fix" it by reflecting the origin).
- Allowing all origins in development (`*`) and forgetting to restrict it before deploying to production.
- Using regex to match origins without anchoring the pattern. `https://evil-example.com` matches an unanchored regex for `example.com`.
- Handling preflight `OPTIONS` requests in the application handler instead of the middleware, leading to inconsistent behavior and missing headers on error responses.
- Setting CORS headers on the application server but having a reverse proxy strip them, or vice versa — the proxy and the application disagreeing on CORS policy.
- Omitting `Vary: Origin`, allowing a CDN to cache a CORS response for one origin and serve it to a different origin.
- Setting `Access-Control-Expose-Headers` to `*`, exposing internal headers (rate-limit details, server internals) to cross-origin JavaScript.

## References
- OWASP ASVS V14.5 — HTTP Request Header Validation
- OWASP REST Security Cheat Sheet (CORS section)
- CWE-942 — Permissive Cross-domain Policy with Untrusted Domains
