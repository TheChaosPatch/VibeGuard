---
schema_version: 1
archetype: concurrency/deadlock-prevention
title: Deadlock Prevention
summary: Eliminating deadlocks through consistent lock ordering, bounded acquisition timeouts, and minimizing critical section scope.
applies_to: [csharp, python, go, java, kotlin, rust, c]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - deadlock
  - lock
  - ordering
  - mutex
  - semaphore
  - timeout
related_archetypes:
  - concurrency/race-conditions
references:
  owasp_asvs: V11.1
  owasp_cheatsheet: Transaction Access Control Cheat Sheet
  cwe: "833"
---

# Deadlock Prevention — Principles

## When this applies
Any code that acquires more than one lock, mutex, semaphore, database row lock, or advisory lock within a single execution path. Deadlock is a four-condition phenomenon (mutual exclusion, hold-and-wait, no preemption, circular wait); eliminating any one condition prevents it. The most pragmatic approach for application code is to eliminate circular wait through consistent lock ordering and hold-and-wait through bounded acquisition timeouts. Deadlocks manifest as hanging requests, unresponsive goroutines, or service outages with no error log — they are silent until the health check fails.

## Architectural placement
Lock acquisition order is a cross-cutting concern documented at the module or package level, not buried inside individual functions. Any code path that acquires multiple locks must acquire them in the same globally-defined order. Database-level deadlocks are handled by the database engine (it detects cycles and rolls back one transaction), but the application must catch the resulting error and retry — the retry logic belongs in the repository layer, not scattered across service code. In-process lock ordering is enforced by code review and ideally by static analysis or a lint rule.

## Principles
1. **Establish and enforce a global lock acquisition order.** Assign each lock a numeric rank. Every code path that holds multiple locks simultaneously must acquire them in ascending rank order and release them in reverse order. If lock A always precedes lock B in acquisition, A-then-B and A-then-B cannot deadlock regardless of thread interleaving.
2. **Never acquire a lock while holding another of higher rank.** If you already hold lock B (rank 2) and now need lock A (rank 1), release B first, acquire A, then re-acquire B. This is the corollary to ordering: you cannot break ordering by acquiring out-of-sequence.
3. **Use try-acquire with a timeout, not an unbounded block.** `mutex.TryLock(timeout)` returns false instead of blocking forever. On failure, release all locks held by this thread, log the contention event, and retry with backoff. A timeout converts a silent deadlock into a detectable, recoverable error.
4. **Minimise critical section scope.** Hold locks for the shortest possible duration. Move any I/O, RPC calls, or heavy computation outside the critical section. The shorter the hold time, the lower the probability of concurrent threads both blocking on each other's locks.
5. **Never call external code while holding a lock.** "External" means any code you do not own: third-party libraries, callbacks, virtual methods, event handlers. The external code may acquire its own locks, and you have no visibility into that order.
6. **Prefer lock-free or immutable data structures where throughput matters.** Lock-free queues, channels, and atomic operations on primitive types eliminate the acquisition entirely — no acquisition means no deadlock. Use these as default shared-state mechanisms and reserve mutexes for cases where they are truly necessary.
7. **Detect database deadlocks and retry.** Database engines (PostgreSQL, MySQL, SQL Server) signal deadlock victims with a specific error code. Catch it at the repository layer, wait a short random interval, and retry the transaction. Do not propagate the raw database error to the caller.

## Anti-patterns
- Acquiring locks in order determined by the arrival order of requests — two requests for resources A and B arriving in opposite orders will deadlock.
- `lock (a) { lock (b) { ... } }` in one method and `lock (b) { lock (a) { ... } }` in another — classic circular wait.
- Using `Monitor.Enter` or `pthread_mutex_lock` without a timeout in production code.
- Making an HTTP call, database query, or file I/O inside a `lock` block — the I/O latency holds the lock for an unbounded duration.
- Locking on `this` or a publicly visible object — external code can acquire the same lock and create an unintended cycle.
- Swallowing a deadlock exception from the database without retrying — the caller receives a silent failure.
- Using a single global mutex for all shared state — low deadlock risk but high contention that degrades to serial execution.

## References
- OWASP ASVS V11.1 — Business Logic Security
- OWASP Transaction Access Control Cheat Sheet
- CWE-833 — Deadlock
