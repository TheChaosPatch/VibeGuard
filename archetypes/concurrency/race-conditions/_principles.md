---
schema_version: 1
archetype: concurrency/race-conditions
title: Race Condition Defense
summary: Preventing TOCTOU, double-spend, and check-then-act bugs through proper synchronization.
applies_to: [csharp, python, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - race-condition
  - toctou
  - double-spend
  - concurrency
  - locking
  - optimistic
  - pessimistic
  - idempotency
  - mutex
  - atomicity
  - check-then-act
  - distributed-lock
related_archetypes:
  - persistence/sql-injection
  - persistence/orm-security
references:
  owasp_asvs: V11.1
  owasp_cheatsheet: Transaction Access Control Cheat Sheet
  cwe: "367"
---

# Race Condition Defense — Principles

## When this applies
Any operation where the outcome depends on the relative timing of two or more concurrent actions — two HTTP requests hitting the same endpoint, two background workers processing the same queue message, two threads accessing shared state, or a read-then-write sequence where the value can change between the read and the write. Race conditions are not exotic concurrency-theory problems; they are the number-one source of double-charges, double-enrollments, privilege-escalation-via-retry, and inventory oversell in production systems. If your operation reads a value, makes a decision based on it, and then writes — and another request can do the same thing between your read and your write — you have a race condition.

## Architectural placement
The synchronization boundary lives at the data layer, not the application layer. Database-level constraints (unique indexes, row-level locks, version columns) are the primary defense because they serialize access regardless of how many application instances are running. Application-level mutexes protect in-process state but are invisible to other instances. Distributed locks (Redis, ZooKeeper) coordinate across instances but add operational complexity and must handle lock expiry, fencing tokens, and split-brain. The choice depends on the deployment topology, but the principle is the same: the thing that enforces mutual exclusion must see all competing writers.

## Principles
1. **Make the database the arbiter.** A `UNIQUE` constraint, a `SELECT ... FOR UPDATE`, or an optimistic concurrency check (`WHERE version = @expected`) enforced by the database is correct even when you add a second application instance. An in-process `lock` statement is not.
2. **Optimistic concurrency for read-heavy workflows.** Add a `version` or `rowversion` column. Read the version with the data, include it in the `WHERE` clause of the update, and handle the "zero rows affected" case as a concurrency conflict. This avoids holding locks during user think-time.
3. **Pessimistic locking for short, write-heavy critical sections.** `SELECT ... FOR UPDATE` (or equivalent) holds a row lock for the duration of the transaction. Use it when conflicts are frequent and retries are expensive. Keep the transaction short — seconds, not minutes.
4. **Idempotency keys for external-facing mutations.** Every create or payment endpoint accepts a client-supplied idempotency key. The server stores the key with the result and returns the stored result on replay. This turns a retry into a no-op instead of a double-charge. The key is enforced by a unique index, not by an application-level cache.
5. **Never check-then-act without holding the lock through the act.** `if (!exists) { create(); }` is a TOCTOU bug. The check and the act must be atomic — use `INSERT ... ON CONFLICT DO NOTHING`, `MERGE`, or a database-level unique constraint that rejects the duplicate.
6. **Distributed locks need fencing tokens.** A lock held in Redis expires after a TTL. If the holder is paused by GC or network delay past the TTL, a second holder acquires the lock, and now both are inside the critical section. Fencing tokens — monotonically increasing values passed to the downstream resource — let the resource reject stale writes.
7. **Test with concurrency, not just correctness.** A test that calls the endpoint once and asserts success does not find race conditions. A test that calls the endpoint 50 times concurrently with the same idempotency key and asserts exactly one success does.

## Anti-patterns
- `if (await repo.ExistsAsync(id)) return; await repo.CreateAsync(entity);` — classic TOCTOU. Two requests pass the check simultaneously.
- Using an in-memory `HashSet` as a deduplication guard in a horizontally-scaled service. The second instance has its own empty set.
- Catching the unique-constraint violation and silently swallowing it instead of returning the existing resource or a conflict status.
- Holding a database transaction open while waiting for an external HTTP call. Lock duration is now bounded by the slowest third-party API.
- Using `Thread.Sleep` or `time.sleep` as a "retry backoff" after a concurrency conflict without a maximum retry count — infinite retry loops under contention.
- Implementing distributed locks without TTL expiry. A crashed holder permanently blocks the resource.
- Trusting application-layer rate limiting to prevent double-submit. Rate limiting throttles volume; it does not serialize access.
- Reading a balance, computing a deduction in application code, and writing the new balance — instead of `UPDATE accounts SET balance = balance - @amount WHERE balance >= @amount`.

## References
- OWASP ASVS V11.1 — Business Logic Security
- OWASP Transaction Access Control Cheat Sheet
- CWE-367 — Time-of-check Time-of-use (TOCTOU) Race Condition
