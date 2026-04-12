---
schema_version: 1
archetype: crypto/random-number-generation
title: Cryptographic Random Number Generation
summary: Generating unpredictable random values for tokens, keys, nonces, and any security-sensitive context.
applies_to: [csharp, python, go]
status: draft
keywords:
  - csprng
  - prng
  - random
  - entropy
  - token
  - nonce
  - seed
  - secrets
  - unpredictability
  - modulo-bias
related_archetypes:
  - crypto/symmetric-encryption
  - crypto/key-management
  - auth/session-tokens
references:
  owasp_asvs: V6.2
  owasp_cheatsheet: Cryptographic Storage Cheat Sheet
  cwe: "338"
---

# Cryptographic Random Number Generation -- Principles

## When this applies
Any time you generate a value whose unpredictability is a security requirement: session tokens, CSRF tokens, nonces for AEAD ciphers, password-reset links, API keys, OAuth state parameters, random filenames in upload paths, salt for password hashing, initialization vectors, key material, verification codes, or any value an attacker benefits from guessing. If compromise of the value breaks a security property, it must come from a CSPRNG.

## Architectural placement
Random generation belongs in a thin utility or is called inline from the platform's CSPRNG API -- there is no reason to wrap `os.urandom` or `RandomNumberGenerator.GetBytes` in a custom class. What matters is that every call site uses the *right* source. The architectural goal is not abstraction; it is *elimination of the wrong source*. A lint rule or code-review checklist that flags `System.Random`, `math/rand`, or Python's `random` module in security-sensitive paths is worth more than any wrapper class.

## Principles
1. **Always use the OS-backed CSPRNG for security contexts.** `RandomNumberGenerator` (.NET), `secrets` / `os.urandom` (Python), `crypto/rand` (Go). These draw from the kernel entropy pool (CryptGenRandom, getrandom, /dev/urandom). Non-cryptographic PRNGs like `System.Random`, `math/rand`, or Python's `random` are fast and reproducible -- which is exactly why they are wrong here.
2. **Never seed a CSPRNG manually.** The OS CSPRNG seeds itself from hardware entropy. Passing a seed defeats the purpose. If you find yourself writing `new Random(DateTime.Now.Ticks)` for a token, you have already lost -- an attacker who knows the approximate time window can brute-force the seed space.
3. **Generate at least 128 bits of entropy for tokens.** 128 bits means 2^128 possible values -- well beyond brute-force. For most tokens (session IDs, reset codes, API keys), 256 bits (32 bytes) is the pragmatic default. Anything less than 128 bits is gambling with collision probability.
4. **Avoid modulo bias.** `csprng_int % N` does not produce a uniform distribution when N does not evenly divide the range. Use rejection sampling or the platform's uniform-range API (`RandomNumberGenerator.GetInt32`, `secrets.randbelow`, `crypto/rand.Int`).
5. **Encode tokens as URL-safe strings.** `base64url` or lowercase hex. Never truncate the raw bytes to fit a character set -- truncation destroys entropy. A 32-byte random value hex-encoded is 64 characters and 256 bits of entropy; a 32-byte value truncated to printable ASCII is neither.
6. **Treat generated secrets as secrets.** Don't log them, don't include them in error messages, don't embed them in URLs that appear in Referer headers. Generate, transmit over a secure channel, store hashed if possible.
7. **Test the source, not the output.** You cannot unit-test randomness by checking "is this value random enough." You *can* test that the code path calls the CSPRNG (mock/spy), that no two of 10,000 generated tokens collide (smoke test), and that the output length is correct.

## Anti-patterns
- Using `System.Random`, `math/rand`, or Python's `random` module for tokens, keys, nonces, or salts.
- Seeding with `DateTime.Now`, `Environment.TickCount`, `time.Now().UnixNano()`, or the process PID.
- Generating a 4-digit or 6-character token for a security-critical flow (insufficient entropy).
- `random_bytes(32) % 10` to get a "random digit" (modulo bias, and also only 3.3 bits of entropy per digit).
- Logging the generated token at debug level "for troubleshooting."
- Truncating random bytes to fit a column width instead of encoding them properly.
- Rolling a custom PRNG because "the standard one is too slow" without measuring.
- Using a fixed seed "for reproducibility in production."

## References
- OWASP ASVS V6.2 -- Algorithms
- OWASP Cryptographic Storage Cheat Sheet
- CWE-338 -- Use of Cryptographically Weak Pseudo-Random Number Generator
