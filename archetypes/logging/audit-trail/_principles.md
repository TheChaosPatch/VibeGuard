---
schema_version: 1
archetype: logging/audit-trail
title: Security Audit Trail
summary: Structured, tamper-evident audit logs that record who did what, when, and why.
applies_to: [csharp, python, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - audit
  - audit-trail
  - tamper-evidence
  - accountability
  - compliance
  - structured-events
  - retention
  - pii
  - correlation-id
  - immutable
  - append-only
  - hmac
related_archetypes:
  - logging/sensitive-data
  - auth/authorization
references:
  owasp_asvs: V7.2
  owasp_cheatsheet: Logging Cheat Sheet
  cwe: "778"
---

# Security Audit Trail — Principles

## When this applies
Any operation that changes system state in a way that is security-relevant: authentication attempts (success and failure), authorization decisions, data creation/modification/deletion, privilege changes, configuration changes, and administrative actions. The audit trail is not application logging. Application logs exist for debugging and are ephemeral. Audit events exist for accountability and compliance and are retained for years. They answer the question "who did what, when, from where, and did it succeed?" long after the engineer who wrote the code has moved on.

## Architectural placement
Audit events flow through a dedicated audit service or interface that is separate from the application logger. The audit writer accepts structured event objects — not format strings — and appends them to a write-once or append-only store (a dedicated database table with no UPDATE/DELETE grants, an immutable blob store, or a log stream with write-only IAM). The audit pipeline is synchronous with the operation: if the audit write fails, the operation fails. This is the opposite of application logging, where a failed log line should not crash the request. The audit store has its own retention policy, access controls, and integrity verification independent of the application database.

## Principles
1. **Every audit event answers: who, what, when, where, outcome.** `ActorId`, `Action`, `Timestamp` (server-generated UTC), `SourceIP`/`CorrelationId`, and `Outcome` (success/failure/denied) are required fields. Optional: `TargetId`, `Reason`, `PreviousValue`, `NewValue` (for change tracking). If any required field is missing, the audit writer rejects the event.
2. **Audit events are append-only.** The audit store does not support UPDATE or DELETE from the application. Use a database user with INSERT-only grants, a write-once object store, or a log stream with no delete API. If someone asks "can we delete audit records for GDPR," the answer is "we pseudonymize the actor, we do not delete the event."
3. **Tamper evidence through hash chaining.** Each audit event includes an HMAC or hash of the previous event's hash plus the current event's content. An auditor can verify the chain from any point and detect gaps or modifications. The HMAC key is stored in a HSM or secrets manager, not in the application config.
4. **PII minimization — log identifiers, not personal data.** The audit event records `actor_id: "usr_abc123"`, not `actor_name: "Jane Smith"`. The mapping from ID to person lives in the identity system, not in the audit log. This limits the blast radius on log compromise and simplifies GDPR pseudonymization.
5. **Correlation IDs link audit events across services.** A single user action that spans multiple services produces one audit event per service, all sharing the same `CorrelationId`. The audit query interface supports searching by correlation ID to reconstruct the full operation.
6. **Clock accuracy matters.** Audit timestamps must come from the server, not the client. Use NTP-synchronized clocks and UTC. A skewed timestamp makes event ordering unreliable and weakens forensic analysis.
7. **Retention is a policy, enforced by infrastructure.** Audit events have a defined retention period (typically 1-7 years depending on regulation). Retention is enforced by the storage layer (TTL, lifecycle policy), not by an application cron job. The retention policy is documented, tested, and auditable.
8. **Failed operations are as important as successful ones.** A failed login attempt, a denied authorization check, a rejected payment — these are the events that matter most during incident investigation. Logging only successes creates blind spots.

## Anti-patterns
- Using the application logger (`ILogger`, `structlog`) for audit events. Application logs get rotated, sampled, and dropped under load; audit events must not.
- Including the user's full name, email, or IP address in the audit event body without necessity. Log the user ID and resolve it at query time.
- Writing audit events asynchronously with fire-and-forget. If the audit queue drops the event, the operation happened without a record.
- Storing audit events in the same database table that the application can UPDATE and DELETE.
- Relying on database triggers for audit — triggers can be disabled by a DBA, and the audit gap is invisible.
- Logging only the new value without the previous value on updates. "Changed role" is useless without "from viewer to admin."
- Using `DateTime.Now` instead of `DateTime.UtcNow` or local clocks instead of NTP — timestamps become unreliable across time zones and DST transitions.
- Treating audit log access as non-sensitive. Read access to the audit trail is itself a privileged operation that should be logged.

## References
- OWASP ASVS V7.2 — Log Processing
- OWASP Logging Cheat Sheet
- CWE-778 — Insufficient Logging
