---
schema_version: 1
archetype: architecture/incident-response
title: Incident Response Preparation
summary: Designing systems and processes so that security incidents are detected quickly, contained effectively, and learned from systematically.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - incident-response
  - incident
  - breach
  - detection
  - containment
  - eradication
  - recovery
  - post-mortem
  - runbook
  - alerting
  - forensics
  - escalation
  - on-call
  - triage
related_archetypes:
  - architecture/threat-modeling
  - architecture/defense-in-depth
  - logging/audit-trail
  - logging/sensitive-data
references:
  owasp_asvs: V7
  owasp_cheatsheet: Logging Cheat Sheet
  cwe: "778"
---

# Incident Response Preparation -- Principles

## When this applies
Every production system. The question is not whether you will have a security incident -- it is whether you will detect it in minutes or months, and whether your response will be coordinated or chaotic. This archetype applies at design time (building the observability and isolation that make response possible), at deployment time (having runbooks and communication plans ready), and at incident time (executing the response). If your system serves users, stores data, or processes transactions, you need an incident response capability.

## Architectural placement
Incident response is not a runtime component -- it is a capability woven into the architecture. It depends on the audit trail (`logging/audit-trail`) for detection, on defense in depth (`architecture/defense-in-depth`) for containment, on data classification (`architecture/data-classification`) for understanding what was exposed, and on the threat model (`architecture/threat-modeling`) for anticipating what kind of incidents to prepare for. The architecture must be designed so that a compromised component can be isolated, its logs can be preserved forensically, and the system can continue operating in a degraded mode while the incident is resolved.

## Principles
1. **Design for observability before you need it.** Structured logs, metrics, distributed traces, and security-specific audit events must be in place before the incident. You cannot retroactively add logging to understand a breach that already happened. Every authentication event, authorization decision, privilege escalation, data export, and configuration change should produce a structured, timestamped, immutable log entry.
2. **Define alert thresholds for security events.** Failed login spikes, privilege escalation attempts, unusual data access patterns, unexpected outbound network traffic, and configuration changes outside maintenance windows should trigger alerts. Alerts must reach humans who can act -- not a Slack channel that nobody reads. Tune for signal, not volume; alert fatigue is functionally the same as no alerting.
3. **Write runbooks before the incident.** For each threat category in your threat model, write a runbook that specifies: how to confirm the incident is real (triage), how to contain it (isolate the affected component), how to eradicate the root cause, how to recover normal operation, and who to notify (legal, affected users, regulators). A runbook written under pressure during an active breach will miss steps.
4. **Establish communication channels and roles.** Before an incident, define: who is the incident commander (decision authority), who communicates externally (legal, PR), who handles technical investigation, and what channel they use (a dedicated war room, not the general engineering Slack). Role ambiguity during an incident wastes time and causes contradictory actions.
5. **Contain before you investigate.** When a breach is confirmed, the first action is to stop the bleeding: revoke compromised credentials, isolate the affected component, block the attacker's access path. Investigation happens after containment. A thorough root-cause analysis is useless if the attacker is still active in the system while you analyze logs.
6. **Preserve forensic evidence.** Do not reboot, redeploy, or wipe the compromised system before capturing its state. Snapshot disk images, export logs, save network captures, and record the timeline. Forensic evidence is needed for root-cause analysis, for legal proceedings, and for regulatory notification. Overwriting it in the rush to restore service destroys your ability to understand what happened.
7. **Practice the response.** Run tabletop exercises (walk through a scenario verbally) and game days (simulate a real incident with injected faults) at least quarterly. A runbook that has never been tested is a hypothesis, not a plan. Exercises reveal gaps in tooling, permissions, communication, and knowledge before a real incident does.
8. **Conduct blameless post-mortems.** After every incident, write a post-mortem that covers: what happened, how it was detected, how it was contained, what the root cause was, what the impact was, and what changes will prevent recurrence. The post-mortem focuses on systemic causes (missing controls, inadequate monitoring, unclear ownership) not on individual blame. Blame suppresses reporting; systemic fixes prevent recurrence.
9. **Track remediation to completion.** Post-mortem action items are not suggestions -- they are commitments. Each item has an owner, a deadline, and a verification step. If the post-mortem identified that "we need network segmentation between the web tier and the database," that becomes a tracked work item, not a note in a document that nobody revisits.

## Anti-patterns
- No logging or alerting in production -- incidents are discovered by customers or the press.
- Alert thresholds so sensitive that the on-call engineer ignores all alerts (alert fatigue).
- No runbooks -- the response is improvised by whoever happens to be online.
- Immediately redeploying the compromised server to "fix it" before capturing forensic evidence.
- A single person who is the only one who knows how to respond, creating a bus-factor-one for incident response.
- Post-mortems that assign blame to an individual instead of identifying systemic failures.
- Post-mortem action items that are written but never tracked, prioritized, or completed.
- "We'll figure it out when it happens" as the incident response strategy.
- No practice exercises -- the first time the runbook is tested is during a real incident.

## References
- OWASP ASVS V7 -- Error Handling and Logging
- OWASP Logging Cheat Sheet
- CWE-778 -- Insufficient Logging
- NIST SP 800-61 -- Computer Security Incident Handling Guide
- PagerDuty Incident Response documentation
- Google SRE Book -- Managing Incidents
