# Generating VibeGuard Archetypes with an LLM

Copy and adapt the prompt below to generate new VibeGuard archetypes. The prompt is designed to produce files that pass the VibeGuard validator on the first attempt.

## Prompt

```
You are generating a VibeGuard secure-coding archetype. VibeGuard is an MCP server
that provides security guidance to LLM coding assistants before they write code.

Create the following archetype: {{TOPIC}}
Category: {{CATEGORY}}
Archetype ID: {{ARCHETYPE_ID}}
Languages: {{COMMA_SEPARATED_LANGUAGES, or "all" for principles-only}}

## Constraints (MUST follow exactly)

### _principles.md
- YAML frontmatter with: schema_version: 1, archetype, title, summary, applies_to,
  status: stable, author, reviewed_by, stable_since, keywords, related_archetypes, references
- Body must have EXACTLY these 5 level-2 headings (case-sensitive):
  ## When this applies
  ## Architectural placement
  ## Principles
  ## Anti-patterns
  ## References
- Maximum 200 total lines
- No code blocks in the principles file
- keywords: use specific domain terms that a developer would use when describing
  the task (not generic words like "security" or "best practice")
- references: include owasp_asvs (section), owasp_cheatsheet (name), cwe (number as string)

### Language files (e.g. python.md)
- YAML frontmatter with: schema_version: 1, archetype, language (must match filename),
  principles_file: _principles.md, libraries (preferred, acceptable, avoid with name+reason),
  minimum_versions
- Body must have EXACTLY these 4 level-2 headings (case-sensitive):
  ## Library choice
  ## Reference implementation
  ## Language-specific gotchas
  ## Tests to write
- Maximum 200 total lines
- The FIRST code block in "## Reference implementation" must have <= 40 NON-EMPTY lines
  (blank lines don't count, but comments DO count as non-empty)
- Code must be copy-paste ready, use realistic names, and demonstrate the secure pattern
- No placeholder comments like "// TODO" or "// add error handling"

### Quality requirements
- Name specific algorithms, libraries, and API functions — no vague advice
- Every anti-pattern must name the insecure approach and explain WHY it's wrong
- Library recommendations must be current and actively maintained
- Reference implementations must compile/run with the stated minimum versions
- Tests should cover: positive case, negative/rejection case, and at least one
  adversarial scenario

### Wire name reference
Supported language wire names: c, csharp, go, java, javascript, kotlin, php, python,
ruby, rust, swift, typescript

### applies_to: [all]
If the archetype is language-agnostic (architectural guidance), set applies_to: [all]
and do NOT create any language files. The principles file alone is the complete archetype.

Now generate the _principles.md file first, then each language file.
```

## Workflow

1. Copy the prompt above
2. Replace the `{{PLACEHOLDERS}}` with your topic details
3. Run through your preferred LLM
4. Save files to `archetypes/{{category}}/{{archetype-id}}/`
5. Run `dotnet test` to validate
6. Fix any validator errors and re-run

## Example usage

**For a language-specific archetype:**
```
TOPIC: Secure session token management
CATEGORY: auth
ARCHETYPE_ID: session-tokens
LANGUAGES: csharp, python, go, java
```

**For a language-agnostic archetype:**
```
TOPIC: Threat modeling methodology
CATEGORY: architecture
ARCHETYPE_ID: threat-modeling
LANGUAGES: all
```
