---
schema_version: 1
archetype: concurrency/deadlock-prevention
language: java
principles_file: _principles.md
libraries:
  preferred: java.util.concurrent.locks (ReentrantLock)
  acceptable:
    - java.util.concurrent (Semaphore)
    - Spring Retry (database deadlock retry)
  avoid:
    - name: synchronized (this) or synchronized (publicField)
      reason: Synchronising on publicly accessible objects allows external code to acquire the same monitor and create lock cycles.
minimum_versions:
  java: "21"
---

# Deadlock Prevention — Java

## Library choice
`ReentrantLock` from `java.util.concurrent.locks` supports `tryLock(timeout, unit)`, which is the primary tool for bounded lock acquisition. `Semaphore` is useful when multiple permits model concurrent access. `synchronized` blocks are simpler but do not support timeouts. For database deadlock retry, Spring Retry with `@Retryable` and a custom `RetryPolicy` that matches the SQL error code is the standard pattern.

## Reference implementation
```java
import java.util.concurrent.TimeUnit;
import java.util.concurrent.locks.ReentrantLock;

public final class TransferService {
    // Rank 1 < Rank 2 — always acquire lockA before lockB.
    private final ReentrantLock lockA = new ReentrantLock(); // rank 1
    private final ReentrantLock lockB = new ReentrantLock(); // rank 2

    private static final long TIMEOUT_MS = 5_000;

    public void transfer() throws InterruptedException {
        if (!lockA.tryLock(TIMEOUT_MS, TimeUnit.MILLISECONDS))
            throw new IllegalStateException("Could not acquire lock A within timeout.");
        try {
            if (!lockB.tryLock(TIMEOUT_MS, TimeUnit.MILLISECONDS))
                throw new IllegalStateException("Could not acquire lock B within timeout.");
            try {
                doWork();
            } finally {
                lockB.unlock();
            }
        } finally {
            lockA.unlock();
        }
    }

    private void doWork() {
        // Keep critical section short — no I/O, no external calls.
    }
}
```

## Language-specific gotchas
- `synchronized (this)` exposes the monitor to external callers. Use a `private final Object lock = new Object()` instead.
- `ReentrantLock` is re-entrant: the same thread can acquire it multiple times without blocking. Each `lock()` must be paired with exactly one `unlock()` in a `finally` block.
- Never call `unlock()` without a preceding `lock()` — `ReentrantLock.unlock()` on an un-acquired lock throws `IllegalMonitorStateException`.
- `lockA.tryLock()` (no-arg) attempts a non-blocking acquire. In production always use `tryLock(timeout, unit)` to bound wait time.
- Making an RPC or database call while holding a `ReentrantLock` — the I/O latency is now part of the lock hold time, blocking all contenders for its duration.
- `java.lang.Thread.holdsLock(obj)` can assert lock ordering in unit tests. Use it with `assertTrue` to verify that lock A is held when lock B is acquired.

## Tests to write
- Two threads call `transfer()` simultaneously; assert both complete within 2 seconds (no deadlock).
- Acquire `lockB` manually then call `transfer()`; assert `IllegalStateException` from `lockA.tryLock` within timeout.
- `lockA.tryLock(0, MILLISECONDS)` returns false when held; assert non-blocking fast path.
- Thread.holdsLock assertion inside `doWork()`: assert `lockA` is held and `lockB` is held when `doWork` executes.
