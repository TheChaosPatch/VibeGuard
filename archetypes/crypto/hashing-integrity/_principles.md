---
schema_version: 1
archetype: crypto/hashing-integrity
title: Hashing and Data Integrity
summary: Using HMAC and cryptographic digests to verify data integrity, authenticate messages, and produce checksums — not for password storage.
applies_to: [csharp, python, go, javascript, typescript, java, kotlin, rust, c]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - hmac
  - sha256
  - sha512
  - checksum
  - integrity
  - digest
  - mac
  - hash
related_archetypes:
  - auth/password-hashing
references:
  owasp_asvs: V6.3
  owasp_cheatsheet: Cryptographic Storage Cheat Sheet
  cwe: "328"
---

# Hashing and Data Integrity — Principles

## When this applies
Any time you need to detect accidental or malicious modification of data in transit or at rest: API webhook signature verification, file integrity checks, cache invalidation keys, idempotency tokens, TOTP HMAC computation, signed URL parameters, message authentication codes for inter-service payloads, and checksum validation after downloads. This archetype is explicitly NOT about password storage — use `auth/password-hashing` (Argon2id/bcrypt/scrypt) for that. This is also not about encryption — a hash or HMAC does not provide confidentiality. It provides authenticity (with a key) or integrity (without a key, but only against accidental corruption, not adversarial tampering).

## Architectural placement
HMAC operations belong in a thin authentication layer that wraps the transport or storage boundary — the same place you'd put signature verification for JWTs or request signing for outbound webhooks. The HMAC key is a secret and must be treated identically to an encryption key: pulled from a secrets manager, never hardcoded, rotatable. For keyless checksums (SHA-256 of a file), the computation belongs in the I/O layer at the point where data enters or leaves the trust boundary — not scattered throughout business logic. The verified hash or HMAC result (a boolean) is what crosses into domain logic; the raw digest does not.

## Principles
1. **HMAC, not bare hash, when authentication is the goal.** A raw SHA-256 of a payload is integrity-only: anyone who can see the payload can compute the same hash and replace it. HMAC adds a secret key, making the tag unforgeable without that key. Use HMAC-SHA256 or HMAC-SHA512 for any MAC use case.
2. **Use SHA-256 or SHA-512 for all new work.** SHA-1 and MD5 are broken for collision resistance. SHA-256 is the default; SHA-512 provides a larger margin. SHA-3 (Keccak) is an acceptable alternative but rarely necessary in practice.
3. **Compare MACs and hashes with a constant-time comparison.** Byte-by-byte comparison that short-circuits on the first mismatch leaks timing information — an attacker can infer how many bytes match. Every language has a constant-time equality function; use it for any security-relevant comparison.
4. **HMAC keys are secrets — treat them as such.** An HMAC key must be a random secret, at least 32 bytes, from a CSPRNG. It is not a password, not a database ID, not the application name. It lives in the secrets manager alongside encryption keys and is rotatable. Compromising the HMAC key allows forging any MAC computed with it.
5. **Include context in what you sign.** HMAC-SHA256 of a raw payload is vulnerable to substitution attacks across different message types. Prefix the input with a purpose string: `"webhook-v1:" + payload`. This prevents an HMAC intended for one context from being accepted in another.
6. **Keyed hash functions (HMAC) and unkeyed hash functions (SHA-256) are different primitives for different purposes.** Unkeyed hashes are suitable for: content-addressed storage, file checksums for download integrity, deterministic identifiers from stable inputs. They are not suitable for any use case where an attacker controls the input and could forge the hash.
7. **HKDF is not a MAC; it is a key derivation function.** If you need to derive multiple keys from a master secret, use HKDF. If you need to authenticate a message, use HMAC. The two operations look similar but are not interchangeable.
8. **Version your HMAC scheme.** Include a version prefix in the signed input (e.g., `"v1:"`) and in the tag storage. When you rotate keys or upgrade from HMAC-SHA256 to HMAC-SHA512, you can verify old tags with the old key+algorithm and issue new ones without a flag day.

## Anti-patterns
- SHA-256 of a webhook payload used as a signature without a secret key — anyone who can observe the payload can forge it.
- Comparing HMAC tags with `==` or `memcmp` — timing side-channel allows forging.
- MD5 or SHA-1 for any new integrity check (collision attacks are practical for SHA-1 with chosen prefixes).
- Using the HMAC key as the data or the data as the key (argument order confusion in HMAC APIs).
- A hardcoded HMAC key in source: `const HMAC_KEY = "supersecret"`.
- Truncating a MAC to 4 or 8 bytes to save space — this lowers forgery resistance to 2^32 or 2^64, which is attackable offline.
- Using `hashlib.sha256(password)` for password storage — SHA-256 is not a password hash; use `auth/password-hashing`.
- Storing the HMAC tag and the key in the same database row — a database dump then yields both.

## References
- OWASP ASVS V6.3 — Hashing and Random Values
- OWASP Cryptographic Storage Cheat Sheet
- CWE-328 — Use of Weak Hash
- RFC 2104 — HMAC: Keyed-Hashing for Message Authentication
- NIST SP 800-107 — Recommendation for Applications Using Approved Hash Algorithms
