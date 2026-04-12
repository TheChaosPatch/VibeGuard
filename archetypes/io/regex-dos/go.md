---
schema_version: 1
archetype: io/regex-dos
language: go
principles_file: _principles.md
libraries:
  preferred: regexp (stdlib — RE2, linear time guaranteed)
  acceptable: []
  avoid:
    - name: dlclark/regexp2
      reason: A .NET-compatible backtracking engine for Go; loses the linear-time guarantee of the stdlib regexp package.
    - name: regexp.MustCompile with user-supplied pattern string
      reason: Attacker supplies the pattern; even though RE2 rejects exponential patterns, the compile step itself can be slow for maliciously complex RE2 expressions.
minimum_versions:
  go: "1.22"
---

# ReDoS Defense — Go

## Library choice
Go's `regexp` package implements the RE2 algorithm, which guarantees `O(n)` matching time regardless of pattern complexity or input content. There is no backtracking and therefore no catastrophic backtracking risk for patterns compiled with the stdlib package. The remaining risks are: (1) compiling user-supplied patterns (the compile step is not O(1) for adversarial RE2 expressions), and (2) using `dlclark/regexp2` which is a backtracking engine. Use the stdlib `regexp` package for all validation; pre-compile patterns as package-level variables; never compile user-supplied strings.

## Reference implementation
```go
package validation

import (
	"regexp"
)

const (
	MaxEmailLen = 254
	MaxSlugLen  = 128
)

// Package-level compiled patterns -- compiled once, re-used, and not derived from user input.
var (
	slugRE  = regexp.MustCompile(`^[a-z0-9]+(?:-[a-z0-9]+)*$`)
	emailRE = regexp.MustCompile(`^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,253}\.[a-zA-Z]{2,63}$`)
)

// IsValidSlug returns true if v is a safe URL slug.
// Length check is O(1) and runs before regex evaluation.
func IsValidSlug(v string) bool {
	if len(v) > MaxSlugLen {
		return false
	}
	return slugRE.MatchString(v)
}

// IsValidEmail returns true if v matches a simple email shape.
// This is a format check, not RFC 5322 full compliance.
func IsValidEmail(v string) bool {
	if len(v) > MaxEmailLen {
		return false
	}
	return emailRE.MatchString(v)
}
```

## Language-specific gotchas
- `regexp.MustCompile` panics if the pattern is syntactically invalid. Declare patterns as package-level `var` blocks so a bad pattern causes a startup panic rather than a runtime panic during a request. Never call `MustCompile` inside a hot path.
- Go's RE2 engine rejects certain features that PCRE supports: lookaheads, lookbehinds, backreferences, and possessive quantifiers. Attempting to compile a pattern with these causes `regexp.Compile` to return an error. This is a feature — it enforces the linear-time guarantee.
- `regexp.Regexp` is safe for concurrent use. Pre-compiling patterns as package-level variables and calling `MatchString` from multiple goroutines is correct and efficient.
- Even though RE2 is linear-time, `regexp.Compile(userInput)` should be avoided. The RE2 compiler itself is not constant-time for all inputs — a sufficiently large or complex pattern (e.g., a 10 MB alternation string) can consume significant CPU during compilation. If you must allow user patterns, validate their length and complexity first.
- `regexp.MatchString(pattern, input)` compiles the pattern on every call. Always use pre-compiled `*regexp.Regexp` values for paths that handle external input.
- `regexp.FindAllString` and related methods return all matches in the input. If the input is large and the pattern matches many substrings, the result slice can be large. Cap the number of results with the `n` parameter: `FindAllString(input, 100)`.

## Tests to write
- Slug happy path: `"hello-world"` returns `true`.
- Slug too long: a string of `MaxSlugLen + 1` bytes returns `false`.
- Slug with uppercase: `"Hello"` returns `false`.
- Email happy path: `"user@example.com"` returns `true`.
- Email too long: a 255-byte string returns `false`.
- Adversarial slug: `strings.Repeat("a", 1000) + "!"` completes in under 10 ms (linear time regression).
- Pattern concurrency: call `IsValidSlug` and `IsValidEmail` from 50 goroutines simultaneously with valid inputs; assert no data race (run with `-race`).
