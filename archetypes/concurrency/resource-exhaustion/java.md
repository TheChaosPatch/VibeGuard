---
schema_version: 1
archetype: concurrency/resource-exhaustion
language: java
principles_file: _principles.md
libraries:
  preferred: java.util.concurrent (ThreadPoolExecutor, LinkedBlockingQueue)
  acceptable:
    - Resilience4j (Bulkhead, RateLimiter)
  avoid:
    - name: Executors.newCachedThreadPool()
      reason: Creates an unbounded number of threads; under load it spawns thousands of threads and exhausts OS resources.
minimum_versions:
  java: "21"
---

# Resource Exhaustion Prevention — Java

## Library choice
`ThreadPoolExecutor` with a `LinkedBlockingQueue(capacity)` provides a bounded thread pool with backpressure. `Resilience4j`'s `Bulkhead` and `ThreadPoolBulkhead` patterns apply the same principle at the library level with metrics and fallback support. `Executors.newFixedThreadPool(n)` uses an unbounded `LinkedBlockingQueue` internally — use `ThreadPoolExecutor` directly to set queue capacity.

## Reference implementation
```java
import java.util.concurrent.*;

public final class BoundedExecutorService {
    private final ThreadPoolExecutor executor;

    public BoundedExecutorService(int maxThreads, int queueCapacity) {
        this.executor = new ThreadPoolExecutor(
            2,                              // corePoolSize
            maxThreads,                     // maximumPoolSize
            60L, TimeUnit.SECONDS,          // keepAliveTime
            new LinkedBlockingQueue<>(queueCapacity), // bounded queue
            new ThreadPoolExecutor.CallerRunsPolicy() // backpressure: caller blocks
        );
    }

    public Future<?> submit(Runnable task) {
        return executor.submit(task);
    }

    public void shutdown() throws InterruptedException {
        executor.shutdown();
        if (!executor.awaitTermination(30, TimeUnit.SECONDS)) {
            executor.shutdownNow();
        }
    }

    public int getQueueSize() { return executor.getQueue().size(); }
    public int getActiveCount() { return executor.getActiveCount(); }
}
```

## Language-specific gotchas
- `Executors.newCachedThreadPool()` creates threads without bound. Under load it can spawn tens of thousands of threads, exhausting OS limits and causing `OutOfMemoryError`. Never use in production services.
- `Executors.newFixedThreadPool(n)` uses `new LinkedBlockingQueue<>()` (unbounded) internally. Prefer `ThreadPoolExecutor` with an explicit queue capacity.
- `CallerRunsPolicy` makes the submitting thread execute the task when the queue is full, providing natural backpressure to HTTP request handlers. `AbortPolicy` throws `RejectedExecutionException` — catch it and return a `503`.
- Virtual threads (Project Loom, Java 21): `Executors.newVirtualThreadPerTaskExecutor()` creates a virtual thread per task — still not bounded. Use a semaphore in front of the virtual thread executor to limit concurrency.
- `ThreadPoolExecutor.prestartAllCoreThreads()` starts the core threads immediately at construction — useful to validate the pool can be created at startup rather than at first request.
- Shutting down with `shutdownNow()` interrupts in-progress tasks. Ensure tasks respond to `Thread.isInterrupted()` or `InterruptedException` for clean cancellation.

## Tests to write
- Submit `maximumPoolSize + queueCapacity + 1` tasks simultaneously; assert the surplus tasks are handled by `CallerRunsPolicy` (caller thread executes them) rather than being rejected.
- `getActiveCount()` and `getQueueSize()` metrics: submit known number of tasks; assert reported values match.
- `shutdown()`: submit tasks, call `shutdown()`, assert all tasks complete within the termination timeout.
- Resilience4j `Bulkhead`: configure `maxConcurrentCalls=5`; submit 6 concurrent calls; assert the 6th receives `BulkheadFullException`.
