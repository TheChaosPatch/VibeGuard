---
schema_version: 1
archetype: architecture/resilience-patterns
title: Resilience Patterns
summary: Designing systems to degrade gracefully, recover automatically, and prevent cascading failures.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - resilience
  - circuit-breaker
  - retry
  - backoff
  - timeout
  - bulkhead
  - graceful-degradation
  - fallback
  - idempotency
  - health-check
  - availability
  - fault-tolerance
  - cascading-failure
related_archetypes:
  - architecture/secure-development-lifecycle
  - architecture/defense-in-depth
  - errors/error-handling
  - concurrency/race-conditions
references:
  owasp_asvs: V11
  cwe: "400"
---

# Resilience Patterns -- Principles

## When this applies
Any system that depends on another system -- a database, a third-party API, a message queue, a cache, a DNS resolver, or a downstream microservice. In production, dependencies fail: networks partition, services overload, databases run out of connections, cloud providers have regional outages. This archetype applies to every component that makes a network call, processes a queue message, or depends on shared infrastructure. If your service cannot answer the question "what happens when the database is slow for 30 seconds?", the answer is probably "it falls over, and takes its callers down with it."

## Architectural placement
Resilience patterns are applied at the boundary between your code and its dependencies. They wrap outbound calls (HTTP clients, database connections, message producers) and inbound entry points (request handlers, queue consumers, health-check endpoints). They are infrastructure concerns, not business logic -- implemented in middleware, client wrappers, or framework-provided abstractions. The business logic should not contain retry loops or timeout arithmetic; it should call an abstraction that handles failure transparently.

## Principles
1. **Set timeouts on every outbound call.** A missing timeout turns a slow dependency into a cascading failure: your threads block, your connection pool drains, your callers time out, their callers time out. Every HTTP request, database query, and RPC call gets an explicit timeout. The timeout value is based on the dependency's observed P99 latency, not on a guess. When the timeout fires, the call fails fast with a clear error.
2. **Retry with exponential backoff and jitter.** Transient failures (network blips, brief overloads) often resolve on their own. Retry the operation with increasing delays between attempts (exponential backoff) and random jitter to prevent thundering herds. Cap the number of retries (typically 2-3). Retry only on idempotent operations or operations you have made idempotent. Retrying a non-idempotent write creates duplicates.
3. **Use circuit breakers for persistent failures.** When a dependency fails repeatedly, stop calling it. A circuit breaker tracks failure rates and opens (stops sending requests) when the rate exceeds a threshold. After a cooldown period, it allows a single probe request. If the probe succeeds, the circuit closes and traffic resumes. This prevents your service from wasting resources on a dependency that is clearly down, and gives the dependency time to recover without being hammered.
4. **Isolate failures with bulkheads.** Separate connection pools, thread pools, or rate limits for different dependencies so that a failure in one does not consume all resources and starve the others. If the recommendation service is slow, it should exhaust its own connection pool, not the pool shared with the payment service.
5. **Degrade gracefully, not catastrophically.** When a non-critical dependency is unavailable, the system should continue serving its core function with reduced capability. If the recommendation engine is down, show the product page without recommendations -- do not return a 500. Define which dependencies are critical (payment gateway: the transaction fails) and which are optional (analytics: fire and forget).
6. **Make operations idempotent.** If a client retries a request because it did not receive a response, the result should be the same as if the request executed once. Use idempotency keys, database upserts, or conditional writes to ensure that duplicate executions are safe. Non-idempotent operations in a retry-capable system will eventually produce corrupted state.
7. **Implement health checks that verify dependencies.** A health-check endpoint that returns 200 without checking the database, the cache, or the message broker is lying. Health checks should verify that the service can actually serve requests -- that its critical dependencies are reachable and responsive. Use separate liveness (is the process alive?) and readiness (can it serve traffic?) probes.
8. **Shed load before you fall over.** When the system is approaching capacity, reject new requests cleanly (HTTP 429 or 503) rather than accepting them and degrading everyone's experience. Rate limiting, admission control, and queue depth limits are better than unbounded acceptance followed by timeouts and errors.
9. **Test failure modes deliberately.** Inject failures in non-production environments: kill a dependency, add latency, corrupt a response, exhaust a connection pool. Verify that the circuit breaker opens, the fallback activates, the health check fails, and the system degrades gracefully. Chaos engineering is not recklessness -- it is the controlled verification that your resilience patterns actually work.

## Anti-patterns
- No timeouts on HTTP clients or database connections -- a slow dependency drains the entire thread pool.
- Retry loops with no backoff, no jitter, and no maximum -- amplifying the overload that caused the original failure.
- A circuit breaker that is configured but never tested, so no one knows if it actually opens.
- A single shared connection pool for all dependencies, so a failure in one starves all others.
- Returning a 500 error when a non-critical dependency is unavailable instead of degrading gracefully.
- Retrying non-idempotent operations (charging a credit card twice because the first response timed out).
- Health-check endpoints that always return 200 regardless of dependency state.
- Accepting unbounded request volume with no rate limiting or admission control.
- "It works on my machine" as the resilience test -- never injecting failures or testing degraded modes.
- Logging "connection failed, retrying" at INFO level with no circuit breaker, flooding the log aggregator.

## References
- OWASP ASVS V11 -- Business Logic
- CWE-400 -- Uncontrolled Resource Consumption
- Michael Nygard, *Release It!* (2018)
- AWS Well-Architected Framework -- Reliability Pillar
- Azure Architecture Center -- Reliability Patterns
- Google SRE Book -- Handling Overload
