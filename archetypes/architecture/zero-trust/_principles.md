---
schema_version: 1
archetype: architecture/zero-trust
title: Zero Trust Architecture
summary: Designing systems where no actor, network location, or device is trusted implicitly; every access request is verified continuously.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - zero-trust
  - perimeter
  - microsegmentation
  - identity
  - verify
  - continuous-verification
  - never-trust
  - explicit-access
related_archetypes:
  - architecture/defense-in-depth
  - architecture/least-privilege
references:
  owasp_asvs: V1.1
  owasp_cheatsheet: Secure Product Design Cheat Sheet
  cwe: "284"
---

# Zero Trust Architecture -- Principles

## When this applies
Any system where the network perimeter cannot be fully trusted -- which in practice means every modern system. Cloud deployments, hybrid infrastructure, remote workforce access, and microservice architectures all invalidate the assumption that "inside the network means trusted." If your security posture relies on a firewall boundary as the primary trust mechanism, zero trust applies.

## Architectural placement
Zero trust is a holistic architecture posture, not a product or a perimeter device. It governs identity and access management, network segmentation, device posture verification, application-layer authorization, and observability. Every communication channel -- user to app, service to service, operator to infrastructure -- is subject to the same verify-before-trust policy regardless of whether the traffic originates inside or outside a data center.

## Principles
1. **Never trust, always verify.** Network location is not an authorization signal. A request from inside the corporate network gets the same scrutiny as one from the public internet. Identity, device health, and context are the authorization signals -- not IP range or VLAN membership.
2. **Verify identity explicitly for every access request.** Authentication must occur at every service boundary, not just at the perimeter gateway. Service-to-service calls require mutual authentication (mTLS or equivalent). User sessions require continuous validation, not just a login event at session start.
3. **Apply least-privilege access to all principals.** Grant users, services, and devices only the permissions required for their current task. Permissions are scoped by identity, context, and time -- not by broad role membership. See `architecture/least-privilege` for the full treatment.
4. **Assume breach -- design for containment.** Every component is designed as if adjacent components are already compromised. Lateral movement after a breach must cross explicit authorization boundaries, not just hop between hosts on the same subnet.
5. **Microsegment the network.** Replace flat network zones with fine-grained segments, each with explicit allow-listed traffic rules. Services communicate over documented, policy-enforced paths. Unanticipated communication between segments is blocked by default, not just unmonitored.
6. **Enforce device posture as an access condition.** Where applicable, access decisions incorporate device state: patch level, EDR status, certificate validity. An identity from an unmanaged or out-of-compliance device is not treated the same as the same identity from a healthy device.
7. **Log all access decisions with context.** Every allow and deny decision is recorded with identity, resource, action, timestamp, and contextual signals. Logs are tamper-evident and forwarded to a centralized collector outside the component's control. Detection depends on this telemetry.
8. **Make policy explicit and machine-enforceable.** Authorization policy is declarative, version-controlled, and enforced by a policy engine -- not encoded implicitly in application logic or firewall rules maintained by a single team. Policy changes go through review and are auditable.
9. **Design for continuous validation, not periodic re-authentication.** Session tokens have short lifetimes. Access is re-evaluated on contextual changes (new IP, new device, privilege escalation attempt). Long-lived, context-free credentials are a zero-trust failure.

## Anti-patterns
- Treating internal network traffic as implicitly trusted and skipping authentication between services.
- A single perimeter firewall as the entire access control model ("castle and moat").
- Long-lived service credentials that grant broad access and never expire.
- Flat network segments where a compromised host can reach all other hosts and databases.
- Authorization decisions based solely on IP address or network zone.
- No logging of service-to-service access, making lateral movement invisible in logs.
- Permanent administrative access that does not require re-verification for sensitive operations.
- Treating "on the VPN" as equivalent to "authorized to access all internal systems."

## References
- OWASP ASVS V1.1 -- Architecture, Design and Threat Modeling
- OWASP Secure Product Design Cheat Sheet
- CWE-284 -- Improper Access Control
- NIST SP 800-207 -- Zero Trust Architecture
- Google BeyondCorp -- Enterprise Security Without a Traditional Perimeter
- CISA Zero Trust Maturity Model v2
