---
schema_version: 1
archetype: concurrency/resource-exhaustion
title: Resource Exhaustion Prevention
summary: Preventing thread pool starvation, connection leaks, and unbounded queues through explicit capacity limits and backpressure.
applies_to: [csharp, python, go, javascript, typescript, java, kotlin]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - exhaustion
  - thread
  - pool
  - starvation
  - leak
  - connection
  - backpressure
related_archetypes:
  - concurrency/race-conditions
references:
  owasp_asvs: V11.1
  owasp_cheatsheet: Denial of Service Cheat Sheet
  cwe: "400"
---

# Resource Exhaustion Prevention — Principles

## When this applies
Any service that handles concurrent requests and uses shared finite resources: thread pools, connection pools, file descriptors, goroutines, async task schedulers, or heap memory. Resource exhaustion is both a security vulnerability (an attacker can trigger it with a burst of expensive requests) and a reliability failure (legitimate traffic spikes cause cascading outages). The failure mode is typically a slow degradation: response times climb, queues fill, and eventually the process becomes unresponsive or crashes — usually at the worst time.

## Architectural placement
Capacity limits are specified at every layer that holds a finite resource: the HTTP server (max connections, request queue depth), the thread pool (min/max threads, queue capacity), the connection pool (min/max connections, acquire timeout), and any internal bounded queue. Backpressure propagates upward: when the inner layer is full, the outer layer receives an error or a rejection signal and communicates it to the client with a `503 Service Unavailable` or `429 Too Many Requests`. No layer silently queues work forever. Observability — metrics on queue depth, pool utilisation, rejected requests — is a first-class requirement: you cannot tune limits you cannot measure.

## Principles
1. **Every shared resource has an explicit upper bound.** Thread pool max threads, connection pool max size, channel buffer capacity, semaphore permit count — all must be set to a finite value appropriate for the hardware. The default for most runtimes is either unbounded or a number calibrated for demo machines, not production.
2. **Reject work when the bound is reached; do not queue indefinitely.** A bounded queue with a rejection policy (`CallerRunsPolicy`, `DropWithLog`, `503`) is safe. An unbounded queue accepts all work, delays it infinitely, and exhausts heap memory. The bound converts exhaustion into a controlled rejection that the caller can handle.
3. **Always release resources in a finally/defer/using block.** A connection, semaphore permit, or file descriptor that is not released on the error path leaks. One leak per request is a linear resource drain; under load it is a rapid exhaustion. Use language constructs that guarantee release: `using`, `defer`, `try-with-resources`, context managers.
4. **Set acquisition timeouts on all pool borrows.** A thread waiting forever for a pool connection while the pool is exhausted is itself a thread that cannot serve other requests. A bounded wait followed by a `503` returns the thread to the pool to handle other work.
5. **Apply backpressure end-to-end.** When internal processing cannot keep up, the HTTP listener should stop accepting new connections, not queue them in memory. This applies from the innermost queue to the outermost socket backlog. Each layer signals overload to the layer above it.
6. **Distinguish CPU-bound and I/O-bound work; schedule them separately.** Mixing long CPU-bound tasks into an async I/O scheduler (e.g., Node.js event loop, .NET async machinery) starves I/O processing. Offload CPU-bound work to a dedicated thread pool or worker process.
7. **Use timeouts on all outbound calls.** A downstream service that responds slowly holds the calling thread/goroutine/promise until the timeout fires. Without a timeout, a degraded dependency can hold every worker in the pool and cause a full stall. Set connect timeout and read timeout independently.
8. **Monitor and alert on utilisation, not just errors.** A pool at 95% utilisation is about to fail. An alert at 70% gives operators time to act. Instrument pool size, queue depth, wait time, and reject count; set alerts before limits are reached.

## Anti-patterns
- `new Thread(() => { ... }).Start()` per request — no pool, no bound, heap exhausted under load.
- `ThreadPool.QueueUserWorkItem` with an unbounded queue — accepts all work until the process crashes.
- Opening a database connection per request and closing it in an unchecked finally block that swallows the close exception, leaving the connection open.
- Calling a third-party HTTP endpoint with no timeout inside an async handler — one slow downstream stalls the entire thread pool.
- Using `Task.Run` for CPU-heavy work inside an ASP.NET request handler — it borrows from the same shared pool that serves HTTP requests.
- A channel or queue with `capacity = int.MaxValue` — effectively unbounded, masks backpressure.
- Scaling horizontally instead of fixing a connection leak — adding instances distributes the leak, it does not eliminate it.

## References
- OWASP ASVS V11.1 — Business Logic Security
- OWASP Denial of Service Cheat Sheet
- CWE-400 — Uncontrolled Resource Consumption
