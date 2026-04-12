---
schema_version: 1
archetype: concurrency/deadlock-prevention
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Threading (Monitor, SemaphoreSlim, Mutex)
  acceptable:
    - System.Collections.Concurrent
    - Microsoft.EntityFrameworkCore (retry on deadlock)
  avoid:
    - name: lock (this) or lock (publicField)
      reason: Locking on publicly visible objects allows external code to acquire the same lock and create unintended cycles.
minimum_versions:
  dotnet: "10.0"
---

# Deadlock Prevention — C#

## Library choice
The `lock` statement (backed by `Monitor.Enter`/`Monitor.Exit`) is the idiomatic in-process mutual exclusion primitive. `SemaphoreSlim` is preferred in async code because it can be awaited without blocking a thread. For database-level deadlocks, configure EF Core's execution strategy with retry-on-deadlock logic. `System.Collections.Concurrent` types (e.g., `ConcurrentDictionary`) eliminate the need for explicit locks over shared collections.

## Reference implementation
```csharp
using System.Threading;

// Define lock rank as a comment-enforced convention.
// Lock A (rank 1) must always be acquired before Lock B (rank 2).
public sealed class TransferService
{
    // Rank 1 — always acquired first.
    private readonly SemaphoreSlim _lockA = new(1, 1);
    // Rank 2 — acquired only while holding _lockA, or independently.
    private readonly SemaphoreSlim _lockB = new(1, 1);

    private const int AcquireTimeoutMs = 5_000;

    public async Task TransferAsync(CancellationToken ct)
    {
        // Acquire in rank order: A then B.
        if (!await _lockA.WaitAsync(AcquireTimeoutMs, ct))
            throw new TimeoutException("Could not acquire lock A within timeout.");
        try
        {
            if (!await _lockB.WaitAsync(AcquireTimeoutMs, ct))
                throw new TimeoutException("Could not acquire lock B within timeout.");
            try
            {
                await DoWorkAsync(ct);
            }
            finally { _lockB.Release(); }
        }
        finally { _lockA.Release(); }
    }

    private async Task DoWorkAsync(CancellationToken ct)
    {
        // No I/O calls here that could introduce external lock ordering.
        await Task.CompletedTask;
    }
}
```

## Language-specific gotchas
- `lock (this)` is equivalent to `Monitor.Enter(this)`. Any external code that holds a reference to the same object can call `Monitor.Enter` on it too, creating an unintended lock cycle. Use a private `readonly object _lock = new()` field instead.
- `async/await` and `Monitor.Enter` do not mix. A thread that enters a `lock` block and then does `await` may resume on a different thread after the await, which then tries to `Monitor.Exit` on the wrong thread. Use `SemaphoreSlim.WaitAsync` for async critical sections.
- `SemaphoreSlim.Wait()` without a timeout blocks the calling thread indefinitely. Always use `WaitAsync(timeout, ct)` with both a timeout and a `CancellationToken`.
- EF Core's `EnableRetryOnFailure` in `UseSqlServer` retries transient errors including deadlock victims (error 1205). Configure it on the execution strategy; do not write manual retry loops.
- Calling a virtual method or raising an event while holding a lock is calling unknown external code — a source of lock-order inversion.

## Tests to write
- Acquire `_lockA` and `_lockB` in rank order from two concurrent tasks; assert both complete without deadlock within 2 seconds.
- Acquire `_lockB` then attempt `_lockA` from a second task; assert `TimeoutException` is thrown (demonstrates rank inversion is detected).
- `SemaphoreSlim.WaitAsync(0)` returns false when the semaphore is already held — assert non-blocking fast path.
- EF Core retry: simulate a SQL error 1205; assert the execution strategy retries and the operation eventually succeeds.
