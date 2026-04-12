---
schema_version: 1
archetype: auth/jwt-handling
language: javascript
principles_file: _principles.md
libraries:
  preferred: jose
  acceptable:
    - jsonwebtoken
  avoid:
    - name: jwt-simple
      reason: Unmaintained; no built-in claim validation.
    - name: Manual atob() decode
      reason: Reads payload without signature verification.
minimum_versions:
  node: "22"
---

# JWT Handling — JavaScript

## Library choice
`jose` (by Panva) is a modern, actively maintained JOSE implementation that uses the native Web Crypto API — it works in Node.js, Deno, Cloudflare Workers, and browsers without polyfills. It supports JWS, JWE, JWK, and JWKS out of the box. `jsonwebtoken` is the historically dominant library and remains acceptable in Node.js-only contexts, but it does not support Web Crypto or edge runtimes. Prefer `jose` for new projects.

## Reference implementation
```js
import { SignJWT, jwtVerify, createRemoteJWKSet } from 'jose';
import { createPrivateKey } from 'node:crypto';
import { readFileSync } from 'node:fs';

const ISSUER   = 'https://auth.example.com';
const AUDIENCE = 'https://api.example.com';
const ALG      = 'RS256';
const TTL      = '15m';

const privateKey = createPrivateKey(readFileSync('/run/secrets/jwt_private.pem'));

// Issuance
export async function issueToken(subject, claims = {}) {
    return new SignJWT({ ...claims })
        .setProtectedHeader({ alg: ALG })
        .setSubject(subject)
        .setIssuer(ISSUER)
        .setAudience(AUDIENCE)
        .setIssuedAt()
        .setExpirationTime(TTL)
        .sign(privateKey);
}

// Validation — resolve keys from JWKS endpoint
const JWKS = createRemoteJWKSet(new URL('https://auth.example.com/.well-known/jwks.json'));

export async function validateToken(token) {
    const { payload } = await jwtVerify(token, JWKS, {
        issuer:    ISSUER,
        audience:  AUDIENCE,
        algorithms: [ALG],
    });
    return payload;
}
```

## Language-specific gotchas
- `jwtVerify` from `jose` rejects `alg: none` by default and validates `exp`, `nbf`, `iss`, and `aud` when those options are supplied. Omitting `issuer` or `audience` silently skips that check.
- `createRemoteJWKSet` caches the JWKS document and re-fetches on `kid` miss — this is the correct rotation-friendly approach. Do not hard-code the public key unless you explicitly own both issuer and verifier.
- Never use `jose`'s `decodeJwt` (or `jsonwebtoken`'s `decode`) for anything beyond diagnostics. These functions skip signature verification.
- In Express middleware, extract the token from `Authorization: Bearer <token>` with a split, not a regex that might backtrack on adversarial input.
- `jsonwebtoken`'s `verify` is synchronous and blocks the event loop for RSA verification on large payloads. Use `jose` on performance-sensitive paths.

## Tests to write
- `validateToken(await issueToken('u1'))` resolves with `sub === 'u1'`.
- Expired token → `jwtVerify` throws `JWTExpired`.
- Token signed with a different key → throws `JWSSignatureVerificationFailed`.
- Token with `alg: none` → throws `JOSENotSupported` or equivalent.
- Missing `aud` claim → throws `JWTClaimValidationFailed`.
