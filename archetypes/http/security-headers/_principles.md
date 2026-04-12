---
schema_version: 1
archetype: http/security-headers
title: HTTP Security Headers
summary: Configuring browser security policies through HTTP response headers.
applies_to: [csharp, python, go]
status: draft
keywords:
  - security-headers
  - csp
  - content-security-policy
  - hsts
  - strict-transport-security
  - x-frame-options
  - x-content-type-options
  - permissions-policy
  - referrer-policy
  - clickjacking
  - framing
  - headers
  - transport
related_archetypes:
  - http/xss
  - http/cors
  - http/csrf
references:
  owasp_asvs: V14.4
  owasp_cheatsheet: HTTP Headers Cheat Sheet
  cwe: "693"
---

# HTTP Security Headers — Principles

## When this applies
Every HTTP response served to a browser. Security headers are not optional extras — they are configuration for the browser's built-in security mechanisms. Without them, the browser falls back to permissive defaults that were designed for compatibility with the 2005 web. This applies to HTML pages, API responses consumed by same-origin JavaScript, static assets, error pages, redirects, and even 204 responses. A missing header is an implicit policy of "allow everything."

## Architectural placement
Security headers are set in a **single middleware** that runs early in the response pipeline, before any handler writes to the response. The middleware applies a default policy to every response and allows per-route overrides only through an explicit, auditable mechanism (not by individual handlers writing headers ad hoc). The header values are defined in configuration, not scattered across controllers. This ensures that a new endpoint automatically inherits the security policy without the developer having to remember to add headers.

## Principles
1. **Content-Security-Policy is the most impactful header.** Start with a strict policy: `default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; font-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`. Relax only what you need, with a documented reason for each directive.
2. **Strict-Transport-Security forces HTTPS.** Set `Strict-Transport-Security: max-age=31536000; includeSubDomains` on every HTTPS response. This tells the browser to never connect over plain HTTP. Do not set it on HTTP responses — it is ignored there, and the redirect itself is the vulnerability HSTS prevents.
3. **X-Content-Type-Options: nosniff is non-negotiable.** It prevents the browser from MIME-sniffing a response away from the declared Content-Type. Without it, a JSON response can be reinterpreted as HTML.
4. **X-Frame-Options: DENY prevents clickjacking.** If your site should never be framed, set `DENY`. If it must be framed by same-origin pages, use `SAMEORIGIN`. CSP's `frame-ancestors` is the modern replacement but X-Frame-Options provides fallback for older browsers.
5. **Referrer-Policy controls information leakage.** Set `Referrer-Policy: strict-origin-when-cross-origin` at minimum. This prevents full URL paths (which may contain tokens or IDs) from leaking to third-party sites while preserving the origin for same-origin requests.
6. **Permissions-Policy disables unneeded browser features.** Explicitly disable features your application does not use: `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`. This reduces the attack surface if an XSS vulnerability is exploited.
7. **Remove server-identifying headers.** `Server`, `X-Powered-By`, `X-AspNet-Version`, and similar headers reveal technology stack details that help attackers select exploits. Remove or suppress them.
8. **Use Report-Only mode to test CSP changes.** `Content-Security-Policy-Report-Only` lets you deploy a new policy and observe violations without breaking functionality. Monitor the reports, fix violations, then promote to enforcing mode.

## Anti-patterns
- Setting CSP to `default-src *` or `script-src 'unsafe-inline' 'unsafe-eval'` — this disables every protection CSP offers.
- Adding security headers only to HTML responses and not to API responses. A missing `X-Content-Type-Options` on a JSON endpoint can enable MIME-sniffing attacks.
- Setting HSTS with a short `max-age` (seconds or minutes) during "testing" and forgetting to increase it. A short max-age provides no meaningful protection.
- Configuring headers in individual controllers instead of a centralized middleware. The first controller someone writes without headers creates a gap.
- Setting `X-Frame-Options: ALLOW-FROM https://partner.com` — this directive is not supported by modern browsers. Use CSP `frame-ancestors` instead.
- Deploying a strict CSP without Report-Only testing first, breaking production, and then removing CSP entirely instead of fixing the violations.
- Setting `Referrer-Policy: no-referrer` globally, which breaks CSRF `Referer` validation in some frameworks.
- Trusting a reverse proxy to add security headers without verifying. If the proxy configuration changes, headers silently disappear.

## References
- OWASP ASVS V14.4 — HTTP Security Headers
- OWASP HTTP Headers Cheat Sheet
- CWE-693 — Protection Mechanism Failure
