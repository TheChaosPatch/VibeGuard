---
schema_version: 1
archetype: {{category}}/{{archetype-id}}
title: {{Title}}
summary: {{One-sentence summary of what this archetype covers.}}
applies_to: [{{comma-separated list of supported language wire names, e.g. csharp, python, go — OR use [all] for language-agnostic architectural guidance}}]
status: stable
author: {{github-username}}
reviewed_by: [{{github-username}}]
stable_since: "{{YYYY-MM-DD}}"
keywords:
  - {{keyword1}}
  - {{keyword2}}
  - {{keyword3}}
related_archetypes:
  - {{category/related-archetype-id}}
equivalents_in: {}
references:
  owasp_asvs: {{e.g. V2.4}}
  owasp_cheatsheet: {{e.g. Password Storage Cheat Sheet}}
  cwe: "{{e.g. 916}}"
---

# {{Title}} — Principles

## When this applies
{{Describe the exact scenarios where this archetype is relevant. Be specific about what triggers the need for this guidance, and explicitly mention what is out of scope (with cross-references to other archetypes where appropriate).}}

## Architectural placement
{{Describe WHERE in the architecture this concern lives. Name the abstraction or service layer that owns this responsibility. Explain why no other layer should perform this function directly. The goal is a single point of control for the security-relevant logic.}}

## Principles
1. **{{Principle title.}}** {{Explanation. Be specific and actionable — say what to do, not just what to avoid. Include concrete thresholds, algorithm names, or configuration values where applicable.}}
2. **{{Principle title.}}** {{Explanation.}}
3. **{{Principle title.}}** {{Explanation.}}

## Anti-patterns
- {{Describe a common mistake and why it is wrong. Be specific — name the insecure algorithm, the wrong API call, or the flawed design.}}
- {{Another anti-pattern.}}

## References
- OWASP ASVS {{section}} — {{description}}
- OWASP {{Cheat Sheet name}}
- CWE-{{number}} — {{title}}
