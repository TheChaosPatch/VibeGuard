---
schema_version: 1
archetype: io/regex-dos
language: php
principles_file: _principles.md
libraries:
  preferred: preg_match with PCRE2 (PHP 7.3+) and input length cap
  acceptable: []
  avoid:
    - name: preg_match with user-supplied pattern
      reason: Attacker supplies an exponential PCRE pattern; also enables PCRE injection via pattern delimiters and flags.
    - name: ereg / eregi (POSIX regex)
      reason: Removed in PHP 7.0; POSIX ERE engine also exhibits catastrophic backtracking on certain inputs.
minimum_versions:
  php: "8.3"
---

# ReDoS Defense — PHP

## Library choice
PHP uses PCRE2 (since PHP 7.3) for `preg_match` and related functions. PCRE2 is a backtracking NFA engine. PHP exposes `pcre.backtrack_limit` and `pcre.recursion_limit` in `php.ini` as global backtracking caps — useful but coarse. The per-call defense is a strict input length cap before `preg_match`. PHP 8.0+ also supports `PCRE_NO_AUTO_CAPTURE` and possessive quantifiers. Always check `preg_match`'s return value for `-1` (PREG_BACKTRACK_LIMIT_ERROR) as well as `false`; both indicate failure.

## Reference implementation
```php
<?php
declare(strict_types=1);

final class InputValidation
{
    private const MAX_EMAIL_LEN = 254;
    private const MAX_SLUG_LEN  = 128;

    // Anchored with ^ and $ in single-line context (no MULTILINE flag).
    // Possessive quantifiers (++) prevent backtracking into the character class.
    private const SLUG_PATTERN  = '/^[a-z0-9]++(?:-[a-z0-9]++)*$/D';
    private const EMAIL_PATTERN = '/^[a-zA-Z0-9._%+\-]{1,64}+@[a-zA-Z0-9.\-]{1,253}+\.[a-zA-Z]{2,63}$/D';

    public static function isValidSlug(string $value): bool
    {
        if (strlen($value) > self::MAX_SLUG_LEN) {
            return false;
        }
        $result = preg_match(self::SLUG_PATTERN, $value);
        return $result === 1;
    }

    public static function isValidEmail(string $value): bool
    {
        if (strlen($value) > self::MAX_EMAIL_LEN) {
            return false;
        }
        $result = preg_match(self::EMAIL_PATTERN, $value);
        if ($result === false || $result === -1) {
            // PREG_BACKTRACK_LIMIT_ERROR or PREG_RECURSION_LIMIT_ERROR.
            error_log('preg_match error for email validation: ' . preg_last_error_msg());
            return false;
        }
        return $result === 1;
    }
}
```

## Language-specific gotchas
- `preg_match` returns `1` on match, `0` on non-match, and `false` on pattern error. It also returns `false` when PCRE hits the backtrack or recursion limit. Always check for both `false` and the error code via `preg_last_error()` — silently treating an error return as "no match" hides attacks.
- The `/D` modifier makes `$` match only at the end of the string, not before a trailing newline. Without it, `/^admin$/` matches `"admin\n"`. Always use `/D` for validation patterns.
- Possessive quantifiers (`++`, `*+`, `?+`) are supported by PCRE2. They prevent the engine from backtracking into the quantified group. Use them in character class quantifiers inside validation patterns.
- `pcre.backtrack_limit` (default 1,000,000) and `pcre.recursion_limit` (default 100,000) are global limits. A catastrophic pattern can still exhaust CPU before hitting the backtrack limit if the pattern structure produces `O(2^n)` work per match position. The length cap is the more reliable defense.
- `preg_match('/pattern/', $userInput)` where `$userInput` is used as the *pattern* is injection. Validate that the pattern argument is always a string literal or a constant.
- FILTER_VALIDATE_EMAIL (`filter_var($email, FILTER_VALIDATE_EMAIL)`) uses PHP's internal email validator, which is backed by PCRE but with a bounded pattern. It is an acceptable alternative for simple email format checks; it is not ReDoS-safe for all inputs without a length cap.

## Tests to write
- Slug happy path: `isValidSlug("hello-world")` returns `true`.
- Slug too long: a string of `MAX_SLUG_LEN + 1` characters returns `false`.
- Email happy path: `isValidEmail("user@example.com")` returns `true`.
- Email too long: a 255-character string returns `false`.
- Adversarial slug: `str_repeat("a", 200) . "!"` completes in under 100 ms.
- Backtrack error handling: mock `preg_last_error()` to return `PREG_BACKTRACK_LIMIT_ERROR`; assert `isValidEmail` returns `false` and logs an error.
- `/D` modifier regression: assert `isValidSlug("ok\n")` returns `false` (trailing newline bypass test).
- Pattern is a constant: verify the pattern strings are `private const`, not variables that could be overwritten.
