---
schema_version: 1
archetype: http/content-security-policy
title: Content Security Policy
summary: Deploying strict CSP headers with nonces and hashes to eliminate XSS execution paths.
applies_to: [csharp, python, go, javascript, typescript, java, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - csp
  - content-security-policy
  - nonce
  - script-src
  - frame-ancestors
  - xss
  - trusted-types
  - report-uri
  - hash
  - directive
related_archetypes:
  - http/xss
  - http/security-headers
references:
  owasp_asvs: V14.4
  owasp_cheatsheet: Content Security Policy Cheat Sheet
  cwe: "1021"
---

# Content Security Policy — Principles

## When this applies
Any application that serves HTML to a browser. CSP is the browser-enforced second line of defense that limits what scripts, styles, images, and frames can execute even after output-encoding failures. It applies equally to server-rendered HTML, single-page applications, hybrid SSR+CSR architectures, and admin panels. If the browser parses your HTML, CSP can protect it. Applications that serve only JSON to non-browser clients still benefit from `X-Content-Type-Options` but do not need a CSP directive-set — CSP is browser-native.

## Architectural placement
CSP is emitted as an HTTP response header — **never** through a `<meta http-equiv>` tag, which cannot express `frame-ancestors`, `sandbox`, or `report-uri` and can be bypassed if injected content appears before the meta tag in the document. Set CSP in a **single centralized middleware** that runs before any handler. Nonces are generated per-request, stored in the request context, and threaded into both the header and any inline `<script>` or `<style>` tags that legitimately need them. Hashes are preferred for static inline content; nonces for dynamic inline content. The nonce approach requires that no response is cached across requests without stripping the nonce from the header.

## Principles
1. **Start with `default-src 'none'` and add only what is needed.** An additive policy is easier to audit than a subtractive one. Every directive you add must be justified. Undeclared directives fall back to `default-src`, so `'none'` as the base means everything is blocked unless explicitly allowed.
2. **Eliminate `'unsafe-inline'` from `script-src`.** It bypasses CSP for every inline script. Use per-request cryptographic nonces (`script-src 'nonce-{random}'`) or SHA-256/384/512 hashes for static inline scripts. Both must accompany `'strict-dynamic'` in modern browsers to allow script-loaded sub-scripts.
3. **Eliminate `'unsafe-eval'` from `script-src`.** It allows `eval()`, `Function()`, `setTimeout(string)`, and `setInterval(string)` — all of which turn XSS payloads from DOM-injection attacks into arbitrary JS execution. If a library requires `eval`, replace the library.
4. **Use `'strict-dynamic'` with nonces to allow dynamic script loading.** `'strict-dynamic'` tells the browser to trust scripts loaded by a nonce-bearing script, eliminating the need for `'self'` or host allowlists. The combination `'nonce-{x}' 'strict-dynamic'` covers modern browsers; include a SHA-256 hash fallback for CSP Level 2.
5. **Set `frame-ancestors 'none'` unless framing is intentionally required.** This is the CSP-native replacement for `X-Frame-Options: DENY` and is honored by all modern browsers. If partner framing is needed, enumerate exact origins: `frame-ancestors https://partner.example.com`.
6. **Set `base-uri 'self'` and `form-action 'self'`.** A `<base>` injection can redirect all relative URLs; an injected form can exfiltrate credentials to an attacker's server. Neither is covered by `default-src`.
7. **Deploy in Report-Only mode first.** `Content-Security-Policy-Report-Only` sends violation reports without blocking. Collect violations for at least one release cycle, fix them, then promote to enforcing. Use `report-to` (Reporting API v1) and retain `report-uri` for older browser compatibility.
8. **Adopt Trusted Types for DOM sinks.** `require-trusted-types-for 'script'` forces all DOM injection APIs (`innerHTML`, `outerHTML`, `document.write`, `eval`, `script.src`) to accept only `TrustedHTML` / `TrustedScript` objects, making DOM XSS impossible to express accidentally.

## Anti-patterns
- Using `script-src 'unsafe-inline'` to unblock a library that needs it — find an alternative or use a hash/nonce for that specific script.
- Listing `*.cdn.example.com` in `script-src` — a wildcard subdomain allowlist is bypassed by any XSS-vulnerable page on that CDN.
- Setting CSP via `<meta http-equiv="Content-Security-Policy">` — `frame-ancestors`, `report-uri`, and `sandbox` are ignored; injected content before the tag can win the race.
- Generating a new nonce by re-using a static value, a predictable counter, or the session ID — nonces must be cryptographically random (min 128 bits) and unique per response.
- Applying CSP only to HTML pages and omitting it from JSON, CSS, or error responses — a misconfigured MIME type on any of these opens MIME-sniffing vectors.
- Using `'unsafe-hashes'` for event handlers (`onclick`, `onload`) instead of refactoring to external scripts — it is a temporary workaround that weakens the policy.
- Ignoring Report-Only violation reports — violations in Report-Only are real bugs and attack signals, not noise.
- Omitting `upgrade-insecure-requests` on HTTPS-only applications that still have some hard-coded HTTP asset URLs.

## References
- OWASP ASVS V14.4 — HTTP Security Headers
- OWASP Content Security Policy Cheat Sheet
- CWE-1021 — Improper Restriction of Rendered UI Layers or Frames
- W3C CSP Level 3 — https://www.w3.org/TR/CSP3/
