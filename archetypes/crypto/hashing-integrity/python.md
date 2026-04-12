---
schema_version: 1
archetype: crypto/hashing-integrity
language: python
principles_file: _principles.md
libraries:
  preferred: hmac (stdlib) + hashlib (stdlib)
  acceptable:
    - cryptography (for HKDF, CMAC, or streaming HMAC)
  avoid:
    - name: hashlib.md5
      reason: MD5 is broken for collision resistance; do not use for integrity.
    - name: hashlib.sha1
      reason: SHAttered collision demonstrated; deprecated for new integrity work.
    - name: == for MAC comparison
      reason: Short-circuits on mismatch — use hmac.compare_digest.
minimum_versions:
  python: "3.11"
---

# Hashing and Data Integrity — Python

## Library choice
Python's stdlib `hmac` module provides `hmac.new` and the critical `hmac.compare_digest` for constant-time comparison. Combined with `hashlib.sha256` or `hashlib.sha512` for the underlying digest, it covers all HMAC and keyless-hash use cases without a third-party dependency. The `cryptography` package is the right addition when you need HKDF, CMAC (AES-based MAC), or streaming HMAC over large files via an incremental interface. Do not reach for `hashlib.md5` or `hashlib.sha1` for new integrity work — their collision resistance is broken.

## Reference implementation
```python
from __future__ import annotations
import hashlib
import hmac as hmac_mod

_DIGEST = "sha256"
_PREFIX = b"webhook-v1:"


def compute_hmac(key: bytes, data: bytes) -> bytes:
    """HMAC-SHA256. key must be at least 32 random bytes."""
    return hmac_mod.new(key, data, _DIGEST).digest()


def verify_hmac(key: bytes, data: bytes, expected_tag: bytes) -> bool:
    """Constant-time HMAC verification. Returns False on any mismatch."""
    actual = compute_hmac(key, data)
    return hmac_mod.compare_digest(actual, expected_tag)


def sha256_digest(data: bytes) -> bytes:
    """Keyless SHA-256 for content-addressed storage or download checksums."""
    return hashlib.sha256(data).digest()


def webhook_tag(hmac_key: bytes, payload: bytes) -> bytes:
    """Bind a purpose string to prevent cross-context replay."""
    return compute_hmac(hmac_key, _PREFIX + payload)
```

## Language-specific gotchas
- `hmac.compare_digest` is the only correct comparator for MAC values. It accepts `bytes` or `str` but the types must match. `actual == expected` (or any form of `!=`) leaks timing. Python's `==` on `bytes` short-circuits at the first differing byte.
- `hmac.new(key, msg, digestmod)` — the `digestmod` argument is mandatory since Python 3.8; passing `None` raises a `TypeError`. Pass `"sha256"` (a string) or `hashlib.sha256` (the constructor). Both work; the string form is slightly shorter.
- Key length: `hmac.new` accepts any key length. Keys shorter than the block size (64 bytes for SHA-256) are zero-padded internally; keys longer are hashed. Use 32 bytes of `os.urandom(32)` for a clean 256-bit key.
- For large files, use the incremental form: `h = hmac_mod.new(key, digestmod=_DIGEST); h.update(chunk)` in a loop, then `h.digest()`. Do not load a multi-GB file into memory to hash it.
- `hashlib.sha256(data).hexdigest()` returns a hex string — correct for display or logging. Use `.digest()` (bytes) for binary comparison or further cryptographic use.
- Do not use `hashlib.sha256(password).digest()` as a password hash — this is a fast hash, not a memory-hard KDF. See `auth/password-hashing` for the correct approach.

## Tests to write
- Round-trip HMAC: compute, verify with same key and data, assert True.
- Wrong-key rejection: compute with key A, verify with key B, assert False.
- Tampered data: compute, change one byte of data, verify, assert False.
- Constant-time comparison: grep the codebase to assert `==` is never used to compare HMAC digests (enforce via linting rule or test).
- Purpose prefix: assert `webhook_tag(k, data) != compute_hmac(k, data)`.
- Key length: construct HMAC with a key of fewer than 32 bytes, assert a `ValueError` is raised by your factory (add this check to your wrapper, not the stdlib).
