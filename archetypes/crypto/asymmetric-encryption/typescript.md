---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: typescript
principles_file: _principles.md
libraries:
  preferred: WebCrypto API (SubtleCrypto) with typed wrappers
  acceptable:
    - jose (JWT/JWK, typed)
    - node:crypto (Node.js built-in)
  avoid:
    - name: node-rsa
      reason: Unmaintained; no OAEP default; no TypeScript types; use node:crypto or WebCrypto.
    - name: Any library that accepts algorithm as a plain string without type-checking
      reason: Defeats the purpose of TypeScript's type safety over algorithm selection.
minimum_versions:
  node: "22.0"
  typescript: "5.4"
---

# Asymmetric Encryption and Signing — TypeScript

## Library choice
Same runtime as JavaScript — `SubtleCrypto` is the correct default, and `jose` is the standard for JWT/JWK work. TypeScript adds algorithm-string types (`AlgorithmIdentifier`, `EcKeyGenParams`, etc.) that catch mismatched algorithm names at compile time. Prefer defining `const ALGORITHM = { name: 'ECDSA', namedCurve: 'P-256' } as const satisfies EcKeyGenParams` so the type system validates the object shape. The `jose` package ships its own TypeScript types and is the easiest path to typed, algorithm-pinned JWT verification.

## Reference implementation
```ts
const SIGN_ALGO = { name: 'ECDSA', namedCurve: 'P-256' } as const;
const HASH_ALGO = { name: 'ECDSA', hash: 'SHA-256' } as const;

export async function generateEcdsaKeyPair(): Promise<CryptoKeyPair> {
  return crypto.subtle.generateKey(SIGN_ALGO, false, ['sign', 'verify']);
}

export async function sign(
  data: BufferSource,
  privateKey: CryptoKey,
): Promise<Uint8Array> {
  const buf = await crypto.subtle.sign(HASH_ALGO, privateKey, data);
  return new Uint8Array(buf);
}

export async function verify(
  data: BufferSource,
  signature: BufferSource,
  publicKey: CryptoKey,
): Promise<boolean> {
  return crypto.subtle.verify(HASH_ALGO, publicKey, signature, data);
}

export async function exportPublicJwk(pair: CryptoKeyPair): Promise<JsonWebKey> {
  return crypto.subtle.exportKey('jwk', pair.publicKey);
}

export async function importPublicJwk(jwk: JsonWebKey): Promise<CryptoKey> {
  return crypto.subtle.importKey('jwk', jwk, SIGN_ALGO, true, ['verify']);
}
```

## Language-specific gotchas
- `CryptoKey` is opaque — it carries no runtime type information beyond `key.type` (`"public"` | `"private"` | `"secret"`) and `key.usages`. A `CryptoKey` for ECDSA `sign` will throw a `DOMException` if passed to an RSA-OAEP decrypt operation. Write thin wrapper types (`EcdsaSigningKey`, `EcdsaVerifyingKey`) as branded types to prevent accidental key misuse at the TypeScript level.
- `as const satisfies EcKeyGenParams` lets the compiler validate the algorithm object without widening the type. Without `satisfies`, `{ name: 'ECDSA', namedCurve: 'P-256' }` is just `object` when passed to `generateKey`.
- Promise rejection from `verify` is different from a `false` return. If `verify` rejects, the key type, algorithm, or data encoding is wrong — do not conflate that with a bad signature.
- For JWT with `jose`: `import { jwtVerify, type JWTPayload } from 'jose'`. Pass the `algorithms` option as `['ES256']`. The return type is `{ payload: JWTPayload; protectedHeader: ... }` — destructure immediately and do not pass `payload` through application layers without narrowing it.
- `BufferSource` (= `ArrayBuffer | ArrayBufferView`) is the correct parameter type for WebCrypto data inputs. Accepting `string` at the boundary forces an explicit `new TextEncoder().encode(str)` call, which makes encoding decisions visible and auditable.
- In Node.js, `globalThis.crypto` is available since Node 19 without an import. In Node 18, use `import { webcrypto } from 'node:crypto'` and assign `const { subtle } = webcrypto`.

## Tests to write
- Round-trip: generate pair, sign a `Uint8Array`, verify with public key, assert true.
- Wrong-key rejection: sign with key A, verify with key B, assert false.
- Tampered payload: sign, flip a bit in the signature buffer, verify, assert false.
- Key usage enforcement: assert that using the private key for `verify` throws (wrong usage set).
- JWT algorithm pinning: assert `jwtVerify` with `algorithms: ['ES256']` rejects an `RS256` token.
- Type safety: assert that TypeScript compilation fails when passing `CryptoKeyPair.publicKey` to `sign` (requires compile-time test with `@ts-expect-error`).
