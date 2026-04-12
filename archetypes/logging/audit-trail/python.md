---
schema_version: 1
archetype: logging/audit-trail
language: python
principles_file: _principles.md
libraries:
  preferred: Custom audit service over append-only table
  acceptable:
    - structlog (dedicated audit logger)
    - SQLAlchemy event listeners
  avoid:
    - name: logging.getLogger for audit events
      reason: Application logs are rotated and dropped under load. Audit events must be durable.
    - name: print or f-string audit lines
      reason: No structure, no sink control, no tamper evidence.
minimum_versions:
  python: "3.10"
---

# Security Audit Trail — Python

## Library choice
Audit events flow through a dedicated `AuditWriter` class, not through `structlog` or the stdlib `logging` module. The writer inserts structured events into an append-only database table (the database user has INSERT but not UPDATE or DELETE). For tamper evidence, each event includes an HMAC chain linking it to the previous event. `structlog` with a dedicated audit processor and a durable sink is an acceptable secondary output. SQLAlchemy event listeners (`after_insert`, `after_update`) can supplement the audit trail but must not be the primary mechanism — they can be bypassed by raw SQL.

## Reference implementation
```python
from __future__ import annotations
import hashlib, hmac
from dataclasses import dataclass, field
from datetime import datetime, timezone
from uuid import uuid4
from sqlalchemy import String, insert
from sqlalchemy.orm import Session, Mapped, mapped_column, DeclarativeBase

class Base(DeclarativeBase):
    pass

class AuditEventRow(Base):
    """Append-only table — application DB user has INSERT only, no UPDATE/DELETE."""
    __tablename__ = "audit_events"
    id: Mapped[str] = mapped_column(primary_key=True)
    actor_id: Mapped[str]; action: Mapped[str]; timestamp: Mapped[datetime]
    correlation_id: Mapped[str]; outcome: Mapped[str]
    target_id: Mapped[str | None]; detail: Mapped[str | None]
    previous_hash: Mapped[str] = mapped_column(String(64))
    hash: Mapped[str] = mapped_column(String(64))

@dataclass(frozen=True, slots=True)
class AuditEvent:
    actor_id: str; action: str; correlation_id: str
    outcome: str  # "success" | "denied" | "failure"
    target_id: str | None = None; detail: str | None = None
    id: str = field(default_factory=lambda: str(uuid4()))
    timestamp: datetime = field(default_factory=lambda: datetime.now(timezone.utc))

class AuditWriter:
    def __init__(self, session: Session, hmac_key: bytes) -> None:
        self._session, self._hmac_key = session, hmac_key
        self._prev = "0" * 64

    def write(self, ev: AuditEvent) -> None:
        payload = f"{ev.id}|{ev.actor_id}|{ev.action}|{ev.outcome}"
        h = hmac.new(self._hmac_key, (self._prev + payload).encode(), hashlib.sha256).hexdigest()
        self._session.execute(insert(AuditEventRow).values(
            id=ev.id, actor_id=ev.actor_id, action=ev.action, timestamp=ev.timestamp,
            correlation_id=ev.correlation_id, outcome=ev.outcome,
            target_id=ev.target_id, detail=ev.detail, previous_hash=self._prev, hash=h,
        ))
        self._session.flush()  # fail here = fail the business operation
        self._prev = h
```

## Language-specific gotchas
- `datetime.utcnow()` is deprecated in Python 3.12+. Use `datetime.now(timezone.utc)` for timezone-aware UTC timestamps. Naive datetimes lose their meaning when the audit log is queried from a different timezone.
- The database user for the audit table must have INSERT-only grants. If the application can `DELETE FROM audit_events`, a compromised application can erase evidence. Use a separate SQLAlchemy engine with restricted credentials.
- `session.flush()` must not be wrapped in a `try/except` that silently swallows the error. If the audit write fails, the business operation must also fail. This is the critical difference between audit logging and application logging.
- Do not log PII in the `detail` field. Record `actor_id: "usr_abc123"`, not `actor_name: "Jane Smith"`. Resolve the mapping at query time through the identity service.
- SQLAlchemy's `after_insert` / `after_update` event listeners fire on ORM operations but not on raw SQL (`session.execute(text(...))`). They are a convenience supplement, not the primary audit mechanism.
- The HMAC key must come from a secrets manager, not from environment variables or config files checked into VCS. If the key is leaked, an attacker can forge valid hashes.
- `structlog` can emit audit events to a durable sink (a file with fsync, a message queue), but it must be a separate logger instance with a separate processor chain. Do not mix audit events with application debug logs.

## Tests to write
- Required fields: constructing an `AuditEvent` without `actor_id`, `action`, `correlation_id`, or `outcome` raises `TypeError`.
- Hash chain: write two events — assert the second event's `previous_hash` equals the first event's `hash`.
- Tamper detection: modify a stored event's `detail` column directly — recompute the HMAC and assert it no longer matches the stored `hash`.
- Insert-only: attempt a `DELETE` on the audit table with the application's database user — assert a permission error.
- Audit failure propagation: mock `session.flush` to raise — assert the calling business function also raises (the audit write is not fire-and-forget).
