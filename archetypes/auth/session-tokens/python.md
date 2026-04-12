---
schema_version: 1
archetype: auth/session-tokens
language: python
principles_file: _principles.md
libraries:
  preferred: flask (with server-side session via flask-session)
  acceptable:
    - django.contrib.sessions
    - starlette.middleware.sessions
  avoid:
    - name: Flask's default client-side signed cookies for session data
      reason: Cannot be revoked server-side. Logout only clears the client cookie.
    - name: itsdangerous for session token generation
      reason: Signed tokens are not opaque server-side sessions. Revocation requires a denylist.
minimum_versions:
  python: "3.10"
---

# Session Token Management -- Python

## Library choice
Django's `django.contrib.sessions` with a database or cache backend is the gold standard for Python web sessions -- server-side storage, opaque cookie, configurable timeouts, and built-in rotation on login via `cycle_key()`. For Flask, use `flask-session` with a Redis or database backend to move from client-side signed cookies (the default) to server-side storage. The default Flask session is a signed cookie that cannot be revoked -- it is not acceptable for session management in any app with a logout feature.

## Reference implementation
```python
import secrets
from datetime import datetime, timedelta, timezone
from dataclasses import dataclass, field

@dataclass
class Session:
    session_id: str
    user_id: str
    created_at: datetime
    last_active: datetime
    absolute_expiry: datetime
    idle_timeout: timedelta = field(default=timedelta(minutes=30))

    @property
    def is_expired(self) -> bool:
        now = datetime.now(timezone.utc)
        idle_expired = now - self.last_active > self.idle_timeout
        absolute_expired = now > self.absolute_expiry
        return idle_expired or absolute_expired

def create_session(user_id: str, store: dict) -> str:
    """Issue a new session after successful authentication."""
    session_id = secrets.token_urlsafe(32)  # 256 bits, CSPRNG
    now = datetime.now(timezone.utc)
    store[session_id] = Session(
        session_id=session_id,
        user_id=user_id,
        created_at=now,
        last_active=now,
        absolute_expiry=now + timedelta(hours=8),
    )
    return session_id

def validate_session(session_id: str, store: dict) -> Session | None:
    """Return the session if valid, None if expired or unknown."""
    session = store.get(session_id)
    if session is None or session.is_expired:
        store.pop(session_id, None)
        return None
    session.last_active = datetime.now(timezone.utc)
    return session

def destroy_session(session_id: str, store: dict) -> None:
    """Invalidate on logout -- delete server-side, caller clears cookie."""
    store.pop(session_id, None)
```

## Language-specific gotchas
- `secrets.token_urlsafe(32)` produces 256 bits of CSPRNG entropy, URL-safe base64-encoded. Never `uuid.uuid4().hex` (only 122 bits, and CPython's implementation is CSPRNG-backed but other runtimes may not be) and absolutely never `random.randint`.
- Django: call `request.session.cycle_key()` after login to rotate the session ID and prevent session fixation. This is not automatic -- you must call it explicitly.
- Flask's default `SecureCookieSession` serializes the entire session dict into a signed cookie. It is tamper-proof but not revocable. Use `flask-session` with `SESSION_TYPE = "redis"` or `"sqlalchemy"` to get server-side storage.
- Set `SESSION_COOKIE_HTTPONLY = True`, `SESSION_COOKIE_SECURE = True`, and `SESSION_COOKIE_SAMESITE = "Lax"` in Django settings. Flask equivalents: `SESSION_COOKIE_HTTPONLY`, `SESSION_COOKIE_SECURE`, `SESSION_COOKIE_SAMESITE`.
- When using Redis as the session backend, set a TTL on the Redis key equal to your absolute timeout. This provides a hard backstop even if your application-level expiry check has a bug.

## Tests to write
- Round-trip: create a session, validate it, confirm the user_id matches.
- Idle timeout: create a session, advance the clock past 30 minutes of inactivity, confirm `validate_session` returns `None` and removes the entry.
- Absolute timeout: create a session, keep it "active" but advance past 8 hours, confirm rejection.
- Logout: destroy a session, attempt to validate it, confirm `None`.
- Token entropy: generate 1000 session IDs, confirm all are unique and at least 43 characters long (256 bits in base64).
- Cookie flags (integration): in Django/Flask, inspect the `Set-Cookie` header and assert `HttpOnly`, `Secure`, and `SameSite=Lax`.
