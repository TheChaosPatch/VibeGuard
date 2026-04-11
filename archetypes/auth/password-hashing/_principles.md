---
schema_version: 1
archetype: auth/password-hashing
title: Password Hashing
summary: Storing, verifying, and handling user passwords in any backend.
applies_to: [csharp, python, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-11"
keywords:
  - password
  - credential
  - login
  - hash
  - bcrypt
  - argon2
  - pbkdf2
  - kdf
related_archetypes:
  - auth/session-tokens
references:
  owasp_asvs: V2.4
  owasp_cheatsheet: Password Storage Cheat Sheet
  cwe: "916"
---

# Password Hashing — Principles

## When this applies
Any time your system stores a user-chosen password, verifies a login attempt, or transmits a credential through a component you control. This does **not** apply to API keys, access tokens, or other high-entropy machine secrets — those use different primitives (see `persistence/secrets-handling`).

## Architectural placement
Password handling lives behind a dedicated abstraction — typically a `PasswordHasher` or `CredentialService` — that HTTP handlers, CLI commands, and admin tools all go through. No route handler, data-access layer, or view should ever call a hashing library directly. This keeps algorithm selection, parameter tuning, and migration logic in exactly one place, and makes password logic independently testable and auditable.

## Principles
1. **Use a modern memory-hard KDF.** Argon2id is the current default. PBKDF2 is acceptable only when required by FIPS compliance or an existing database you can't migrate.
2. **Never invent your own scheme.** Do not "hash and salt" with SHA-256. Do not add a homegrown pepper unless you have a documented reason and a key-rotation plan.
3. **Tune cost parameters for your hardware.** Target 200–500 ms per hash on the production server. Re-tune when hardware changes.
4. **Verify in constant time.** Use the library's verify function, never a manual string comparison.
5. **Rehash on login when parameters change.** When you upgrade the cost factor, verify the old hash, then silently re-hash and update the database if the user's stored hash uses outdated parameters.
6. **Plaintext passwords live only on the stack.** Never log them. Never include them in error messages. Never serialize them. Never store them even temporarily in persistent caches.

## Anti-patterns
- Storing MD5, SHA-1, or SHA-256 hashes of passwords ("fast hash" algorithms are not password hashes).
- Concatenating a salt with SHA-256 and calling it "salted hashing."
- Building your own pepper / key-wrap scheme without a documented threat model.
- Using `==` to compare hashes (timing-attack surface).
- Logging the hashed password at debug level.
- Returning a different error for "user not found" vs "wrong password" (username enumeration).

## References
- OWASP ASVS V2.4 — Stored Credential Verifier Requirements
- OWASP Password Storage Cheat Sheet
- CWE-916 — Use of Password Hash With Insufficient Computational Effort
