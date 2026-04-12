---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: javascript
principles_file: _principles.md
libraries:
  preferred: WebCrypto API (SubtleCrypto)
  acceptable:
    - jose (JWT/JWK operations)
    - node:crypto (Node.js built-in, for non-browser)
  avoid:
    - name: node-rsa
      reason: Unmaintained; lacks OAEP support by default; use node:crypto or WebCrypto.
    - name: jsrsasign
      reason: Large, browser-oriented, known history of low-severity CVEs; prefer WebCrypto/jose.
minimum_versions:
  node: "22.0"
---

# Asymmetric Encryption and Signing — JavaScript

## Library choice
`SubtleCrypto` (the WebCrypto API) is available in all modern browsers and in Node.js 18+ via `globalThis.crypto.subtle`. It is the correct default: hardware-accelerated, spec-defined, and impossible to misuse at the padding level because the API exposes named algorithms rather than raw primitives. For JWT operations (signing, verification, JWKS fetching), `jose` is the ecosystem standard — it uses WebCrypto under the hood and enforces algorithm pinning. On Node.js, `node:crypto` offers equivalent functionality with a slightly different API surface; use it when you need PEM I/O or streaming.

## Reference implementation
```js
// Uses SubtleCrypto — runs in browsers and Node.js 18+

async function generateEcdsaKey() {
  return crypto.subtle.generateKey(
    { name: 'ECDSA', namedCurve: 'P-256' },
    true,               // extractable: false in production if key stays in process
    ['sign', 'verify'],
  );
}

async function sign(data, privateKey) {
  const encoded = new TextEncoder().encode(data);
  const sig = await crypto.subtle.sign(
    { name: 'ECDSA', hash: 'SHA-256' },
    privateKey,
    encoded,
  );
  return new Uint8Array(sig);
}

async function verify(data, signature, publicKey) {
  const encoded = new TextEncoder().encode(data);
  return crypto.subtle.verify(
    { name: 'ECDSA', hash: 'SHA-256' },
    publicKey,
    signature,
    encoded,
  );
}

// Export public key as JWK for distribution
async function exportPublicJwk(keyPair) {
  return crypto.subtle.exportKey('jwk', keyPair.publicKey);
}
```

## Language-specific gotchas
- `SubtleCrypto` methods are all `async`/`Promise`-based. Never `.then()`-chain without handling rejection, and never use `await` inside a `try/catch` that swallows the error — a failed `verify` rejects the promise; an uncaught rejection silently continues execution in older Node.js.
- `generateKey` with `extractable: false` prevents the private key from being exported via `exportKey`. Use this in production; `true` is only for test fixtures or cases where you need to serialize the key.
- The `algorithm` object must match between `generateKey`, `sign`, and `verify`. Passing `{ name: 'ECDSA' }` at verify without `hash: 'SHA-256'` will throw a `DOMException`. Always include `hash`.
- For JWT verification with `jose`: `import { jwtVerify } from 'jose'` then `jwtVerify(token, publicKey, { algorithms: ['ES256'] })`. The `algorithms` option is mandatory — omitting it in older versions permitted `alg: none`.
- RSA-OAEP in WebCrypto: `{ name: 'RSA-OAEP', hash: 'SHA-256' }`. The `hash` must be specified; the default is SHA-1 in some environments, which is weak.
- `ArrayBuffer` vs `Uint8Array`: WebCrypto returns `ArrayBuffer`; most userland code expects `Uint8Array`. Wrap with `new Uint8Array(buffer)` at the boundary.

## Tests to write
- Round-trip: generate key pair, sign data, verify, assert true.
- Wrong-key rejection: sign with key A, verify with key B, assert false.
- Tampered data: sign, change one byte, verify, assert false.
- Algorithm object completeness: assert that calling `sign` without `hash` in the algorithm object throws.
- JWT algorithm pinning: assert that `jwtVerify` rejects a token with an unexpected algorithm.
- Extractable false: generate key with `extractable: false`, assert that `exportKey('jwk', privateKey)` rejects.
