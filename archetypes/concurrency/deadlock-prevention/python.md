---
schema_version: 1
archetype: concurrency/deadlock-prevention
language: python
principles_file: _principles.md
libraries:
  preferred: threading (stdlib)
  acceptable:
    - asyncio.Lock
    - multiprocessing.Lock
  avoid:
    - name: threading.Lock without timeout
      reason: threading.Lock().acquire() with no timeout blocks forever; use acquire(timeout=N) to detect deadlocks.
minimum_versions:
  python: "3.12"
---

# Deadlock Prevention — Python

## Library choice
`threading.RLock` and `threading.Lock` for multi-threaded code; `asyncio.Lock` for async code (cannot be awaited with a timeout directly — use `asyncio.wait_for`). For database deadlocks, SQLAlchemy's `create_engine(pool_pre_ping=True)` plus a retry decorator handles PostgreSQL deadlock errors (pgcode `40P01`).

## Reference implementation
```python
from __future__ import annotations
import threading
import logging

log = logging.getLogger(__name__)

# Rank 1 < Rank 2 — always acquire in this order.
_lock_a = threading.Lock()  # rank 1
_lock_b = threading.Lock()  # rank 2

ACQUIRE_TIMEOUT = 5.0  # seconds


def transfer() -> None:
    """Acquire A then B — rank order prevents circular wait."""
    if not _lock_a.acquire(timeout=ACQUIRE_TIMEOUT):
        raise TimeoutError("Could not acquire lock A within timeout.")
    try:
        if not _lock_b.acquire(timeout=ACQUIRE_TIMEOUT):
            raise TimeoutError("Could not acquire lock B within timeout.")
        try:
            _do_work()
        finally:
            _lock_b.release()
    finally:
        _lock_a.release()


def _do_work() -> None:
    # No blocking I/O here. Keep critical sections short.
    pass
```

## Language-specific gotchas
- `threading.Lock().acquire()` with no arguments blocks forever. Always pass `timeout=` in production code.
- `threading.RLock` (re-entrant lock) allows the same thread to acquire the lock multiple times. This is useful for recursive algorithms but hides rank-ordering bugs — a thread can acquire A and B in opposite orders if both happen in the same call stack via recursion.
- `asyncio.Lock` cannot be used with a timeout directly. Wrap the await: `await asyncio.wait_for(lock.acquire(), timeout=5.0)`. Remember to call `lock.release()` in a `finally` block; `async with` on an `asyncio.Lock` does not support timeout.
- GIL (Global Interpreter Lock) in CPython serialises Python bytecode execution in a single process but does not protect against deadlocks in multi-threaded code — the GIL is released during I/O and C extensions.
- `multiprocessing.Lock` uses OS-level semaphores. A process that crashes while holding the lock leaves it permanently locked on some platforms (Linux). Use `multiprocessing.Manager().Lock()` with a heartbeat mechanism for robustness.

## Tests to write
- `transfer()` from two threads simultaneously; assert both complete within 2 seconds with no deadlock.
- Acquire `_lock_b` first, then attempt `_lock_a.acquire(timeout=0.1)`; assert `TimeoutError` is raised (detects rank inversion without blocking).
- `_lock_a.acquire(timeout=0)` returns `False` when held; assert non-blocking fast path.
- SQLAlchemy retry: mock a `psycopg2.extensions.TransactionRollbackError` with pgcode `40P01`; assert the decorated function retries and eventually succeeds.
