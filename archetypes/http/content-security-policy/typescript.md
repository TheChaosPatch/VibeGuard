---
schema_version: 1
archetype: http/content-security-policy
language: typescript
principles_file: _principles.md
libraries:
  preferred: helmet (Express + TypeScript types via @types/helmet)
  acceptable:
    - next/headers with crypto.randomBytes (Next.js App Router)
    - "@fastify/helmet (Fastify)"
  avoid:
    - name: Meta tag injection
      reason: frame-ancestors and report-uri are ignored; cannot serve as the sole CSP mechanism.
minimum_versions:
  node: "22.0"
  typescript: "5.4"
---

# Content Security Policy — TypeScript

## Library choice
`helmet` ships its own TypeScript types (`helmet` package, types bundled since v7). For Express projects, use `helmet.contentSecurityPolicy()` with a typed nonce middleware. For Next.js App Router, implement CSP in `middleware.ts` using the native `crypto` module. `@fastify/helmet` provides Fastify-native types. The implementation pattern is identical to the JavaScript archetype; TypeScript adds type safety for the middleware signature and the nonce shape.

## Reference implementation
```typescript
import { randomBytes } from "node:crypto";
import { type Request, type Response, type NextFunction } from "express";
import helmet from "helmet";

declare module "express-serve-static-core" {
    interface Locals { nonce: string; }
}

export function nonceMiddleware(req: Request, res: Response, next: NextFunction): void {
    res.locals.nonce = randomBytes(16).toString("base64");
    next();
}

export function cspMiddleware(req: Request, res: Response, next: NextFunction): void {
    const nonceFn = () => `'nonce-${res.locals.nonce}'`;
    helmet.contentSecurityPolicy({
        directives: {
            defaultSrc: ["'none'"],
            scriptSrc: ["'strict-dynamic'", nonceFn],
            styleSrc: ["'self'", nonceFn],
            imgSrc: ["'self'", "data:"],
            connectSrc: ["'self'"],
            fontSrc: ["'self'"],
            formAction: ["'self'"],
            frameAncestors: ["'none'"],
            baseUri: ["'self'"],
            upgradeInsecureRequests: [],
        },
    })(req, res, next);
}
```

```typescript
// Next.js App Router — middleware.ts
import { type NextRequest, NextResponse } from "next/server";
import { randomBytes } from "node:crypto";

export function middleware(request: NextRequest): NextResponse {
    const nonce = randomBytes(16).toString("base64");
    const policy = [
        `default-src 'none'`,
        `script-src 'nonce-${nonce}' 'strict-dynamic'`,
        `style-src 'nonce-${nonce}' 'self'`,
        `img-src 'self' data:`,
        `connect-src 'self'`,
        `frame-ancestors 'none'`,
        `base-uri 'self'`,
        `form-action 'self'`,
    ].join("; ");

    const response = NextResponse.next({
        request: { headers: new Headers({ "x-nonce": nonce }) },
    });
    response.headers.set("Content-Security-Policy", policy);
    return response;
}
```

## Language-specific gotchas
- Declare `nonce` on `express-serve-static-core.Locals` (as shown) so TypeScript enforces the property's presence throughout the middleware chain.
- The nonce generator callback in `helmet.contentSecurityPolicy.directives` is typed as `(req: Request, res: Response) => string`. The return value must not be async — `randomBytes` synchronous API is correct here.
- Next.js `middleware.ts` runs in the Edge Runtime — `node:crypto` is available via the Web Crypto polyfill. Use `crypto.getRandomValues(new Uint8Array(16))` if targeting strict Edge Runtime compatibility.
- `@types/helmet` was merged into the `helmet` package itself at v7 — do not install `@types/helmet` separately; it conflicts.
- TypeScript strict mode (`"strict": true`) will catch uninitialized `res.locals.nonce` if you skip `nonceMiddleware` — the module augmentation makes this a compile-time check.
- In Fastify + TypeScript, decorate the `reply` object: `fastify.decorateReply("cspNonce", "")` and set it in a `preHandler` hook before `@fastify/helmet` applies the CSP.

## Tests to write
- Type-level: `res.locals.nonce` compiles as `string` without casting, confirming the module augmentation.
- Supertest: response has `Content-Security-Policy` with a valid base64 nonce.
- Two requests yield different nonce values.
- Next.js middleware test: `NextResponse` has both `Content-Security-Policy` header and `x-nonce` request header with matching values.
- `frame-ancestors 'none'` present in all CSP responses.
