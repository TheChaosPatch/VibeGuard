---
schema_version: 1
archetype: io/regex-dos
title: ReDoS Defense
summary: Preventing catastrophic backtracking in regular expressions from causing denial-of-service on attacker-controlled input.
applies_to: [csharp, python, go, javascript, typescript, java, kotlin, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - regex
  - redos
  - catastrophic
  - backtracking
  - pattern
  - denial
  - exponential
  - polynomial
  - nfa
  - timeout
related_archetypes:
  - io/input-validation
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: Input Validation Cheat Sheet
  cwe: "1333"
---

# ReDoS Defense — Principles

## When this applies
Any time your code applies a regular expression to a string that originates outside your process, or to a string whose length is not strictly capped by the transport layer before evaluation. This includes: validating user-supplied email addresses, URLs, or free-text fields; parsing tokens extracted from HTTP headers or request bodies; matching patterns in file names or search queries provided by the user; and applying regex-based routing or rewriting rules where the subject string is attacker-controlled. A regex that runs fine on benign input can spin an NFA engine for minutes — or until the process is killed — on a crafted adversarial string of a few dozen characters.

## Architectural placement
All regex patterns that operate on external input live in a central `patterns` module or static constant block, reviewed at definition time for backtracking risk. Pattern definitions are never constructed from user input (no dynamic regex assembly). Execution of every pattern is wrapped with a timeout or a character-count pre-check that rejects inputs longer than the pattern's tested worst-case length. Validation functions expose a typed result (`bool` or `Option<Match>`) and never expose the raw regex engine to callers. A denial-of-service via regex is a bug in the pattern, not in the input, so patterns are treated as code: they go through code review and regression tests.

## Principles
1. **Prefer linear-time engines.** Go's `regexp` package and Rust's `regex` crate use an NFA/DFA engine that guarantees linear time regardless of input. Java 9+ `java.util.regex` does not — it is a backtracking NFA. C#, Python, JavaScript, PHP, and Ruby all use backtracking NFA engines. Use a linear-time engine where the language/ecosystem provides one; fall back to timeout-guarded backtracking where it does not.
2. **Understand which patterns are dangerous.** Catastrophic backtracking arises from: alternations with overlapping prefixes (`(a|a)+`), nested quantifiers (`(a+)+`), and groups that can match the same position by multiple paths. Audit patterns for these structures before deploying them against user input. Static analysis tools (ESLint `no-unsafe-regex`, RE2JS validator, regexploit) can automate part of this audit.
3. **Cap input length before evaluation.** A pattern that is `O(n²)` on an NFA engine is safe if `n` is bounded by 200 characters. Reject or truncate inputs that exceed the length for which the pattern has been tested. Document the length assumption next to the pattern constant.
4. **Set a timeout.** For backtracking engines that expose one (C# `Regex.Match(input, TimeSpan)`, Python regex library with `timeout`, Java `Pattern` with a wrapping thread/executor), set an absolute wall-clock timeout per evaluation. A regex that times out is treated as a non-match and logged as an anomaly.
5. **Never assemble a regex pattern from user input.** Dynamic regex construction (`new Regex(userString)`) lets the attacker supply an exponential pattern directly. If you must allow user-defined search, use a purpose-built query language (glob, prefix match, full-text index) instead.
6. **Use possessive quantifiers and atomic groups where available.** Languages that support possessive quantifiers (`a++`) or atomic groups (`(?>a+)`) prevent backtracking into the quantified group. They are not universally available but are the correct structural fix when they are.
7. **Test patterns with adversarial inputs.** For every validated pattern, write a test that feeds it the worst-case adversarial string for its structure (e.g., `"a" * 50 + "!"`) and asserts it completes in under 100 ms. This catches regressions when patterns are modified.

## Anti-patterns
- `(a+)+b` applied to a long string of `a`s that does not end in `b` — the textbook catastrophic backtracking pattern.
- Email validators with nested quantifiers: `^([a-zA-Z0-9!#...]+)*@...` — a single malformed address can peg a CPU core.
- HTML or URL parsing with regex instead of a purpose-built parser. Regex cannot correctly parse these grammars, and the attempt usually produces both a safety hole and a ReDoS risk.
- `new Regex(Request.Query["pattern"])` — user-supplied pattern is a direct ReDoS (and injection) vector.
- Silently swallowing a `RegexMatchTimeoutException` and treating it as a match or non-match without logging.
- Patterns without a documented, tested maximum input length assumption.
- Using `.*` as the primary matching clause inside a repeated group: `(.*?foo)+` has polynomial complexity on input that contains many partial `foo` matches.

## References
- OWASP ASVS V5.3 -- Output Encoding and Injection Prevention
- OWASP Input Validation Cheat Sheet
- CWE-1333 -- Inefficient Regular Expression Complexity
- OWASP "Testing for ReDoS" (WSTG-BUSL-09)
