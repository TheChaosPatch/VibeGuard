---
schema_version: 1
archetype: crypto/hashing-integrity
language: typescript
principles_file: _principles.md
libraries:
  preferred: WebCrypto API (SubtleCrypto) with typed wrappers
  acceptable:
    - node:crypto (createHmac, timingSafeEqual — Node.js built-in)
  avoid:
    - name: crypto-js
      reason: Unmaintained; no TypeScript-first design; use SubtleCrypto.
    - name: === for Uint8Array HMAC comparison
      reason: Compares object references, not contents — use subtle.verify or timingSafeEqual.
minimum_versions:
  node: "22.0"
  typescript: "5.4"
---

# Hashing and Data Integrity — TypeScript

## Library choice
Same runtime as JavaScript — `SubtleCrypto` is the correct default. TypeScript adds value here by letting you type the key handle as `CryptoKey` rather than `any`, and by making the `'HMAC'` algorithm string a literal that can be validated by the type system. The `subtle.verify` path is inherently constant-time. For Node.js-only server code, `node:crypto` is a fine alternative with `timingSafeEqual` for comparison. Use `Buffer` / `Uint8Array` for MAC values, never `string`.

## Reference implementation
```ts
const HMAC_ALGO = { name: 'HMAC', hash: 'SHA-256' } as const;

export async function importHmacKey(rawKey: BufferSource): Promise<CryptoKey> {
  return crypto.subtle.importKey('raw', rawKey, HMAC_ALGO, false, [
    'sign',
    'verify',
  ]);
}

export async function computeHmac(
  key: CryptoKey,
  data: BufferSource,
): Promise<Uint8Array> {
  const buf = await crypto.subtle.sign('HMAC', key, data);
  return new Uint8Array(buf);
}

export async function verifyHmac(
  key: CryptoKey,
  data: BufferSource,
  expectedTag: BufferSource,
): Promise<boolean> {
  // subtle.verify is constant-time by spec — the only acceptable comparator
  return crypto.subtle.verify('HMAC', key, expectedTag, data);
}

export async function sha256Digest(data: BufferSource): Promise<Uint8Array> {
  const buf = await crypto.subtle.digest('SHA-256', data);
  return new Uint8Array(buf);
}
```

## Language-specific gotchas
- `CryptoKey` is an opaque type — you cannot inspect whether a `CryptoKey` is an HMAC key or an ECDSA key at runtime without checking `key.algorithm.name`. Define a branded type or a factory function that returns `HmacKey & CryptoKey` to enforce intent at the TypeScript level.
- `as const satisfies HmacImportParams` on the algorithm object lets the compiler validate that `name` and `hash` are correct property names for the interface. Without `satisfies`, a typo like `hash: 'sha-256'` (lowercase) is only caught at runtime.
- `BufferSource` = `ArrayBuffer | ArrayBufferView`. Accept `BufferSource` at your public API boundary; internally always use `Uint8Array` for manipulation. Avoid `string` — encoding bugs are the most common source of HMAC mismatches between services.
- In Node.js, `import { timingSafeEqual } from 'node:crypto'` provides constant-time comparison for `Buffer` and `TypedArray`. Both arguments must have the same `byteLength` or `timingSafeEqual` throws a `TypeError`. Check lengths first: `if (a.length !== b.length) return false; return timingSafeEqual(a, b)`.
- `subtle.verify` argument order is `(algorithm, key, signature, data)` — note that `signature` comes before `data`. This is the opposite of `subtle.sign`'s `(algorithm, key, data)`. A swapped argument is a silent bug that always returns false.
- TypeScript's `readonly` modifier on the algorithm constant (`as const`) prevents accidental mutation of the shared algorithm object across calls. Without it, someone could do `HMAC_ALGO.hash = 'SHA-1'` and affect all subsequent calls.

## Tests to write
- Round-trip: import key, sign, verify with same key and data, assert true.
- Wrong-key rejection: sign with key A, verify with key B, assert false.
- Tampered data: sign, mutate one byte of the data buffer, verify, assert false.
- Argument order: assert that swapping `signature` and `data` in `subtle.verify` returns false (not throws).
- Type-level: assert that passing a `string` to `computeHmac(key, data)` produces a TypeScript compilation error (`@ts-expect-error`).
- SHA-256 known vector: `sha256Digest(new TextEncoder().encode(''))` matches the known empty-string SHA-256 hash.
