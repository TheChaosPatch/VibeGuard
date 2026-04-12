---
schema_version: 1
archetype: crypto/key-management
language: python
principles_file: _principles.md
libraries:
  preferred: cryptography (hazmat key derivation + Fernet for envelope pattern)
  acceptable:
    - boto3 (AWS KMS)
    - google-cloud-kms
    - azure-keyvault-keys
  avoid:
    - name: Hardcoded key strings in source
      reason: Cannot be rotated, revoked, or audited.
    - name: os.environ for raw key bytes in production
      reason: Environment variables are visible in /proc, ps, and crash dumps. Use a secrets manager.
minimum_versions:
  python: "3.10"
---

# Cryptographic Key Management -- Python

## Library choice
For cloud-hosted production keys, use the cloud KMS SDK (`boto3` for AWS KMS, `google-cloud-kms`, `azure-keyvault-keys`). These keep the KEK in hardware and provide audit trails. For local DEK generation and wrapping, the `cryptography` package provides `os.urandom`-backed key generation and `Fernet` (which is AES-CBC+HMAC with built-in versioning -- acceptable for envelope wrapping when a cloud KMS is not available). For key derivation from passwords or low-entropy sources, use `HKDF` or `PBKDF2HMAC` from `cryptography.hazmat`. Never store raw key bytes in environment variables that end up in container image layers or process listings.

## Reference implementation
```python
from __future__ import annotations
import os
from contextlib import contextmanager
from dataclasses import dataclass, field
from typing import Iterator


@dataclass(slots=True)
class KeyMaterial:
    """Holds DEK bytes and zeroes them on destruction."""
    version: int
    _key: bytearray = field(repr=False)

    @property
    def key(self) -> bytes:
        if not self._key:
            raise RuntimeError("Key material has been destroyed")
        return bytes(self._key)

    def destroy(self) -> None:
        for i in range(len(self._key)):
            self._key[i] = 0
        self._key.clear()


@contextmanager
def scoped_key(version: int, key_bytes: bytes) -> Iterator[KeyMaterial]:
    """Context manager that guarantees key zeroing on exit."""
    km = KeyMaterial(version=version, _key=bytearray(key_bytes))
    try:
        yield km
    finally:
        km.destroy()


def generate_dek(version: int) -> KeyMaterial:
    """Generate a 256-bit DEK from the OS CSPRNG."""
    return KeyMaterial(version=version, _key=bytearray(os.urandom(32)))
```

## Language-specific gotchas
- Python's garbage collector does not guarantee when (or if) an object's `__del__` runs. Never rely on `__del__` for zeroing key material. Use an explicit `destroy()` method or the `scoped_key` context manager to ensure deterministic zeroing.
- `bytearray` is mutable and can be overwritten in place. `bytes` is immutable -- once a key is in a `bytes` object, you cannot zero it. The `KeyMaterial` class stores `bytearray` internally and exposes `bytes` only via a property that copies on read. The copy is short-lived and on the caller's stack.
- `repr=False` on the `_key` field prevents the key from appearing in `repr()`, `print()`, `logging.debug("%s", km)`, or debugger watches. This is a defense-in-depth measure against accidental logging.
- When using AWS KMS `generate_data_key`, the response contains both the plaintext DEK and the encrypted (wrapped) DEK. Store only the wrapped version. Unwrap at runtime, use the plaintext DEK, then zero it. Do not cache the plaintext DEK in Redis or a database.
- `os.urandom(32)` is the correct source for local DEK generation. `secrets.token_bytes(32)` is equivalent but signals "token" rather than "key" -- either is fine, but be consistent.
- Environment variables holding key material are visible via `/proc/<pid>/environ` on Linux. Use a secrets manager (Vault, AWS Secrets Manager, Azure Key Vault) that provides short-lived leases and audit logging.

## Tests to write
- Key length: `generate_dek` produces a key that is exactly 32 bytes.
- Zeroing: create a `KeyMaterial`, call `destroy()`, assert every byte in `_key` is zero and `_key` is empty.
- Destroyed access: after `destroy()`, accessing `.key` raises `RuntimeError`.
- Scoped cleanup: use `scoped_key`, exit the context, assert the key is destroyed.
- No repr leak: `repr(km)` does not contain the key bytes.
- Version tagging: `generate_dek(3).version == 3`.
- Two DEKs differ: generate two DEKs, assert they are not equal.
