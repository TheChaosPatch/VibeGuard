---
schema_version: 1
archetype: http/xss
title: Cross-Site Scripting Defense
summary: Preventing untrusted data from executing as code in the browser.
applies_to: [csharp, python, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - xss
  - cross-site
  - scripting
  - output-encoding
  - sanitize
  - escape
  - html
  - template
  - injection
  - dom
  - csp
  - content-security-policy
  - reflected
  - stored
related_archetypes:
  - io/input-validation
  - http/security-headers
  - http/csrf
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: Cross-Site Scripting Prevention Cheat Sheet
  cwe: "79"
---

# Cross-Site Scripting Defense — Principles

## When this applies
Any time your server produces HTML, JSON-within-HTML, or JavaScript that includes data originating outside the current trust boundary. The obvious cases: rendering a user's display name in a page, reflecting a search query back into the results heading, embedding user-generated content in a CMS. The non-obvious cases: a JSON blob rendered into a `<script>` tag for client-side hydration, an error message that echoes a URL parameter, an SVG upload rendered inline, a Markdown-to-HTML converter, an email template that includes user input, a WebSocket message rendered into the DOM without escaping. If untrusted data reaches the browser in a context where the browser interprets it as code, you have XSS.

## Architectural placement
Output encoding happens at the **template layer**, not at input time. Data flows through the system as plain text and is encoded only at the moment it is interpolated into a specific output context (HTML body, HTML attribute, JavaScript string, URL parameter, CSS value). The template engine is the last line of defense and must auto-escape by default. Disabling auto-escape requires an explicit, auditable opt-out — a function like `markSafe()` or `@Html.Raw()` — that reviewers can grep for and challenge. A Content Security Policy header provides defense-in-depth by restricting what the browser will execute even if encoding fails.

## Principles
1. **Auto-escape by default.** Choose a template engine that HTML-encodes all interpolated values unless explicitly marked safe. This makes "secure" the path of least resistance and "unsafe" the one that requires effort and review.
2. **Encode for the output context, not generically.** HTML body, HTML attribute, JavaScript string, URL parameter, and CSS value each have different dangerous characters and different encoding rules. `&lt;` is correct for HTML body but wrong inside a JavaScript string literal. Use context-aware encoding functions.
3. **Never construct HTML by string concatenation.** `"<div>" + userName + "</div>"` bypasses every encoding safeguard. Build HTML through the template engine or a DOM builder API, never through string operations.
4. **Treat `innerHTML`, `document.write`, and `v-html` as code injection points.** Any API that parses a string as HTML is a DOM XSS vector. Use `textContent`, `innerText`, or framework-safe bindings (`{{ }}` in Angular, `{}` in React) that set text, not markup.
5. **Sanitize rich content with an allowlist parser, not a denylist regex.** If you must accept HTML (markdown comments, WYSIWYG editors), parse it into a DOM tree and keep only allowed tags and attributes. Strip everything else. Never regex-strip `<script>` tags — there are hundreds of bypass vectors.
6. **Deploy Content Security Policy as defense-in-depth.** CSP does not replace encoding, but it limits blast radius. At minimum: `script-src 'self'` with no `'unsafe-inline'` and no `'unsafe-eval'`. Use nonces or hashes for inline scripts that are genuinely needed.
7. **Isolate user-generated content in sandboxed iframes.** If you render untrusted HTML that cannot be fully sanitized (embedded widgets, third-party content), serve it from a separate origin in a sandboxed iframe with `sandbox="allow-scripts"` but no `allow-same-origin`.

## Anti-patterns
- Escaping input on the way in and storing "safe HTML" in the database. Now every consumer trusts the writer's escaping, and you cannot re-encode for a different context.
- Relying on `Content-Type: application/json` to prevent XSS in API responses. Older browsers and certain embedding contexts (JSONP, `<script src>`) will still execute it.
- Stripping `<script>` with regex and calling it safe. `<img onerror=...>`, `<svg onload=...>`, `<body onpageshow=...>`, and dozens of other vectors remain.
- Using `@Html.Raw()` / `| safe` / `noescape` to "fix" double-encoding instead of finding the real encoding bug.
- Embedding user data in a `<script>` block without JSON-encoding and assigning to a variable. `</script><script>alert(1)` breaks out of the block.
- Setting CSP to `script-src 'unsafe-inline' 'unsafe-eval'` — this disables every protection CSP offers for XSS.
- Trusting client-side validation or WAF rules as the XSS defense instead of server-side output encoding.
- Allowing `javascript:` URLs in `href` or `src` attributes without validation.

## References
- OWASP ASVS V5.3 — Output Encoding and Injection Prevention
- OWASP Cross-Site Scripting Prevention Cheat Sheet
- CWE-79 — Improper Neutralization of Input During Web Page Generation
