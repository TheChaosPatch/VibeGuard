---
schema_version: 1
archetype: architecture/threat-modeling
title: Threat Modeling
summary: Systematically identifying, categorizing, and mitigating threats before writing code.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - threat-modeling
  - stride
  - attack-surface
  - trust-boundary
  - data-flow
  - threat
  - risk
  - mitigation
  - dfd
  - security-design
  - adversary
  - spoofing
  - tampering
  - repudiation
  - information-disclosure
  - denial-of-service
  - elevation-of-privilege
related_archetypes:
  - architecture/secure-development-lifecycle
  - architecture/defense-in-depth
  - architecture/data-classification
  - logging/audit-trail
references:
  owasp_asvs: V1.1
  owasp_cheatsheet: Threat Modeling Cheat Sheet
  cwe: "657"
---

# Threat Modeling -- Principles

## When this applies
Before you write the first line of a new system, service, or feature that handles user data, processes payments, authenticates callers, crosses a network boundary, or integrates with a third party. Threat modeling is not a one-time gate -- revisit the model when the architecture changes, when new data flows appear, or when a post-mortem reveals a threat category you missed. If you are adding an endpoint, a storage layer, or an external integration, the threat model needs updating.

## Architectural placement
Threat modeling sits at the very beginning of the design phase, before the architecture solidifies. It produces artifacts (data-flow diagrams, threat lists, mitigation decisions) that feed into every downstream archetype. The password-hashing archetype tells you *how* to hash; the threat model tells you *that* credential storage is a high-value target in the first place. Without a threat model, security controls are applied by intuition rather than analysis, which means the controls cluster around what the developer thought of and leave blind spots everywhere else.

## Principles
1. **Start with a data-flow diagram (DFD).** Draw every process, data store, external entity, and the flows between them. Mark trust boundaries -- every line where data crosses from one trust level to another (browser to server, server to database, your service to a third-party API). Threats live at trust boundaries; if you have not drawn them, you cannot find the threats.
2. **Apply STRIDE to every element.** For each element and each flow in the DFD, ask six questions: can an attacker *Spoof* an identity? *Tamper* with data? *Repudiate* an action? Cause *Information disclosure*? Cause *Denial of service*? Achieve *Elevation of privilege*? STRIDE is not the only framework, but it is the most widely taught and the hardest to skip categories with.
3. **Rank threats by risk, not by ease of fix.** Use a lightweight risk formula: likelihood multiplied by impact. A low-likelihood, catastrophic-impact threat (key compromise) outranks a high-likelihood, low-impact one (username enumeration). Do not let the backlog fill with easy wins while existential risks sit unmitigated.
4. **Every threat gets a disposition.** For each identified threat, decide: *mitigate* (apply a control), *accept* (document the residual risk and the business reason), *transfer* (shift to insurance or a third party), or *eliminate* (remove the feature or data flow). "We'll get to it later" is not a disposition.
5. **Mitigations map to specific controls.** "We'll encrypt it" is not a mitigation. "Data at rest is encrypted with AES-256-GCM using envelope encryption, DEKs stored in AWS KMS, key rotation every 90 days" is. Each mitigation should point to a VibeGuard archetype or an equivalent concrete specification.
6. **Minimize the attack surface.** Every endpoint, every open port, every permission, every stored field is attack surface. The most effective mitigation is removal. If a feature is not needed yet, do not build it. If an admin endpoint is only used during setup, disable it after setup. Smaller surface means fewer threats to track.
7. **Review the model in code review.** When a PR adds a new external call, a new data store, or a new role, ask: "Is this in the threat model?" If not, update the model before merging the code. The threat model is a living document, not a PDF from the design phase.
8. **Assume breach.** Model what happens *after* a component is compromised, not just how to prevent compromise. If the web server is owned, can the attacker reach the database directly? Can they pivot to internal services? Defense-in-depth and blast-radius controls come from this question.

## Anti-patterns
- Treating threat modeling as a compliance checkbox completed once and never revisited.
- A threat model that exists as a slide deck no developer has read or can find.
- Listing threats without dispositions -- an unbounded worry list with no decisions.
- Skipping the DFD and trying to brainstorm threats from memory ("I think we need rate limiting").
- Modeling only external attackers and ignoring insider threats, supply-chain compromise, and misconfiguration.
- Applying the same security controls everywhere regardless of data sensitivity or threat exposure.
- "We trust the internal network" as a blanket assumption that removes all internal trust boundaries from the model.

## References
- OWASP ASVS V1.1 -- Threat Modeling
- OWASP Threat Modeling Cheat Sheet
- CWE-657 -- Violation of Secure Design Principles
- Microsoft STRIDE model
- NIST SP 800-154 -- Guide to Data-Centric System Threat Modeling
