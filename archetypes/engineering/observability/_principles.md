---
schema_version: 1
archetype: engineering/observability
title: Observability
summary: Structured logs, metrics, traces, correlation IDs, and health endpoints baked in from day one; retrofitting never works.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - observability
  - logging
  - metrics
  - tracing
  - correlation-id
  - structured-logging
  - health-check
  - three-pillars
  - distributed-tracing
  - telemetry
  - opentelemetry
  - slo
  - golden-signals
  - debuggability
related_archetypes:
  - engineering/walking-skeleton
  - engineering/deployment-discipline
  - engineering/continuous-integration
  - logging/audit-trail
  - logging/sensitive-data
  - errors/error-handling
references:
  book: "Observability Engineering — Majors, Fong-Jones, Miranda"
  book_2: "Site Reliability Engineering — Google"
  article: "The USE Method — Brendan Gregg"
---

# Observability -- Principles

## When this applies
From the first line of a system that will run in production, not as a later retrofit. Observability is the ability to answer new questions about your system without writing and deploying new code. It applies to every service, worker, and batch job -- anywhere work happens that you cannot physically see. When a production incident forces the team to add logging mid-incident, the lesson is always the same: observability should have been present before it was needed.

## Architectural placement
Observability is a cross-cutting concern that lives in infrastructure and decorates every piece of application code. It sits adjacent to the walking skeleton (the first endpoint ships with a log, a metric, and a trace), to deployment (every deploy emits version markers), and to error-handling (every error has enough context to debug). Without observability, systems become black boxes -- behavior can only be inferred from output, and root causes must be guessed rather than measured. With it, engineers know what the system did, not what they hope it did.

## Principles
1. **The three pillars answer different questions.** Logs tell you *what happened* at a specific moment with full context. Metrics tell you *how much / how fast* over time in aggregate. Traces tell you *where time went* through a distributed call path. A system with only one pillar is blind to the questions the other two answer.
2. **Structured logs, not free text.** Logs are JSON (or equivalent structured format), not formatted strings. Every event has named fields: `user_id`, `order_id`, `duration_ms`, `status_code`. Free-text logs cannot be queried at scale; structured logs can. Every greenfield project starts with structured logging; every legacy migration starts with converting to it.
3. **Correlation IDs on every request.** A request entering the system receives an ID that propagates through every log line, every outbound call, every queued job it spawns. When an error surfaces, a single query by correlation ID reconstructs the entire causal chain. Without correlation IDs, debugging at scale is archaeology.
4. **Emit the golden signals.** Latency (how long did it take), traffic (how many requests), errors (how many failed), saturation (how full is the system). For every request handler, every database, every queue, every external dependency. These four numbers catch most production problems -- dashboards should show them prominently.
5. **Health endpoints that mean something.** A `/health` endpoint that only returns 200 proves the process is alive, nothing more. A useful health check verifies dependencies: can I reach the database? Is the queue accessible? Distinguish liveness (am I running?) from readiness (can I handle traffic?) -- different questions, different consequences.
6. **Errors carry context, not just messages.** An error with "invalid input" is useless; an error with `invalid input: field=email, value=<redacted>, caller=user-service, request_id=abc123` is actionable. Wrap errors with context as they propagate up, preserving causality.
7. **Logs at the boundary, metrics everywhere, traces for distributed work.** Log requests in and out at service boundaries. Counter/gauge/histogram metrics inside for hot paths. Spans around any call that leaves the process. Over-logging inside hot loops burns disk; under-logging at boundaries blinds debugging.
8. **Sampling is deliberate.** High-volume traces and logs are sampled to control cost -- but sampling is a decision, not an accident. Head-based sampling for general traffic, tail-based sampling to keep all errors and slow requests. Document the policy; metrics are not sampled (they are pre-aggregated).
9. **Alert on symptoms, not causes.** Alerts fire when users are suffering (latency spikes, error rates climbing, SLO burn), not on arbitrary resource thresholds (CPU over 70%). Symptom-based alerts remain valid across refactors; cause-based alerts false-positive every time the system evolves.
10. **Redact secrets and PII at the source.** Structured logging makes redaction tractable -- `password`, `ssn`, `authorization` are fields that are filtered before emission, not after storage. Cross-reference logging/sensitive-data for the specifics.

## Anti-patterns
- `printf`/`Console.WriteLine`/`print()` statements scattered through code as the logging strategy, with no levels, no structure, no filtering.
- Logs that interpolate values into free-text messages: `"User 42 saved order 9 in 123 ms"` -- not queryable, not aggregatable, not parseable by downstream tooling.
- Dashboards that show every internal metric ever emitted, with no golden-signal summary -- operators drown in detail and miss the four numbers that matter.
- `/health` endpoints that lie -- return 200 while the database connection has been down for an hour, because the check only verifies the HTTP server.
- Alerts on CPU/memory/disk thresholds with no tie to user impact -- they page engineers all night for problems no user notices.
- Catching and swallowing errors with no log at all -- the system misbehaves silently, debugging requires reproduction.
- Tracing added "only when we need it" after an incident -- now the trace is missing for every previous incident, and future incidents cost the same guessing.
- Sensitive data in logs because "we only looked at them locally" -- logs exfiltrate, get indexed, get breached; assume they are public.

## References
- Charity Majors, Liz Fong-Jones, George Miranda -- *Observability Engineering*
- Google SRE Book -- *Site Reliability Engineering* (golden signals, SLOs)
- Brendan Gregg -- "The USE Method" (brendangregg.com/usemethod.html)
- OpenTelemetry Specification -- https://opentelemetry.io
- Cindy Sridharan -- *Distributed Systems Observability*
