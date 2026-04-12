---
schema_version: 1
archetype: architecture/data-classification
title: Data Classification
summary: Categorizing data by sensitivity and applying proportional controls to storage, transit, logging, and retention.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - data-classification
  - sensitivity
  - pii
  - confidential
  - public
  - internal
  - restricted
  - gdpr
  - privacy
  - data-minimization
  - retention
  - data-protection
  - compliance
related_archetypes:
  - architecture/threat-modeling
  - architecture/defense-in-depth
  - logging/sensitive-data
  - persistence/secrets-handling
  - crypto/key-management
references:
  owasp_asvs: V8
  owasp_cheatsheet: User Privacy Protection Cheat Sheet
  cwe: "200"
---

# Data Classification -- Principles

## When this applies
Any system that stores, processes, or transmits data that is not entirely public. If your application handles user names, email addresses, IP addresses, financial records, health data, authentication credentials, internal business metrics, or any data subject to regulatory requirements (GDPR, HIPAA, PCI-DSS, SOC 2), this archetype applies. Data classification is the prerequisite for every other security control -- you cannot protect what you have not categorized.

## Architectural placement
Data classification is a design-time decision that feeds into every downstream control. It determines which encryption standard applies, which log fields must be redacted, which storage backends are acceptable, who can access what, and how long data is retained. The classification scheme is defined once at the organizational or project level and enforced at every layer: the API validates what data it accepts, the service layer enforces access by classification, the persistence layer applies encryption and retention rules, and the logging layer redacts classified fields.

## Principles
1. **Define a classification scheme before writing code.** At minimum, four levels: **Public** (marketing pages, open-source docs), **Internal** (employee names, internal metrics), **Confidential** (PII, customer emails, financial records), **Restricted** (passwords, encryption keys, health data, payment card numbers). Every data field in the system maps to exactly one level. If a field does not have a classification, it defaults to Confidential until reviewed.
2. **Apply controls proportional to classification.** Public data needs integrity checks but not encryption at rest. Internal data needs access controls. Confidential data needs encryption at rest, encryption in transit, access logging, and retention limits. Restricted data needs all of the above plus key management, audit trails, and minimized copies. Over-protecting public data wastes resources; under-protecting restricted data creates liability.
3. **Minimize collection.** Do not collect data you do not need. Every field you store is a field you must protect, a field that can leak, and a field that regulators can ask about. If the feature works without the user's date of birth, do not ask for it. Data you never collected cannot be breached.
4. **Minimize retention.** Define a retention period for every data class. When the period expires, delete the data -- not "mark as inactive," not "move to archive," delete. Soft-deleted data is still data. Backups that retain data beyond the retention period are a compliance gap. Automate deletion; humans forget.
5. **Enforce access by classification.** Not every developer, service, or operator needs access to every data class. The analytics service reads Internal and Public data; it never touches Confidential or Restricted. The support dashboard shows Confidential data to authorized agents; it never shows Restricted data. Access controls follow the classification, not the convenience of the developer.
6. **Redact in logs and error messages.** Logging frameworks must be classification-aware. Confidential and Restricted fields are masked, hashed, or omitted from logs. An error message that includes a customer's email address or a stack trace that contains a database connection string is a data leak, regardless of where the log is stored (see `logging/sensitive-data`).
7. **Track data flows across boundaries.** When Confidential data leaves your system -- to a third-party API, an analytics provider, a partner integration -- the classification travels with it. The receiving party's data protection must meet or exceed yours. A data-flow diagram annotated with classifications makes these obligations visible and auditable (see `architecture/threat-modeling`).
8. **Audit classification decisions.** Maintain a data inventory: what data you hold, its classification, where it is stored, who can access it, and when it should be deleted. Review the inventory periodically. New features add new fields; classifications drift if not maintained.

## Anti-patterns
- No classification scheme -- every field is treated the same, so nothing is adequately protected.
- Collecting user data "in case we need it later" with no defined purpose or retention limit.
- Logging full request bodies that contain PII because "we might need it for debugging."
- Granting every microservice access to the same database with the same credentials regardless of what data each service actually needs.
- Keeping data forever because "disk is cheap" -- ignoring regulatory and security obligations.
- Sending Confidential data to a third-party analytics service without reviewing their data protection practices.
- Classifying everything as Restricted so that the classification scheme is useless (everything gets the same treatment).
- Soft-deleting records instead of hard-deleting them when the retention period expires.
- A data inventory that was created during an audit and never updated afterward.

## References
- OWASP ASVS V8 -- Data Protection
- OWASP User Privacy Protection Cheat Sheet
- CWE-200 -- Exposure of Sensitive Information
- GDPR Article 5 -- Principles relating to processing of personal data
- ISO 27001 Annex A.8 -- Asset Management
- NIST SP 800-60 -- Guide for Mapping Types of Information
