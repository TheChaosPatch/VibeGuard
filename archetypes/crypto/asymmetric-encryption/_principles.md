---
schema_version: 1
archetype: crypto/asymmetric-encryption
title: Asymmetric Encryption and Signing
summary: Generating RSA/ECC key pairs, encrypting with public keys, and signing or verifying data with private keys.
applies_to: [csharp, python, go, javascript, typescript, java, kotlin, rust]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - rsa
  - ecc
  - ecdsa
  - ed25519
  - signing
  - verify
  - keypair
  - asymmetric
  - public-key
related_archetypes:
  - crypto/key-management
  - crypto/symmetric-encryption
references:
  owasp_asvs: V6.2
  owasp_cheatsheet: Cryptographic Storage Cheat Sheet
  cwe: "327"
---

# Asymmetric Encryption and Signing — Principles

## When this applies
Any time two parties need to communicate without sharing a prior secret, or when one party needs to prove data origin to another: JWT signing, software artifact signing, certificate issuance, encrypted email/message envelopes, mTLS client authentication, API request signing, SSH key authentication, and any protocol where a public key can be freely distributed while the private key stays secret. This archetype covers asymmetric encryption (encrypt with public key, decrypt with private key) and digital signatures (sign with private key, verify with public key). For symmetric bulk-data encryption, see `crypto/symmetric-encryption`. For key lifecycle and storage, see `crypto/key-management`.

## Architectural placement
The private key never touches application code. It lives in an HSM, a cloud KMS (AWS KMS asymmetric key, GCP Cloud HSM, Azure Key Vault), or — for lower-value use cases — a hardware-backed OS keystore or a secrets manager. The application holds a reference (key ID, ARN, key handle) and calls the signing or decryption operation remotely or through an OS API that keeps the private key opaque. The public key is freely distributable; it can be embedded in config, fetched from a JWKS endpoint, or stored in a database. Signature verification is a read-only, public-key-only operation and can happen anywhere — it should happen as close to the trust boundary as possible (e.g., in the API gateway or request handler, not deep in domain logic).

## Principles
1. **Prefer signing over encryption for most use cases.** Asymmetric encryption is expensive and limited in message size; in practice you encrypt a symmetric DEK with the recipient's public key and encrypt the payload with the DEK (hybrid encryption). Signatures are the more common need: JWTs, software releases, API webhooks, code signing. Choose the right primitive for the job.
2. **Use modern curves for ECDSA/EdDSA.** P-256 (secp256r1) and Ed25519 are the correct defaults. P-521 is acceptable for high-value keys. Avoid P-192 and secp256k1 outside of blockchain contexts. For RSA, 2048 bits is the floor; 3072 or 4096 is preferred for keys expected to outlive 2030.
3. **Use Ed25519 or ECDSA for new signing workflows.** RSA-PKCS1v1.5 signatures have padding-oracle vulnerabilities; use RSA-PSS if RSA is required by an existing standard. Ed25519 is deterministic, small, fast, and immune to nonce-reuse attacks that plague ECDSA.
4. **Never reuse a nonce (k-value) in ECDSA.** A single nonce reuse with the same key leaks the private key algebraically. Use a CSPRNG or RFC 6979 deterministic nonce generation. Libraries that implement RFC 6979 make this a non-issue; prefer them.
5. **Validate all inputs before verifying signatures.** Verify that the key is the expected key (by fingerprint or key ID), that the algorithm matches what you expect, and that the signed data is well-formed before trusting the verification result. "Signature verified" only means the signing key signed that blob — it does not mean the blob is safe to process.
6. **Always verify signatures before acting on signed data.** This seems obvious, but JWT libraries have had bugs where `alg: none` caused verification to be skipped. Pin the expected algorithm explicitly; never allow the signed artifact to specify its own algorithm.
7. **Use hybrid encryption for large payloads.** Generate a random 256-bit AES-GCM DEK, encrypt the payload with the DEK, encrypt the DEK with the recipient's public RSA or ECDH key, transmit both. Libraries implementing ECIES or HPKE (RFC 9180) do this correctly. Rolling your own hybrid scheme is where mistakes accumulate.
8. **Treat key generation as an infrastructure event, not a code event.** Private keys should be generated inside the KMS or HSM. If you must generate outside (e.g., for a test fixture), use the platform's CSPRNG, store the private key immediately in a secrets manager, and shred the in-memory copy.

## Anti-patterns
- RSA-PKCS1v1.5 encryption (vulnerable to Bleichenbacher attack; use OAEP+SHA-256 if RSA encryption is unavoidable).
- RSA-PKCS1v1.5 signatures on new systems (use RSA-PSS or switch to ECDSA/EdDSA).
- RSA key sizes below 2048 bits.
- Hardcoding a private key PEM in source code or a config file.
- Trusting the `alg` header in a JWT without pinning the expected algorithm server-side.
- `alg: none` acceptance — any library configuration that allows this must be explicitly disabled.
- Generating an ECDSA signature in a loop without RFC 6979 or a verified CSPRNG.
- Using the same key pair for both signing and encryption (key-use separation).
- Storing the private key in a database row or alongside encrypted data.

## References
- OWASP ASVS V6.2 — Algorithms
- OWASP Cryptographic Storage Cheat Sheet
- CWE-327 — Use of a Broken or Risky Cryptographic Algorithm
- RFC 8037 — CFRG Elliptic Curves for JOSE (Ed25519)
- RFC 9180 — Hybrid Public Key Encryption (HPKE)
