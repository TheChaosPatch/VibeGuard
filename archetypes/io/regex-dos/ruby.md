---
schema_version: 1
archetype: io/regex-dos
language: ruby
principles_file: _principles.md
libraries:
  preferred: Regexp (stdlib) with input length cap and possessive quantifiers
  acceptable:
    - re2 gem (Google RE2 bindings — linear time)
  avoid:
    - name: Regexp.new(user_input)
      reason: Attacker-supplied pattern on a backtracking NFA; ReDoS and injection vector.
    - name: Unanchored patterns with =~ on unbounded strings
      reason: Scans every position; amplifies backtracking worst case compared to anchored full-string match.
minimum_versions:
  ruby: "3.3"
---

# ReDoS Defense — Ruby

## Library choice
Ruby's `Regexp` uses Oniguruma, a backtracking NFA engine. Ruby 3.2+ includes an experimental linear-time regex engine (`Regexp::EXTENDED_LINEAR`) for a restricted pattern subset, but it is not the default. The practical defenses are: (1) cap input length before `=~` or `match?`; (2) use `\A` / `\z` anchors rather than `^` / `$` (in Ruby, `^` and `$` match line boundaries, not string boundaries — a critical difference); (3) use the `re2` gem for high-risk patterns. Possessive quantifiers (`++`, `*+`) are supported by Oniguruma and eliminate backtracking into the quantified group.

## Reference implementation
```ruby
# frozen_string_literal: true

require "English"

module InputValidation
  MAX_EMAIL_LEN = 254
  MAX_SLUG_LEN  = 128

  # Anchored with \A / \z (string boundaries, not line boundaries).
  # Possessive quantifiers (*+, ++) prevent backtracking into the groups.
  SLUG_RE  = /\A[a-z0-9]++(?:-[a-z0-9]++)*\z/
  EMAIL_RE = /\A[a-zA-Z0-9._%+\-]{1,64}+@[a-zA-Z0-9.\-]{1,253}+\.[a-zA-Z]{2,63}\z/

  module_function

  def valid_slug?(value)
    return false unless value.is_a?(String) && value.length <= MAX_SLUG_LEN

    SLUG_RE.match?(value)
  end

  def valid_email?(value)
    return false unless value.is_a?(String) && value.length <= MAX_EMAIL_LEN

    EMAIL_RE.match?(value)
  end
end
```

## Language-specific gotchas
- **`^` and `$` are line anchors in Ruby, not string anchors.** A pattern like `/^admin$/` matches `"hello\nadmin\nworld"` because `^` matches after a newline. Use `\A` and `\z` for string-boundary anchors in all validation patterns. This is also a security bypass vector independent of ReDoS.
- Possessive quantifiers (`++`, `*+`, `?+`) prevent the engine from backtracking into the quantified group. For example, `[a-z0-9]++` matches a run of characters and will not re-try with fewer characters if the rest of the pattern fails. This is the structural fix for many ReDoS patterns and is supported by Oniguruma.
- `Regexp#match?` returns a boolean without creating a `MatchData` object, which is faster and GC-friendlier than `=~` or `match`. Use it for validation.
- The `re2` gem (`gem "re2"`) wraps Google RE2 in a Ruby-compatible API. `RE2::Regexp.new(pattern)` compiles an RE2 expression; `re2_pattern.match?(string)` is linear-time. It rejects lookaheads, lookbehinds, and backreferences at compile time.
- Ruby's `Regexp::TIMEOUT` (Ruby 3.2+) sets a process-wide timeout for regex matching. Set it in a global initializer: `Regexp::TIMEOUT = 0.1` (100 ms). This is a belt-and-suspenders guard, not a substitute for safe pattern design.
- `Regexp.new(user_input)` compiles an attacker-controlled pattern. If you need user-defined search, use `Regexp.escape(user_input)` to treat the input as a literal string before compiling it. For structured search, prefer a database full-text index over regex.

## Tests to write
- Slug happy path: `valid_slug?("hello-world")` returns `true`.
- Slug too long: a string of `MAX_SLUG_LEN + 1` characters returns `false`.
- Slug with uppercase: `valid_slug?("Hello")` returns `false`.
- Email happy path: `valid_email?("user@example.com")` returns `true`.
- Email too long: a 255-character string returns `false`.
- Anchor regression: `valid_slug?("ok\nbad!")` returns `false` (line-anchor bypass test).
- Adversarial slug: `"a" * 200 + "!"` completes in under 50 ms.
- Non-string input: `valid_email?(nil)` and `valid_email?(42)` return `false` without exception.
