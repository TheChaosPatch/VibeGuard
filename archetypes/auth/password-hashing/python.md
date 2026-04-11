---
schema_version: 1
archetype: auth/password-hashing
language: python
principles_file: _principles.md
libraries:
  preferred: argon2-cffi
  acceptable:
    - passlib
  avoid:
    - name: hashlib
      reason: Fast hashes (SHA-256, etc.) are not password hashes.
    - name: bcrypt
      reason: Outdated unless you need bcrypt interop with an existing DB.
minimum_versions:
  python: "3.10"
---

# Password Hashing — Python

## Library choice
`argon2-cffi` ships Argon2id with sensible defaults. `passlib` is the classic abstraction layer; it is acceptable but its own API is larger than you need. If you pick `passlib`, pin to the Argon2 scheme specifically.

## Reference implementation
```python
from argon2 import PasswordHasher
from argon2.exceptions import VerifyMismatchError, InvalidHashError

_hasher = PasswordHasher(
    time_cost=3,
    memory_cost=64 * 1024,  # 64 MiB
    parallelism=4,
    hash_len=32,
    salt_len=16,
)

def hash_password(password: str) -> str:
    """Return an encoded Argon2id hash, parameters embedded."""
    return _hasher.hash(password)

def verify_password(password: str, encoded: str) -> tuple[bool, bool]:
    """Return (is_valid, needs_rehash). Constant-time under the hood."""
    try:
        _hasher.verify(encoded, password)
    except (VerifyMismatchError, InvalidHashError):
        return False, False
    return True, _hasher.check_needs_rehash(encoded)
```

## Language-specific gotchas
- `argon2-cffi`'s `PasswordHasher` object is re-entrant and cheap to reuse — construct it once at module import, not per request.
- `check_needs_rehash` is the signal to quietly re-hash the password on a successful login after you upgrade parameters.
- Do not wrap passwords in `bytes` and then `decode()`. Pass `str` directly; the library handles encoding.
- Exception types are broader than you might expect: catch `VerifyMismatchError` and `InvalidHashError` explicitly, not bare `Exception`, so malformed hash strings in the database surface distinctly.
- If you use Django, its built-in `PBKDF2PasswordHasher` is fine for new projects but Argon2id via `django-argon2` is better.

## Tests to write
- Round-trip: `verify_password(pw, hash_password(pw))` is `(True, False)` when parameters are current.
- Negative: wrong password returns `(False, False)` and raises nothing.
- Parameter drift: construct a `PasswordHasher` with lower `time_cost`, hash, then verify against a re-tuned module — expect `needs_rehash=True`.
- Salt uniqueness: hashing the same password twice yields distinct encoded strings.
