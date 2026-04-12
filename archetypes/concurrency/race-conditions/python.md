---
schema_version: 1
archetype: concurrency/race-conditions
language: python
principles_file: _principles.md
libraries:
  preferred: SQLAlchemy (optimistic/pessimistic locking)
  acceptable:
    - Django ORM (select_for_update)
    - redis (distributed locks)
  avoid:
    - name: threading.Lock for cross-process coordination
      reason: Only protects the current process. Multiple workers have separate locks.
    - name: Check-then-act without a transaction
      reason: Classic TOCTOU. The check and act must be atomic.
minimum_versions:
  python: "3.10"
---

# Race Condition Defense — Python

## Library choice
SQLAlchemy 2.x provides both optimistic concurrency (via `version_id_col`) and pessimistic locking (via `with_for_update()`). Django's ORM supports `select_for_update()`. For distributed locking across workers or services, the `redis` package with a Redlock implementation or PostgreSQL advisory locks are the standard options. Python's `threading.Lock` and `asyncio.Lock` protect only the current process — with gunicorn, uvicorn workers, or Celery, each process has its own lock and the invariant is unprotected.

## Reference implementation
```python
from __future__ import annotations
from dataclasses import dataclass
from uuid import uuid4
from sqlalchemy import select, String
from sqlalchemy.orm import Session, Mapped, mapped_column, DeclarativeBase

class Base(DeclarativeBase):
    pass

class Account(Base):
    __tablename__ = "accounts"
    id: Mapped[str] = mapped_column(primary_key=True)
    balance: Mapped[int]  # cents, not float
    version: Mapped[int] = mapped_column(default=1)
    __mapper_args__ = {"version_id_col": version}

class Payment(Base):
    __tablename__ = "payments"
    id: Mapped[str] = mapped_column(primary_key=True)
    account_id: Mapped[str]
    amount: Mapped[int]
    idempotency_key: Mapped[str] = mapped_column(String(64), unique=True)

@dataclass(frozen=True, slots=True)
class DebitResult:
    success: bool
    already_processed: bool = False

def debit(session: Session, account_id: str, amount: int, key: str) -> DebitResult:
    if session.execute(select(Payment).where(Payment.idempotency_key == key)).scalar_one_or_none():
        return DebitResult(success=True, already_processed=True)
    acct = session.execute(
        select(Account).where(Account.id == account_id).with_for_update()
    ).scalar_one()
    if acct.balance < amount:
        return DebitResult(success=False)
    acct.balance -= amount
    session.add(Payment(
        id=str(uuid4()), account_id=account_id,
        amount=amount, idempotency_key=key,
    ))
    session.flush()
    return DebitResult(success=True)
```

## Language-specific gotchas
- SQLAlchemy's `version_id_col` adds `WHERE version = :old_version` to every UPDATE. If another transaction modified the row, `StaleDataError` is raised. Catch it, reload, and retry with a bounded attempt count.
- `with_for_update()` acquires a `SELECT ... FOR UPDATE` row lock. The lock is held until the transaction commits or rolls back. Do not call external HTTP APIs while holding this lock — lock duration is now bounded by the slowest external service.
- `threading.Lock` in a gunicorn/uvicorn multi-worker deployment protects nothing — each worker process has its own lock. Use database-level locking or a distributed lock.
- `asyncio.Lock` protects against concurrent coroutines in a single event loop. It does not protect against concurrent workers, processes, or services.
- Python's `float` is IEEE 754 and accumulates rounding errors. Store monetary amounts as integer cents (`amount: Mapped[int]`), never as `float` or `Decimal` with uncontrolled precision. A rounding error in a balance check is a financial bug.
- The idempotency key must be enforced by a database unique constraint, not by an in-memory set. The in-memory set is empty after a process restart and invisible to other workers.
- Django's `select_for_update()` requires an active transaction. In Django's default autocommit mode, you must wrap the view in `@transaction.atomic` or the lock is released immediately.

## Tests to write
- Double-debit: run `debit()` concurrently from two threads with different keys on an account that can only afford one — assert exactly one succeeds.
- Idempotency: call `debit()` twice with the same key — assert the balance is debited once and the second call returns `already_processed=True`.
- Optimistic conflict: load an account, modify it in a separate session, then flush the first — assert `StaleDataError`.
- Concurrent inserts: insert 50 payments with the same idempotency key in parallel using a thread pool — assert exactly one row exists.
