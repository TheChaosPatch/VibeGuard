---
schema_version: 1
archetype: crypto/hashing-integrity
language: javascript
principles_file: _principles.md
libraries:
  preferred: WebCrypto API (SubtleCrypto)
  acceptable:
    - node:crypto (createHmac, createHash — Node.js built-in)
  avoid:
    - name: crypto-js
      reason: Unmaintained since 2021; use SubtleCrypto or node:crypto.
    - name: md5 / sha1 npm packages
      reason: Broken algorithms; SubtleCrypto does not expose MD5 by design.
    - name: Buffer comparison with === or ==
      reason: Not constant-time; use crypto.timingSafeEqual (Node.js) or a subtle.verify pattern.
minimum_versions:
  node: "22.0"
---

# Hashing and Data Integrity — JavaScript

## Library choice
In the browser and in modern Node.js (18+), `SubtleCrypto` (`globalThis.crypto.subtle`) is the correct choice: `subtle.sign('HMAC', key, data)` and `subtle.verify('HMAC', key, sig, data)` — the `verify` path is inherently constant-time because it runs in the engine's crypto implementation. On Node.js, `node:crypto` also provides `createHmac` / `timingSafeEqual` for synchronous use cases and streams. SubtleCrypto does not expose MD5 — this is intentional; any library that lets you request MD5 via SubtleCrypto is wrapping a non-standard path.

## Reference implementation
```js
// SubtleCrypto — browser and Node.js 18+

async function importHmacKey(rawKey) {
  return crypto.subtle.importKey(
    'raw', rawKey,
    { name: 'HMAC', hash: 'SHA-256' },
    false,          // not extractable in production
    ['sign', 'verify'],
  );
}

async function computeHmac(cryptoKey, data) {
  const encoded = typeof data === 'string' ? new TextEncoder().encode(data) : data;
  const sig = await crypto.subtle.sign('HMAC', cryptoKey, encoded);
  return new Uint8Array(sig);
}

async function verifyHmac(cryptoKey, data, expectedTag) {
  const encoded = typeof data === 'string' ? new TextEncoder().encode(data) : data;
  // subtle.verify is constant-time by spec
  return crypto.subtle.verify('HMAC', cryptoKey, expectedTag, encoded);
}

async function sha256Digest(data) {
  const encoded = typeof data === 'string' ? new TextEncoder().encode(data) : data;
  const buf = await crypto.subtle.digest('SHA-256', encoded);
  return new Uint8Array(buf);
}
```

## Language-specific gotchas
- `subtle.verify('HMAC', key, signature, data)` is the correct constant-time verification path. Do not compute a tag and compare with `===` or `Buffer.compare` — those are not constant-time. `crypto.timingSafeEqual` in `node:crypto` is constant-time but requires both buffers to be the same length.
- `importKey` with `extractable: false` prevents the raw HMAC key bytes from being exported later. Always use `false` for production keys; `true` only for test fixtures.
- The key usage array `['sign', 'verify']` is required at import time. A key imported with only `['sign']` will throw a `DOMException` when passed to `subtle.verify` — test this explicitly.
- `subtle.sign` and `subtle.verify` return `Promise<ArrayBuffer>`. Wrap in `new Uint8Array(...)` at the boundary to get a `Uint8Array` that supports typed operations and comparison.
- In Node.js, `node:crypto.createHmac('sha256', key).update(data).digest()` is synchronous and suitable for non-browser server code where async overhead matters. Pair with `crypto.timingSafeEqual(actual, expected)` for comparison — both buffers must be the same length.
- Do not pass raw key bytes as a `string` to `importKey`. The `rawKey` parameter must be an `ArrayBuffer` or `TypedArray`. Convert: `new TextEncoder().encode(keyString)` — but note that a string-derived key is rarely a proper 256-bit random key; the key should come from the secrets store as bytes.

## Tests to write
- Round-trip HMAC: import key, sign data, verify with same key and data, assert true.
- Wrong-key rejection: sign with key A, verify with key B, assert false.
- Tampered data: sign, modify one byte, verify, assert false.
- SHA-256 consistency: assert digest of a known input matches a reference vector.
- Key usage enforcement: import key with `['sign']` only, assert that `verify` rejects with `DOMException`.
- Extractable false: import key with `extractable: false`, assert that `exportKey` rejects.
