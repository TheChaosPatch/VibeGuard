---
schema_version: 1
archetype: http/content-security-policy
language: javascript
principles_file: _principles.md
libraries:
  preferred: helmet (Express)
  acceptable:
    - next/headers (Next.js App Router)
    - fastify-helmet (Fastify)
  avoid:
    - name: csurf CSP helpers
      reason: csurf is deprecated; it provides CSRF tokens, not CSP nonce management.
minimum_versions:
  node: "22.0"
---

# Content Security Policy — JavaScript

## Library choice
`helmet` is the standard security-header middleware for Express and compatible frameworks. Use `helmet.contentSecurityPolicy()` with a `nonce` generator function to emit a fresh nonce per request. For Next.js App Router, set CSP in `middleware.ts` using `crypto.randomBytes` and the `NextResponse.headers` API. Fastify users should use `@fastify/helmet`, which wraps the same `helmet` core.

## Reference implementation
```js
// Express — app.js
import express from "express";
import helmet from "helmet";
import crypto from "node:crypto";

const app = express();

app.use((req, res, next) => {
    res.locals.nonce = crypto.randomBytes(16).toString("base64");
    next();
});

app.use((req, res, next) => {
    helmet.contentSecurityPolicy({
        directives: {
            defaultSrc: ["'none'"],
            scriptSrc: [
                "'strict-dynamic'",
                (req, res) => `'nonce-${res.locals.nonce}'`,
            ],
            styleSrc: ["'self'", (req, res) => `'nonce-${res.locals.nonce}'`],
            imgSrc: ["'self'", "data:"],
            connectSrc: ["'self'"],
            fontSrc: ["'self'"],
            formAction: ["'self'"],
            frameAncestors: ["'none'"],
            baseUri: ["'self'"],
            upgradeInsecureRequests: [],
        },
    })(req, res, next);
});

// In templates (e.g. EJS): <script nonce="<%= nonce %>">...</script>
app.set("view engine", "ejs");
app.get("/", (req, res) => res.render("index", { nonce: res.locals.nonce }));

app.listen(3000);
```

## Language-specific gotchas
- `helmet` 7+ removed legacy defaults — `contentSecurityPolicy` must be explicitly configured; calling `helmet()` without options produces a baseline policy but not a strict nonce-based one.
- The nonce generator in `helmet.contentSecurityPolicy` is a function `(req, res) => string`. Returning a static string breaks the per-request guarantee — always derive it from `res.locals.nonce` which was set by the preceding middleware.
- `crypto.randomBytes(16).toString("base64")` returns 24 characters (128 bits of entropy). `"hex"` encoding is also acceptable; `"base64url"` is slightly cleaner for HTTP headers.
- Next.js App Router: set the nonce in `middleware.ts` before the response, store in a cookie or header for the Server Component to read, and pass it via `generateMetadata` or layout props to `<script nonce>`. CSP via `next.config.js` `headers()` does not support dynamic nonces — middleware is required.
- Do not set `'unsafe-inline'` in `styleSrc` — use style nonces or move all critical styles to external sheets referenced by `'self'`.
- Response caching (e.g., via `express-cache-controller`) caches the nonce. Disable caching for HTML responses or set `Vary: *`.

## Tests to write
- Supertest: `GET /` response has `Content-Security-Policy` header matching `/nonce-[A-Za-z0-9+/]{24}/`.
- Two requests produce different nonce values.
- Rendered HTML body contains `nonce="X"` matching the nonce in the header.
- `frame-ancestors 'none'` present in policy.
- Response without a route handler (404) still has CSP header (middleware fires regardless of route match).
