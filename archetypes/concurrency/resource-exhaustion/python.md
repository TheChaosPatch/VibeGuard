---
schema_version: 1
archetype: concurrency/resource-exhaustion
language: python
principles_file: _principles.md
libraries:
  preferred: asyncio (BoundedSemaphore, Queue)
  acceptable:
    - anyio
    - concurrent.futures.ThreadPoolExecutor
  avoid:
    - name: threading.Thread per request
      reason: Creates an OS thread per request; under load exhausts process file descriptors and OS thread limits before any work limit is reached.
minimum_versions:
  python: "3.12"
---

# Resource Exhaustion Prevention — Python

## Library choice
`asyncio.BoundedSemaphore` gates concurrent async operations. `asyncio.Queue(maxsize=N)` provides a bounded producer-consumer queue with backpressure — `put` blocks when the queue is full. For CPU-bound work offloaded from async code, `concurrent.futures.ProcessPoolExecutor` (not `ThreadPoolExecutor`) bypasses the GIL. For thread-based servers, `concurrent.futures.ThreadPoolExecutor(max_workers=N)` bounds the thread count.

## Reference implementation
```python
from __future__ import annotations
import asyncio
from typing import Awaitable, Callable, TypeVar

T = TypeVar("T")

MAX_CONCURRENT_DB = 20
DB_GATE = asyncio.BoundedSemaphore(MAX_CONCURRENT_DB)
ACQUIRE_TIMEOUT = 5.0  # seconds

WORK_QUEUE: asyncio.Queue[str] = asyncio.Queue(maxsize=200)


async def query_db(fn: Callable[[], Awaitable[T]]) -> T:
    """Gate concurrent database calls to MAX_CONCURRENT_DB."""
    try:
        await asyncio.wait_for(DB_GATE.acquire(), timeout=ACQUIRE_TIMEOUT)
    except asyncio.TimeoutError:
        raise RuntimeError("Database concurrency limit reached.") from None
    try:
        return await fn()
    finally:
        DB_GATE.release()


async def enqueue(item: str) -> None:
    """Enqueue with backpressure — waits if queue is full."""
    await asyncio.wait_for(WORK_QUEUE.put(item), timeout=ACQUIRE_TIMEOUT)


async def worker() -> None:
    """Consume items from the bounded queue indefinitely."""
    while True:
        item = await WORK_QUEUE.get()
        try:
            await process(item)
        finally:
            WORK_QUEUE.task_done()


async def process(item: str) -> None:
    pass
```

## Language-specific gotchas
- `asyncio.BoundedSemaphore` raises `ValueError` if `release()` is called more times than `acquire()`. Use `try/finally` to ensure balanced acquire/release.
- `asyncio.Queue(maxsize=0)` is unbounded — always pass an explicit positive `maxsize` in production.
- CPU-bound code in an `async` function blocks the event loop. Offload with `loop.run_in_executor(pool, fn)` using a `ProcessPoolExecutor`, not `ThreadPoolExecutor`, to bypass the GIL.
- `ThreadPoolExecutor(max_workers=None)` defaults to `min(32, os.cpu_count() + 4)` in Python 3.8+. This may be too high for I/O-bound work on machines with many cores. Set `max_workers` explicitly.
- `asyncio.wait_for` cancels the coroutine on timeout with `asyncio.CancelledError`. Ensure `finally` blocks release the semaphore correctly even when cancelled.

## Tests to write
- Fill `WORK_QUEUE` to `maxsize`; call `enqueue` with a timeout of 0.1 s; assert `asyncio.TimeoutError` is raised.
- Call `query_db` 21 times concurrently with `MAX_CONCURRENT_DB = 20`; assert the 21st raises `RuntimeError` within the timeout.
- Worker drains the queue: enqueue 10 items, start a worker, call `await WORK_QUEUE.join()`; assert it completes.
- Thread pool bound: assert `ThreadPoolExecutor(max_workers=10)` rejects tasks above 10 concurrent with a `Future` that eventually completes (not hangs indefinitely).
