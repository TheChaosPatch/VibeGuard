---
schema_version: 1
archetype: auth/password-reset
language: python
principles_file: _principles.md
libraries:
  preferred: secrets (stdlib)
  acceptable:
    - itsdangerous (for signed tokens without a DB store)
  avoid:
    - name: uuid.uuid4()
      reason: Not from os.urandom in all environments; use secrets.token_bytes() explicitly.
    - name: random
      reason: PRNG — not cryptographically secure.
minimum_versions:
  python: "3.10"
---

# Secure Password Reset — Python

## Library choice
Python's `secrets` module (stdlib) provides `secrets.token_bytes(32)` for generating raw tokens. Hash storage and expiry are managed in your database via SQLAlchemy or Django ORM. `itsdangerous.URLSafeTimedSerializer` is an acceptable alternative that encodes expiry into the signed token itself, eliminating the database row — but it cannot support single-use guarantees without a denylist store, so the DB approach is preferred.

## Reference implementation
```python
import hashlib, secrets
from datetime import datetime, timedelta, UTC
from dataclasses import dataclass

TOKEN_BYTES = 32
EXPIRY_MINUTES = 30

@dataclass
class ResetToken:
    user_id: int
    token_hash: str
    expires_at: datetime
    consumed: bool = False

async def request_reset(db, email_address: str, send_email) -> None:
    user = await db.get_user_by_email(email_address)
    if user is None:
        return
    await db.invalidate_reset_tokens(user.id)
    raw = secrets.token_bytes(TOKEN_BYTES)
    token_hash = hashlib.sha256(raw).hexdigest()
    await db.create_reset_token(ResetToken(
        user_id=user.id, token_hash=token_hash,
        expires_at=datetime.now(UTC) + timedelta(minutes=EXPIRY_MINUTES),
    ))
    await send_email(user.email, f"https://app.example.com/reset?token={raw.hex()}")

async def redeem_reset(db, token_hex: str, new_password: str, hash_password) -> bool:
    try:
        raw = bytes.fromhex(token_hex)
    except ValueError:
        return False
    token_hash = hashlib.sha256(raw).hexdigest()
    record = await db.get_reset_token(token_hash)
    if record is None or record.consumed or record.expires_at < datetime.now(UTC):
        return False
    await db.consume_reset_token(token_hash)
    await db.update_password(record.user_id, hash_password(new_password))
    await db.invalidate_all_sessions(record.user_id)
    return True
```

## Language-specific gotchas
- `secrets.token_bytes(32)` draws from `os.urandom` — cryptographically secure on all modern platforms. `secrets.token_hex(32)` returns 64 hex characters (32 bytes of entropy) and is convenient for URLs, but store only the SHA-256 hash.
- `bytes.fromhex(token_hex)` raises `ValueError` on non-hex input. Catch it and return `False` (or 400) rather than letting it propagate as a 500.
- Django's built-in `PasswordResetTokenGenerator` (from `django.contrib.auth.tokens`) encodes a timestamp and the user's last login and password hash — it is single-use by design and does not need a separate database table. Use it when on Django.
- `itsdangerous.URLSafeTimedSerializer` produces signed, time-limited tokens without a DB table, but signing is HMAC over a server secret — if the secret leaks, all tokens can be forged. It also cannot be invalidated after issuance without a denylist.
- Log the `user_id` on both request and redemption. Never log the raw `token_hex`.

## Tests to write
- `request_reset` for an unknown email returns `None` and sends no email.
- `redeem_reset` with a valid token returns `True` and marks the record consumed.
- Second call to `redeem_reset` with the same token returns `False`.
- `redeem_reset` with an expired token returns `False`.
- `redeem_reset` with garbage hex input returns `False` without raising.
