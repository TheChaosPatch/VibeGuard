---
schema_version: 1
archetype: crypto/symmetric-encryption
title: Symmetric Encryption
summary: Encrypting data at rest (and in transit, when TLS isn't enough) with authenticated symmetric ciphers.
applies_to: [csharp, python]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - crypto
  - encryption
  - aes
  - gcm
  - chacha20
  - cipher
  - symmetric
  - aead
  - nonce
  - iv
related_archetypes:
  - persistence/secrets-handling
references:
  owasp_asvs: V6.2
  owasp_cheatsheet: Cryptographic Storage Cheat Sheet
  cwe: "327"
---

# Symmetric Encryption — Principles

## When this applies
Any time you need to store a blob of bytes such that only holders of a specific key can read it back: encrypted database columns, encrypted files on disk, encrypted cache entries, encrypted cookies, encrypted message-queue payloads between two services. This is about *symmetric* encryption — one key, used by both the writer and the reader. For password storage, see `auth/password-hashing` (that's a one-way operation, not encryption). For key distribution or signing, reach for asymmetric primitives instead — a different archetype.

## Architectural placement
Encryption happens in exactly one narrow layer — a `Cryptographer` / `Encryptor` wrapper — that owns two things: the AEAD primitive and the key (resolved from the `SecretsProvider`, never a literal). The layer above it says `crypto.Encrypt(plaintext, associatedData)` and gets back an opaque byte blob with a versioned key-id prefix; it never touches a nonce, a cipher mode, or a raw key. Rotating keys is then a change to one class, and every call site picks it up automatically. This structural choice is what prevents "I'll just AES this string real quick" from scattering ten inconsistent implementations across the codebase, half of which are ECB.

## Principles
1. **Use AEAD, not raw cipher modes.** AES-GCM or ChaCha20-Poly1305. An AEAD primitive gives you confidentiality *and* integrity in one call. Plain AES-CBC without a MAC is a padding-oracle waiting to happen. AES-ECB is not an encryption mode for user data — it's a demonstration of why modes exist.
2. **Never reuse a nonce with the same key.** This is catastrophic for GCM — a single repeat leaks the authentication key, not just the plaintext. Generate nonces with a CSPRNG (12 bytes for GCM) or a strictly-monotonic counter you control. If you're unsure whether a value is unique, assume it isn't.
3. **Keys come from a KDF or a KMS, never a string literal.** `key = "mysecretkey12345"` is not a key, it's a time bomb. Derive from a KMS-held master via HKDF, or pull a pre-generated 256-bit value from the secrets provider.
4. **Bind associated data to the ciphertext.** If the plaintext "belongs" to a record id, a tenant, or a column name, feed that context into the AEAD as `associatedData`. An attacker who can shuffle ciphertexts between records — a real risk in multi-tenant systems — loses the ability to do so.
5. **Version your ciphertexts with a key id prefix.** `v1:<keyid>:<nonce>:<ciphertext>`. Rotating a key means writing new values with `v2`, decrypting old values with `v1` until they're all rewritten. "We can't rotate without downtime" is a bug.
6. **Encrypt, then forget the plaintext.** Don't log it, don't echo it in an error, don't `ToString()` an object that carries it. Treat plaintext buffers the way you'd treat a secret — because they are one.
7. **Don't invent a mode.** Don't chain AES blocks by hand, don't compose HMAC + CBC yourself "for clarity," don't XOR two ciphertexts "just as a sanity check." If you are writing a loop that calls `cipher.Transform`, stop — the AEAD primitive already does the loop correctly.

## Anti-patterns
- AES-ECB for *anything* longer than a single block. The "ECB penguin" image exists because this failure is visible to the naked eye.
- AES-CBC without an authentication tag. Every padding-oracle CVE in the last fifteen years is a descendant of this choice.
- A hardcoded 16-byte key string compiled into the binary.
- Deriving a key by calling `SHA256(password)` once. That's not a KDF, that's a hash. Use PBKDF2, scrypt, or Argon2id.
- A zero-filled nonce "because the key is random anyway."
- Storing the key in the same table (or the same row) as the ciphertext.
- `try { decrypt() } catch { return plaintext; }` — the classic "fall back to unencrypted if decryption fails" handler.

## References
- OWASP ASVS V6.2 — Algorithms
- OWASP Cryptographic Storage Cheat Sheet
- CWE-327 — Use of a Broken or Risky Cryptographic Algorithm
