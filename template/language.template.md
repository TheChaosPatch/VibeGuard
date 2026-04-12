---
schema_version: 1
archetype: {{category}}/{{archetype-id}}
language: {{wire-name, e.g. python}}
principles_file: _principles.md
libraries:
  preferred: {{primary recommended library}}
  acceptable:
    - {{alternative library}}
  avoid:
    - name: {{library or approach to avoid}}
      reason: {{one-sentence explanation of why}}
minimum_versions:
  {{language-key}}: "{{minimum version, e.g. 3.10}}"
---

# {{Title}} — {{Language Display Name}}

## Library choice
{{1-3 sentences explaining why the preferred library is the right choice. Mention what it does well, why the acceptable alternatives are acceptable, and what makes the avoided libraries/approaches dangerous.}}

## Reference implementation
```{{language}}
{{Complete, runnable code that demonstrates the secure pattern.
CONSTRAINTS:
- Maximum 40 non-empty lines (comments count as non-empty)
- Show the MOST security-critical function(s) first
- Use realistic variable names and types
- Include only essential imports
- No placeholder comments like "// TODO" or "// add error handling"
- The code should be copy-paste ready for a developer who reads the principles first}}
```

## Language-specific gotchas
- {{A trap specific to this language or its ecosystem. Name the exact API, flag, or default that causes the problem and what to do instead.}}
- {{Another gotcha. Be concrete — "Use X instead of Y because Z."}}

## Tests to write
- {{Describe a specific test case: input, expected output, and what it validates.}}
- {{Another test case — focus on negative/adversarial scenarios.}}
