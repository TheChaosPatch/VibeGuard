---
schema_version: 1
archetype: crypto/key-management
title: Cryptographic Key Management
summary: Generating, storing, rotating, and destroying cryptographic keys throughout their lifecycle.
applies_to: [csharp, python, go]
status: draft
keywords:
  - key-management
  - kms
  - hsm
  - envelope-encryption
  - key-rotation
  - key-generation
  - key-storage
  - key-destruction
  - dek
  - kek
  - zeroing
related_archetypes:
  - crypto/symmetric-encryption
  - crypto/random-number-generation
  - persistence/secrets-handling
references:
  owasp_asvs: V6.4
  owasp_cheatsheet: Key Management Cheat Sheet
  cwe: "321"
---

# Cryptographic Key Management -- Principles

## When this applies
Any time your system generates, stores, retrieves, rotates, or disposes of cryptographic key material: symmetric keys for data encryption, asymmetric key pairs for signing or TLS, HMAC keys for integrity, or data encryption keys (DEKs) wrapped by a key-encryption key (KEK). If you are calling an encryption API, the key has a lifecycle, and this archetype governs it.

## Architectural placement
Key management is a *separate concern* from data processing. The component that encrypts a database column should never also be the component that decides where the key lives, how it rotates, or when it expires. In practice this means three layers: (1) a **KMS or HSM** that holds the root/master keys and never exports them, (2) a **key provider** abstraction that resolves a key-id to key material (fetching wrapped DEKs from a store, unwrapping them via the KMS), and (3) the **cryptographer** that receives an already-resolved key and performs encrypt/decrypt. The cryptographer never imports a KMS SDK. The key provider never calls AES. This separation makes rotation a configuration change, not a code change.

## Principles
1. **Never hardcode key material.** Not in source code, not in config files checked into version control, not in environment variables baked into container images. Keys come from a secrets manager, a KMS, or an HSM -- systems designed for this purpose. A hardcoded key is a key that cannot be rotated, audited, or revoked.
2. **Use envelope encryption for data at rest.** Generate a unique DEK per record (or per logical partition), encrypt the data with the DEK, then encrypt the DEK with a KEK held in the KMS. Store the wrapped DEK alongside the ciphertext. This way, rotating the KEK only requires re-wrapping the DEKs, not re-encrypting every row.
3. **Rotate keys without downtime.** Tag every ciphertext with the key version that encrypted it (see `crypto/symmetric-encryption`). When you rotate, new writes use the new key; reads dispatch to the correct key version. A background re-encryption job migrates old ciphertexts. "We can't rotate without a maintenance window" is a design defect.
4. **Generate keys with a CSPRNG at the correct length.** AES-256 requires exactly 32 bytes of CSPRNG output. Do not derive keys by hashing a password (use a KDF) or truncating longer output. The key-generation function is the one place where `crypto/random-number-generation` and this archetype intersect.
5. **Destroy key material when it is no longer needed.** Zero the memory that held the key before releasing it. In managed languages, pin the buffer to prevent the GC from copying it, then overwrite. In Go, use a deferred `clear(slice)`. Key material in swap, core dumps, or heap dumps is a real exfiltration vector.
6. **Separate key management from data processing.** The service that encrypts user data should not have IAM permissions to create, delete, or list keys in the KMS. It should have `encrypt` and `decrypt` permissions on a specific key ARN/resource, nothing more. Principle of least privilege applies to key operations.
7. **Audit every key operation.** Key creation, rotation, deletion, and access should produce an audit log entry with a timestamp, the identity that performed the operation, and the key identifier. Cloud KMS services do this automatically. If you are managing keys yourself, build this logging into the key provider.
8. **Set key expiration policies.** No key should live forever. Define a maximum key age (e.g., 90 days for DEKs, 1 year for KEKs) and enforce it with automated rotation. Expired keys should only be usable for decryption, never for new encryption.

## Anti-patterns
- A symmetric key stored as a Base64 string in a config file, source constant, or environment variable without a secrets manager.
- Using the same key for encryption and HMAC (key separation violation).
- Rotating a key by changing a config value and restarting -- with no re-encryption of existing data and no version tag on ciphertexts.
- Generating a key with `System.Random`, `math/rand`, or Python's `random` module.
- Storing raw key bytes in a database column alongside the data they encrypt.
- Logging key material at any log level, including `Trace` / `DEBUG`.
- A single IAM role with `kms:*` permissions on all keys in the account.
- Assuming the garbage collector will clean up key bytes from memory.

## References
- OWASP ASVS V6.4 -- Secret Management
- OWASP Key Management Cheat Sheet
- CWE-321 -- Use of Hard-coded Cryptographic Key
