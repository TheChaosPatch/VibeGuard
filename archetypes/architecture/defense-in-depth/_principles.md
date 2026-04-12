---
schema_version: 1
archetype: architecture/defense-in-depth
title: Defense in Depth
summary: Layering independent security controls so that no single failure compromises the system.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - defense-in-depth
  - layered-security
  - fail-safe
  - least-privilege
  - blast-radius
  - segmentation
  - isolation
  - redundancy
  - zero-trust
  - compartmentalization
related_archetypes:
  - architecture/secure-development-lifecycle
  - architecture/threat-modeling
  - architecture/data-classification
  - errors/error-handling
references:
  owasp_asvs: V1.1
  owasp_cheatsheet: Secure Product Design Cheat Sheet
  cwe: "693"
---

# Defense in Depth -- Principles

## When this applies
Every system that has something worth protecting -- user data, financial transactions, infrastructure access, intellectual property. Defense in depth is not a feature you add; it is a design philosophy that shapes every layer of the architecture. If your system's security depends on a single control (one firewall, one input check, one authentication gate), this archetype applies because that single control is a single point of failure.

## Architectural placement
Defense in depth is a cross-cutting concern that spans every layer: network (firewalls, segmentation), infrastructure (hardened OS, patched runtimes), application (input validation, authentication, authorization), and data (encryption at rest and in transit, access controls on storage). Each layer assumes the layer above it has already been compromised. This archetype governs how those layers are designed to be independent -- a failure in one does not cascade into a total breach.

## Principles
1. **No single control is the security strategy.** If input validation is the only thing preventing SQL injection, a single bypass is a breach. Layer parameterized queries behind validation, database permissions behind parameterized queries, and network segmentation behind database permissions. Each layer catches what the previous one missed.
2. **Assume every layer will be breached.** Design each layer as if the layers above it have already fallen. The database should enforce row-level access controls even though the application layer already checks permissions. The application should validate input even though the WAF already filters. This redundancy is not waste -- it is the entire point.
3. **Enforce least privilege at every boundary.** Each component gets the minimum permissions it needs. The web server cannot write to the database admin tables. The background worker cannot access the payment API. The CI runner cannot push to production. When a component is compromised, the attacker inherits only its narrow permissions, not the system's full capability.
4. **Segment networks and services.** Place components in separate network zones with explicit firewall rules between them. The public-facing web tier, the application tier, and the database tier should not share a flat network. Lateral movement after a breach should require the attacker to cross additional boundaries, each with its own controls.
5. **Encrypt at rest and in transit -- no exceptions.** Data in the database is encrypted. Data moving between services uses TLS. Data moving to the browser uses HTTPS. Internal service-to-service traffic uses mTLS or equivalent. "It's on the internal network" is not an encryption exemption.
6. **Make the secure path the easy path.** If developers have to opt in to security (manually calling a sanitization function, remembering to set a header), they will forget. Build security into the defaults: the ORM parameterizes by default, the framework sets security headers by default, the deployment pipeline scans by default. Insecure behavior should require explicit, reviewable opt-out.
7. **Detect and respond, not just prevent.** Prevention fails. Complement preventive controls with detection (logging, monitoring, anomaly alerting) and response (automated lockout, incident runbooks). A system that is breached silently is worse than one that is breached loudly.
8. **Limit blast radius through compartmentalization.** If one microservice is compromised, can the attacker access every other service's data? If one user account is hijacked, can it affect other users' data? Design boundaries so that a breach in one compartment stays contained. Separate databases per tenant, separate credentials per service, separate signing keys per environment.
9. **Fail closed, not open.** When a security control encounters an error -- the authorization service is down, the WAF cannot parse a request, the certificate cannot be verified -- the default behavior is deny. Never fall back to "allow everything" because a security component failed.

## Anti-patterns
- A single firewall as the entire security architecture ("we have a WAF, we're secure").
- Flat networks where every service can reach every other service and the database directly.
- A single database account shared by all application components with full read/write access.
- Disabling TLS for internal traffic because "it's behind the load balancer."
- Security controls that require developers to remember to call them rather than being built into the framework.
- "We'll add monitoring later" -- deploying preventive controls without any detection or alerting.
- A single API key or service account used across all environments (dev, staging, production).
- Allowing a compromised component to access secrets or credentials for every other component.

## References
- OWASP ASVS V1.1 -- Architecture, Design and Threat Modeling
- OWASP Secure Product Design Cheat Sheet
- CWE-693 -- Protection Mechanism Failure
- Saltzer & Schroeder, "The Protection of Information in Computer Systems" (1975)
- NIST SP 800-53 -- Security and Privacy Controls
