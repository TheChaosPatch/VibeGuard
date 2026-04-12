---
schema_version: 1
archetype: auth/mfa
language: python
principles_file: _principles.md
libraries:
  preferred: pyotp
  acceptable:
    - py_webauthn
  avoid:
    - name: Custom HMAC-based OTP with hashlib
      reason: Time-step truncation, endianness, and constant-time comparison bugs are near-certain.
    - name: random module for backup code generation
      reason: Not cryptographically secure. Use secrets module exclusively.
minimum_versions:
  python: "3.10"
---

# Multi-Factor Authentication -- Python

## Library choice
`pyotp` implements RFC 6238 TOTP and RFC 4226 HOTP with a small, auditable API. It handles time-step calculation, truncation, and base32 encoding correctly. For WebAuthn, `py_webauthn` covers registration and authentication ceremonies. Both are focused, well-tested libraries. Do not build TOTP from `hmac` + `struct` + `time` -- the implementation looks simple but the edge cases (endianness of the counter, dynamic truncation offset, constant-time comparison) are where every hand-rolled version fails.

## Reference implementation
```python
import hashlib
import secrets
import pyotp

def enroll_totp(user_email: str, issuer: str = "VibeGuard") -> tuple[str, str]:
    """Return (base32_secret, otpauth_uri). Show the URI as a QR code once."""
    secret = pyotp.random_base32(length=32)  # 160-bit secret
    uri = pyotp.TOTP(secret).provisioning_uri(
        name=user_email, issuer_name=issuer
    )
    return secret, uri

def verify_totp(secret: str, code: str) -> bool:
    """Verify a 6-digit TOTP code with +/-1 window tolerance."""
    totp = pyotp.TOTP(secret)
    return totp.verify(code, valid_window=1)

def generate_backup_codes(
    count: int = 10, length: int = 8
) -> tuple[list[str], list[str]]:
    """Return (plain_codes, sha256_hashes). Show plain once, store hashes."""
    alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"
    plain: list[str] = []
    hashes: list[str] = []
    for _ in range(count):
        code = "".join(secrets.choice(alphabet) for _ in range(length))
        plain.append(code)
        hashes.append(hashlib.sha256(code.encode()).hexdigest())
    return plain, hashes

def redeem_backup_code(code: str, stored_hashes: list[str]) -> bool:
    """Verify and consume a backup code. Constant-time comparison."""
    candidate = hashlib.sha256(code.strip().upper().encode()).hexdigest()
    for i, stored in enumerate(stored_hashes):
        if secrets.compare_digest(candidate, stored):
            stored_hashes[i] = ""  # mark consumed
            return True
    return False
```

## Language-specific gotchas
- `pyotp.random_base32(length=32)` generates a 160-bit CSPRNG secret, base32-encoded. The `length` parameter is the number of base32 characters, not bytes. 32 characters = 160 bits, which matches RFC 6238's recommended minimum.
- `valid_window=1` in `pyotp.TOTP.verify()` accepts the current time step and one step on each side (roughly 90 seconds of validity). Do not increase this -- wider windows make brute-force feasible.
- `secrets.compare_digest` is Python's constant-time comparison. Use it for backup code hash comparison. `==` on strings leaks timing information.
- Store the TOTP secret encrypted at rest, not in a plaintext database column. If you use Django, `django-encrypted-model-fields` or application-level envelope encryption work. A leaked TOTP secret table is a complete MFA bypass.
- The backup code alphabet excludes ambiguous characters (`0/O`, `1/I/L`) to avoid transcription errors when users type from a printed sheet.
- `secrets.choice` is the correct function for selecting from an alphabet with CSPRNG. Never `random.choice`.

## Tests to write
- Round-trip: enroll, generate a code with `pyotp.TOTP(secret).now()`, verify it returns `True`.
- Window boundary: generate a code, mock time forward by 31 seconds, verify it still passes (within +1 window).
- Expired code: mock time forward by 61 seconds, verify rejection.
- Wrong code: verify that `"000000"` against a random secret returns `False`.
- Backup code round-trip: generate codes, redeem one, confirm success. Redeem the same code again, confirm failure.
- Backup code case insensitivity: generate a code, redeem its lowercase version, confirm success.
- Entropy: generate 100 secrets, confirm all are unique and 32 characters long.
