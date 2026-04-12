---
schema_version: 1
archetype: concurrency/resource-exhaustion
language: kotlin
principles_file: _principles.md
libraries:
  preferred: kotlinx.coroutines (Channel, Semaphore)
  acceptable:
    - Resilience4j Kotlin extensions
  avoid:
    - name: GlobalScope.launch per request
      reason: GlobalScope launches are not bounded and not supervised; a burst of requests creates unbounded coroutines and leaks them if the caller is cancelled.
minimum_versions:
  kotlin: "2.1"
  jvm: "21"
---

# Resource Exhaustion Prevention — Kotlin

## Library choice
`kotlinx.coroutines.sync.Semaphore` gates concurrent coroutine execution. `kotlinx.coroutines.channels.Channel` with a bounded capacity provides a producer-consumer queue with backpressure. `CoroutineScope` with structured concurrency ensures all child coroutines are tracked and cancelled when the scope is cancelled. Resilience4j's Kotlin extensions provide bulkhead, rate limiter, and retry patterns.

## Reference implementation
```kotlin
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

private val DB_SEMAPHORE = Semaphore(20)
private val WORK_CHANNEL = Channel<String>(capacity = 200) // bounded

suspend fun <T> queryDb(fn: suspend () -> T): T =
    DB_SEMAPHORE.withPermit { fn() }

suspend fun enqueue(item: String) {
    // send suspends if channel is full — backpressure applied to producer.
    withTimeout(5_000L) {
        WORK_CHANNEL.send(item)
    }
}

// Worker launched inside a supervised scope — not GlobalScope.
fun CoroutineScope.startWorker(): Job = launch {
    for (item in WORK_CHANNEL) {
        try {
            processItem(item)
        } catch (e: Exception) {
            // Log and continue; do not let one failure kill the worker loop.
        }
    }
}

suspend fun processItem(item: String) { /* business logic */ }
```

## Language-specific gotchas
- `GlobalScope.launch { }` creates a coroutine outside any structured scope. If the caller is cancelled, the `GlobalScope` coroutine continues running. Use the caller's `CoroutineScope` or a service-level scope created with `CoroutineScope(SupervisorJob() + Dispatchers.Default)`.
- `Channel<T>()` (no capacity argument) creates a `Rendezvous` channel with capacity 0 — the producer suspends until a consumer is ready. This is very strict backpressure. Use `Channel(capacity = N)` for a buffered channel.
- `Channel.UNLIMITED` capacity creates an unbounded channel. Never use this for external input without rate limiting.
- `Semaphore(20).withPermit { }` is the safe API — it releases the permit even if the block throws. Manual `acquire()`/`release()` pairs are error-prone.
- `Dispatchers.IO` has a thread pool with a default soft limit of 64 threads. Launching coroutines that block JVM threads (legacy JDBC, `Thread.sleep`) on `Dispatchers.IO` still consumes real threads; limit via a custom `limitedParallelism(N)` dispatcher.

## Tests to write
- 21 coroutines call `queryDb` simultaneously with `Semaphore(20)`; assert the 21st times out with `TimeoutCancellationException`.
- `enqueue` 201 items; assert the 201st `send` suspends and the `withTimeout` triggers `TimeoutCancellationException`.
- `startWorker` in a `runTest` scope: enqueue 10 items; assert all are processed and the channel is empty.
- Cancellation: cancel the worker scope; assert the `for (item in WORK_CHANNEL)` loop exits cleanly.
