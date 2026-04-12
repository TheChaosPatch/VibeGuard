---
schema_version: 1
archetype: architecture/privacy-by-design
title: Privacy by Design
summary: Engineering privacy as a foundational system property through data minimization, purpose limitation, consent, and technical safeguards.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - privacy
  - gdpr
  - pii
  - anonymization
  - pseudonymization
  - consent
  - data-minimization
  - purpose-limitation
  - right-to-erasure
  - privacy-engineering
related_archetypes:
  - architecture/data-classification
  - logging/sensitive-data
references:
  owasp_asvs: V8.3
  owasp_cheatsheet: User Privacy Protection Cheat Sheet
  cwe: "359"
---

# Privacy by Design -- Principles

## When this applies
Any system that collects, stores, processes, or transmits personal data about identifiable individuals -- regardless of jurisdiction. GDPR applies to EU residents worldwide; CCPA applies to California residents; LGPD, PIPEDA, PDPA, and other laws create overlapping obligations globally. Even absent regulation, engineering for privacy reduces breach impact, builds user trust, and produces simpler, lower-risk systems. If your system touches a name, email, IP address, device ID, or behavioral data, this archetype applies.

## Architectural placement
Privacy by design is applied at the data model layer (what is collected), the API layer (what is exposed), the processing layer (what is retained and for how long), the logging layer (what is recorded), and the organizational layer (who can access what). Privacy controls are embedded in the system's structure, not bolted on as consent banners or privacy policies that do not reflect actual data handling.

## Principles
1. **Collect only what the specific feature requires.** Every personal data field collected must have a documented, specific purpose. "We might need it later" is not a purpose. If a feature works without the user's date of birth, it is not collected. Data that was never collected cannot be breached, subpoenaed, or misused. Data minimization is the highest-leverage privacy control.
2. **Define and enforce purpose limitation.** Data collected for one purpose is not repurposed without fresh consent. Analytics data collected to improve page load times is not piped into a behavioral profiling pipeline without a separate, explicit consent flow. Purpose is a constraint on data flow, not just a notice in a privacy policy.
3. **Obtain informed, specific, and withdrawable consent.** Consent is granular -- for each processing purpose, collected separately. Consent is informed -- users understand what they are consenting to in plain language. Consent is withdrawable -- users can revoke it, and revocation triggers downstream deletion or de-identification. Pre-ticked boxes and consent bundled with terms of service are not valid consent.
4. **Apply pseudonymization where re-identification is not needed.** Replace direct identifiers (name, email, SSN) with non-identifying tokens wherever the processing does not require the original identity. Analytics, ML training, and aggregated reporting typically do not require real names. Pseudonymization limits the impact of a breach -- the attacker gets tokens, not identities, unless they also compromise the mapping table.
5. **Implement the right to erasure as a first-class architectural concern.** Design data models so that a specific individual's data can be deleted or de-identified without corrupting aggregate data or breaking referential integrity. This means pseudonymization of foreign keys in audit logs, care with denormalized data in event stores, and explicit delete propagation plans across services and backups.
6. **Enforce data access controls by privacy classification.** PII is accessible only to the services and operators that have a documented need. The analytics service does not access raw PII; it accesses aggregate or pseudonymized projections. Support operators access masked records by default; unmasking requires approval and is audit-logged.
7. **Build retention limits into the data model.** Every personal data type has a maximum retention period stored alongside the data, not in a policy document. Automated deletion jobs enforce those limits. Backups are included in the retention calculation -- not treated as exempt from deletion obligations.
8. **Default to privacy-preserving settings.** Features that involve personal data sharing, public visibility, or broad data collection are off by default. Users opt in to sharing; they do not opt out of privacy protection. Privacy defaults apply to new features before they are reviewed, not after.
9. **Account for privacy in third-party integrations.** Every third-party SDK, analytics library, CDN, and partner integration that touches user data is a data processor. Understand what each integration transmits, retain Data Processing Agreements, and choose privacy-preserving configurations (e.g., IP anonymization in analytics, disabled fingerprinting). Third-party SDKs that auto-collect data bypass your minimization controls.

## Anti-patterns
- Collecting "all the data we might ever need" without purpose limitation, in case a future feature requires it.
- Logging request payloads that contain PII "for debugging" without a retention policy or access control.
- Consent bundled into terms of service acceptance -- a single checkbox for all processing purposes.
- Assuming that pseudonymization is anonymous (it is not -- it is reversible with the mapping table).
- No erasure pathway: deletion requests that only soft-delete or de-activate accounts, leaving all data intact.
- Personal data shared freely between microservices because they all run in the same cluster.
- Third-party analytics SDKs loaded unconditionally before consent is obtained.
- A "we are privacy compliant" checkbox that refers to a privacy notice, not to technical controls.
- Backups and event logs excluded from data retention and deletion obligations.

## References
- OWASP ASVS V8.3 -- Sensitive Private Data
- OWASP User Privacy Protection Cheat Sheet
- CWE-359 -- Exposure of Private Personal Information to an Unauthorized Actor
- GDPR Articles 5, 6, 7, 17, 25 -- Data protection principles, consent, erasure, privacy by design
- NIST Privacy Framework 1.0
- ISO 29101 -- Privacy Architecture Framework
- Ann Cavoukian -- Privacy by Design: The 7 Foundational Principles (2009)
