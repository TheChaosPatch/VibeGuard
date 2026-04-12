---
schema_version: 1
archetype: concurrency/deadlock-prevention
language: kotlin
principles_file: _principles.md
libraries:
  preferred: kotlinx.coroutines (Mutex)
  acceptable:
    - java.util.concurrent.locks.ReentrantLock
  avoid:
    - name: kotlinx.coroutines.sync.Mutex.lock() without withTimeout
      reason: Mutex.lock() suspends indefinitely; always wrap with withTimeout to detect deadlocks in coroutine code.
minimum_versions:
  kotlin: "2.1"
  jvm: "21"
---

# Deadlock Prevention — Kotlin

## Library choice
`kotlinx.coroutines.sync.Mutex` is the coroutine-safe mutex. It is a suspending primitive — it does not block a thread. Use `withTimeout` from `kotlinx.coroutines` to bound acquisition time. For JVM-thread-level locking (outside coroutines), `java.util.concurrent.locks.ReentrantLock` with `tryLock(timeout, unit)` is available.

## Reference implementation
```kotlin
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.TimeoutCancellationException

// Rank 1 < Rank 2 — always acquire mutexA before mutexB.
val mutexA = Mutex() // rank 1
val mutexB = Mutex() // rank 2

private const val LOCK_TIMEOUT_MS = 5_000L

suspend fun transfer() {
    try {
        withTimeout(LOCK_TIMEOUT_MS) {
            mutexA.withLock {
                withTimeout(LOCK_TIMEOUT_MS) {
                    mutexB.withLock {
                        doWork()
                    }
                }
            }
        }
    } catch (e: TimeoutCancellationException) {
        throw IllegalStateException("Could not acquire lock within timeout.", e)
    }
}

private suspend fun doWork() {
    // No blocking calls here. Suspend functions only.
}
```

## Language-specific gotchas
- `kotlinx.coroutines.sync.Mutex` is NOT re-entrant. A coroutine that calls `mutex.withLock { mutex.withLock { } }` deadlocks immediately. Design the critical section to not re-acquire.
- `withTimeout` cancels the coroutine with `TimeoutCancellationException` — catch it to convert to a domain error, as uncaught cancellation propagates to the parent coroutine scope.
- `Mutex.withLock { }` is the safe wrapper for `lock()/unlock()`. Using `lock()` and `unlock()` directly risks failing to unlock if the body throws.
- Coroutine cancellation may interrupt a suspended `Mutex.lock()`. The lock is not acquired in that case — no cleanup of a held lock is needed.
- For JVM thread locking in Kotlin (e.g., interfacing with Java libraries), use `java.util.concurrent.locks.ReentrantLock` and follow the Java patterns. `@Synchronized` (Kotlin annotation) is equivalent to `synchronized(this)` — avoid it for the same reason.

## Tests to write
- Two coroutines call `transfer()` concurrently; assert both complete within 2 seconds using `runTest`.
- Manually hold `mutexA` and call `transfer()` within a `withTimeout(100)`; assert `TimeoutCancellationException` is thrown.
- `mutexA.tryLock()` returns false when held from another coroutine; assert non-blocking.
- Verify no re-entrant acquisition: assert `mutexA.withLock { mutexA.tryLock() }` returns false (would deadlock if lock were attempted).
