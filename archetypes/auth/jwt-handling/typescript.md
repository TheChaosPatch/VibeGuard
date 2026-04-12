---
schema_version: 1
archetype: auth/jwt-handling
language: typescript
principles_file: _principles.md
libraries:
  preferred: jose
  acceptable:
    - jsonwebtoken
  avoid:
    - name: jwt-decode
      reason: Only decodes — no signature verification. Never use for authentication.
    - name: Manual atob() decode
      reason: Reads payload without signature verification.
minimum_versions:
  node: "22"
  typescript: "5.4"
---

# JWT Handling — TypeScript

## Library choice
`jose` ships full TypeScript types and uses the Web Crypto API, making it compatible with Node.js, Deno, Bun, and edge runtimes. It is the preferred choice for new TypeScript projects. `jsonwebtoken` works in Node.js and has `@types/jsonwebtoken` for type safety, but lacks Web Crypto support.

## Reference implementation
```typescript
import { SignJWT, jwtVerify, createRemoteJWKSet, type JWTPayload } from 'jose';
import { createPrivateKey } from 'node:crypto';
import { readFileSync } from 'node:fs';

const ISSUER   = 'https://auth.example.com' as const;
const AUDIENCE = 'https://api.example.com' as const;
const ALG      = 'RS256' as const;

interface AppClaims extends JWTPayload {
    roles: string[];
}

const privateKey = createPrivateKey(readFileSync('/run/secrets/jwt_private.pem'));
const JWKS = createRemoteJWKSet(new URL('https://auth.example.com/.well-known/jwks.json'));

export async function issueToken(subject: string, roles: string[]): Promise<string> {
    return new SignJWT({ roles } satisfies Pick<AppClaims, 'roles'>)
        .setProtectedHeader({ alg: ALG })
        .setSubject(subject)
        .setIssuer(ISSUER)
        .setAudience(AUDIENCE)
        .setIssuedAt()
        .setExpirationTime('15m')
        .sign(privateKey);
}

export async function validateToken(token: string): Promise<AppClaims> {
    const { payload } = await jwtVerify<AppClaims>(token, JWKS, {
        issuer:     ISSUER,
        audience:   AUDIENCE,
        algorithms: [ALG],
    });
    return payload;
}
```

## Language-specific gotchas
- `jwtVerify<AppClaims>` narrows the return type but does **not** perform runtime validation of custom claims like `roles`. Use a schema validator (Zod, `@sinclair/typebox`) on the returned payload to ensure the shape is correct before trusting it.
- The `JWTPayload` standard fields (`exp`, `iat`, `sub`, etc.) are all `number | undefined` — do not assume they are set unless you pass `requiredClaims` or verify yourself.
- TypeScript's structural typing means an object that satisfies `AppClaims` at the type level may still have extra unexpected fields at runtime. Validate with a schema library before placing the payload on a request context object.
- Use `as const` on algorithm and issuer strings so TypeScript narrows them to literal types, which satisfies `jose`'s strict `Algorithm` union type parameter without casts.
- `jose`'s errors are all in the `jose` namespace: `JWTExpired`, `JWTInvalid`, `JWSSignatureVerificationFailed`. Catch them specifically in middleware; do not catch `Error` broadly and swallow the reason.

## Tests to write
- `validateToken(await issueToken('u1', ['admin']))` resolves with `sub === 'u1'` and `roles` containing `'admin'`.
- Expired token → `jwtVerify` throws `JWTExpired`.
- Token with wrong key → throws `JWSSignatureVerificationFailed`.
- Payload missing `roles` → schema validation step throws before the claim is used.
- `alg: none` in header → throws before any key material is consulted.
