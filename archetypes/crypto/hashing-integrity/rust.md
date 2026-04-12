---
schema_version: 1
archetype: crypto/hashing-integrity
language: rust
principles_file: _principles.md
libraries:
  preferred: hmac + sha2 (RustCrypto)
  acceptable:
    - ring (ring::hmac + ring::digest)
    - aws-lc-rs
  avoid:
    - name: md5 crate
      reason: MD5 is broken for collision resistance; do not use for integrity.
    - name: sha1 crate
      reason: SHA-1 collision attacks are practical; deprecated.
    - name: "== on Vec<u8> or &[u8] for MAC comparison"
      reason: Not constant-time; use subtle::ConstantTimeEq.
minimum_versions:
  rust: "1.78"
---

# Hashing and Data Integrity — Rust

## Library choice
The `hmac` and `sha2` crates from the RustCrypto project are the idiomatic choices: pure Rust, audited, and composable via the `digest` trait. `hmac::Hmac<Sha256>` is the type-level combination of the two. Constant-time comparison is provided by the `subtle` crate's `ConstantTimeEq` trait — this is a transitive dependency of most RustCrypto crates and the correct comparison primitive. The `ring` crate is an equally valid alternative backed by BoringSSL-derived primitives; use it if you need FIPS adjacency or are already using `ring` for TLS.

## Reference implementation
```rust
use hmac::{Hmac, Mac};
use sha2::Sha256;
use subtle::ConstantTimeEq;

type HmacSha256 = Hmac<Sha256>;

const WEBHOOK_PREFIX: &[u8] = b"webhook-v1:";

pub fn compute_hmac(key: &[u8], data: &[u8]) -> Vec<u8> {
    assert!(key.len() >= 32, "HMAC key must be at least 32 bytes");
    let mut mac = HmacSha256::new_from_slice(key)
        .expect("HMAC accepts any key length >= 1");
    mac.update(data);
    mac.finalize().into_bytes().to_vec()
}

pub fn verify_hmac(key: &[u8], data: &[u8], expected: &[u8]) -> bool {
    let actual = compute_hmac(key, data);
    // ConstantTimeEq from the `subtle` crate — not short-circuiting
    actual.ct_eq(expected).into()
}

pub fn sha256_digest(data: &[u8]) -> [u8; 32] {
    use sha2::Digest;
    sha2::Sha256::digest(data).into()
}

pub fn webhook_tag(key: &[u8], payload: &[u8]) -> Vec<u8> {
    let mut prefixed = WEBHOOK_PREFIX.to_vec();
    prefixed.extend_from_slice(payload);
    compute_hmac(key, &prefixed)
}
```

## Language-specific gotchas
- `hmac::Mac::verify_slice` (available in `hmac` 0.12+) performs constant-time verification internally — it is a shorter alternative to calling `compute_hmac` + `ct_eq`. Use it when available: `HmacSha256::new_from_slice(key)?.chain_update(data).verify_slice(expected).is_ok()`.
- `subtle::ConstantTimeEq` is the correct comparator for MAC bytes. `Vec<u8>` and `&[u8]` implement `PartialEq` via element comparison that is explicitly not guaranteed to be constant-time by the Rust reference. Import `use subtle::ConstantTimeEq` and call `.ct_eq(other).into()`.
- `Mac::finalize()` consumes the `Mac` instance and returns a `CtOutput<HmacSha256>`. Calling `.into_bytes()` gives you the raw bytes as a `GenericArray`. Convert to `Vec<u8>` or `[u8; 32]` at your API boundary — `GenericArray` is an implementation detail.
- `Mac::new_from_slice` returns `Result` (an `Err` only if the key is empty — zero-length keys are rejected). For your wrapper that enforces `>= 32` bytes, the `expect` is safe but a proper error type is cleaner in library code.
- The `zeroize` feature on `sha2` and `hmac` crates zeroes intermediate state on drop. Enable it: `sha2 = { version = "0.10", features = ["zeroize"] }`. This prevents key material in the HMAC state from lingering in freed memory.
- `ring::hmac` uses a different type: `ring::hmac::Key` (created with `ring::hmac::Key::new(ring::hmac::HMAC_SHA256, key_bytes)`) and `ring::hmac::sign` / `ring::hmac::verify`. The `verify` function is constant-time. Do not mix `ring` and RustCrypto types in the same module.

## Tests to write
- Round-trip: compute HMAC, verify with same key and data, assert true.
- Wrong-key rejection: compute with key A, verify with key B, assert false.
- Tampered data: compute, flip one byte, verify, assert false.
- Key length guard: assert that `compute_hmac(&[0u8; 8], data)` panics (or returns `Err` if using `Result`).
- Constant-time: assert that `verify_hmac` uses `ct_eq` and not `==` (enforced at code-review or via a `clippy` deny rule on `PartialEq` for byte arrays).
- SHA-256 known vector: `sha256_digest(b"")` equals the known empty-string SHA-256 value.
