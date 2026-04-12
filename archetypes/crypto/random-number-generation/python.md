---
schema_version: 1
archetype: crypto/random-number-generation
language: python
principles_file: _principles.md
libraries:
  preferred: secrets
  acceptable:
    - os.urandom
  avoid:
    - name: random
      reason: Mersenne Twister PRNG; deterministic, predictable, not for security.
    - name: numpy.random
      reason: Designed for statistical simulation, not cryptographic use.
minimum_versions:
  python: "3.10"
---

# Cryptographic Random Number Generation -- Python

## Library choice
The `secrets` module (stdlib, added in 3.6) is the correct default for every security-sensitive random value. It wraps `os.urandom` and provides `token_bytes`, `token_hex`, `token_urlsafe`, and `randbelow` -- all backed by the OS CSPRNG. `os.urandom` is the lower-level primitive and is equally safe, but `secrets` expresses intent more clearly and is harder to confuse with `random`. The `random` module is a Mersenne Twister PRNG: its state can be reconstructed from 624 observed 32-bit outputs, making every token it generates predictable.

## Reference implementation
```python
from __future__ import annotations
import secrets
import hmac
import hashlib


def generate_token(byte_length: int = 32) -> str:
    """URL-safe Base64 token with `byte_length` bytes of entropy."""
    if byte_length < 16:
        raise ValueError("Minimum 16 bytes (128 bits) of entropy required")
    return secrets.token_urlsafe(byte_length)


def generate_hex_token(byte_length: int = 32) -> str:
    """Lowercase hex token with `byte_length` bytes of entropy."""
    if byte_length < 16:
        raise ValueError("Minimum 16 bytes (128 bits) of entropy required")
    return secrets.token_hex(byte_length)


def generate_uniform_int(exclusive_max: int) -> int:
    """Uniform random integer in [0, exclusive_max) without modulo bias."""
    if exclusive_max <= 0:
        raise ValueError("exclusive_max must be positive")
    return secrets.randbelow(exclusive_max)


def generate_lookup_pair(byte_length: int = 32) -> tuple[str, str]:
    """Returns (token, token_hash) for storage patterns where you
    must look up a token without storing it in plaintext."""
    token = secrets.token_urlsafe(byte_length)
    token_hash = hashlib.sha256(token.encode()).hexdigest()
    return token, token_hash
```

## Language-specific gotchas
- `secrets.token_urlsafe(n)` generates `n` random bytes and then Base64-encodes them. The returned string is longer than `n` characters but carries exactly `n * 8` bits of entropy. Do not confuse byte length with string length.
- `secrets.randbelow(n)` uses rejection sampling internally -- it is bias-free. Never write `secrets.randbits(32) % n`.
- `random.SystemRandom` also wraps `os.urandom`, but importing `random` at all in a security module invites reviewers to wonder whether you meant `random.random()`. Prefer `secrets` for clarity.
- `os.urandom` never blocks on Linux 3.17+ (it uses `getrandom(GRND_NONBLOCK)` with fallback). On very early boot in containers with no entropy, it can theoretically block -- this is a deployment concern, not a code concern.
- If you need to compare a user-supplied token against a stored value, use `hmac.compare_digest` for constant-time comparison. `==` leaks length and content through timing.
- Never log the return value of any function in this module. The `generate_lookup_pair` pattern exists specifically so you can store a SHA-256 hash for lookup and hand the raw token to the user exactly once.

## Tests to write
- Length correctness: `generate_token(32)` decodes (Base64) to exactly 32 bytes.
- Minimum entropy enforcement: `generate_token(8)` raises `ValueError`.
- No collisions: generate 10,000 tokens, assert all distinct.
- Uniform distribution: call `generate_uniform_int(6)` 60,000 times, chi-squared test at p < 0.01.
- URL safety: `generate_token` output matches `^[A-Za-z0-9_-]+$`.
- Lookup pair: `generate_lookup_pair` returns a tuple where `sha256(token) == token_hash`.
- Negative/zero max: `generate_uniform_int(0)` and `generate_uniform_int(-1)` raise `ValueError`.
