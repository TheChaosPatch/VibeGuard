---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: rust
principles_file: _principles.md
libraries:
  preferred: ed25519-dalek + p256
  acceptable:
    - ring
    - rsa (crate, OAEP only)
    - aws-lc-rs
  avoid:
    - name: openssl crate for new signing code
      reason: Safe to use but heavy FFI dependency; pure-Rust alternatives are preferred for portability and auditability.
    - name: rsa crate with PKCS1v15 encryption
      reason: Bleichenbacher-vulnerable; use Oaep<Sha256> padding via the rsa crate's OAEP types.
minimum_versions:
  rust: "1.78"
---

# Asymmetric Encryption and Signing — Rust

## Library choice
`ed25519-dalek` (from the `dalek-cryptography` project) is the idiomatic choice for Ed25519 signing in Rust: constant-time, pure Rust, widely audited. For P-256 ECDSA, `p256` (from the `RustCrypto` project) is the standard. The `ring` crate (by Brian Smith) is a production-hardened alternative that bundles BoringSSL-derived primitives — it is the right choice if you need FIPS-adjacent validation or are already using it for symmetric work. For RSA encryption, the `rsa` crate with `Oaep<Sha256>` padding is correct. `aws-lc-rs` is a drop-in `ring` API compatible crate backed by AWS-LC; use it when FIPS compliance is a requirement.

## Reference implementation
```rust
use ed25519_dalek::{Signature, Signer, SigningKey, Verifier, VerifyingKey};
use rand::rngs::OsRng;

pub struct Ed25519Signer {
    signing_key: SigningKey,
}

impl Ed25519Signer {
    pub fn generate() -> Self {
        Self { signing_key: SigningKey::generate(&mut OsRng) }
    }

    pub fn sign(&self, data: &[u8]) -> Signature {
        self.signing_key.sign(data)
    }

    pub fn verifying_key(&self) -> VerifyingKey {
        self.signing_key.verifying_key()
    }
}

pub fn verify(data: &[u8], sig: &Signature, key: &VerifyingKey) -> bool {
    key.verify(data, sig).is_ok()
}

// RSA-OAEP DEK wrapping via the `rsa` crate
use rsa::{Oaep, RsaPrivateKey, RsaPublicKey};
use sha2::Sha256;

pub fn encrypt_dek(dek: &[u8], pub_key: &RsaPublicKey) -> rsa::Result<Vec<u8>> {
    pub_key.encrypt(&mut OsRng, Oaep::new::<Sha256>(), dek)
}

pub fn decrypt_dek(wrapped: &[u8], priv_key: &RsaPrivateKey) -> rsa::Result<Vec<u8>> {
    priv_key.decrypt(Oaep::new::<Sha256>(), wrapped)
}
```

## Language-specific gotchas
- `ed25519-dalek` 2.x changed the `Signer`/`Verifier` traits — ensure your dependency tree uses a consistent major version. The `SigningKey` type in 2.x replaces the old `Keypair`. Check `Cargo.lock` for conflicting `ed25519` crate versions.
- `Signature` implements `Zeroize` in `ed25519-dalek` when the `zeroize` feature is enabled. Enable it: `ed25519-dalek = { version = "2", features = ["zeroize"] }`. Similarly for `SigningKey` — the private key bytes will be zeroed on drop.
- `OsRng` is the correct RNG for all key generation and randomized operations. Never use `rand::thread_rng()` for cryptographic key generation — while it seeds from the OS, its PRNG state is not designed for cryptographic use.
- For the `rsa` crate, `RsaPrivateKey::new(&mut OsRng, 3072)` generates a 3072-bit key. `new(&mut OsRng, 1024)` will succeed — there is no runtime guard. Enforce minimum key size in your factory function with an explicit assertion.
- `ring` uses a different API surface: `ring::signature::Ed25519KeyPair` from a PKCS#8 DER document. It is not directly interoperable with `ed25519-dalek` key formats. Choose one and be consistent within a crate.
- The `zeroize` crate is a transitive dependency of most RustCrypto crates. Use `ZeroizeOnDrop` derive on any struct that holds private key bytes you manage manually.

## Tests to write
- Round-trip: generate key, sign a byte slice, verify with `verifying_key()`, assert `Ok`.
- Wrong-key rejection: sign with key A, verify with key B's verifying key, assert `Err`.
- Tampered data: sign, corrupt one byte of data, verify, assert `Err`.
- RSA OAEP round-trip: encrypt a 32-byte DEK, decrypt, assert `dek == decrypted`.
- Key size: generate RSA key, assert `priv_key.n().bits() >= 2048`.
- Zeroize on drop: assert that `SigningKey` implements `ZeroizeOnDrop` (compile-time, via trait bound assertion in a test).
