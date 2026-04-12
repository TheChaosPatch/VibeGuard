---
schema_version: 1
archetype: io/regex-dos
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Text.RegularExpressions (with timeout + source-generated)
  acceptable:
    - RE2 via Google.RE2 NuGet (linear-time, no backtracking)
  avoid:
    - name: Regex without MatchTimeout
      reason: An unbounded backtracking NFA pattern against attacker input will spin indefinitely; the default timeout is Regex.InfiniteMatchTimeout.
    - name: new Regex(userInput)
      reason: Attacker supplies the pattern; direct path to ReDoS and injection.
minimum_versions:
  dotnet: "10.0"
---

# ReDoS Defense — C#

## Library choice
`System.Text.RegularExpressions.Regex` in .NET uses a backtracking NFA engine. Since .NET 7, the source-generator (`[GeneratedRegex]`) compiles patterns at build time and optionally uses a non-backtracking engine (`RegexOptions.NonBacktracking`). Use `RegexOptions.NonBacktracking` for any pattern applied to untrusted input — it guarantees linear time at the cost of not supporting lookarounds and backreferences. If your pattern requires features incompatible with `NonBacktracking`, add a `TimeSpan` timeout to every `Match`, `IsMatch`, and `Replace` call and cap input length before evaluation.

## Reference implementation
```csharp
using System.Text.RegularExpressions;

public static class InputValidation
{
    private const int MaxEmailLength = 254;
    private const int MaxSlugLength  = 128;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    // Source-generated + NonBacktracking = linear time, zero allocation overhead.
    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.NonBacktracking)]
    private static partial Regex SlugPattern();

    // When NonBacktracking is unsuitable (e.g. lookaheads), use a timeout.
    [GeneratedRegex(
        @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex EmailPattern();

    public static bool IsValidSlug(string input)
    {
        if (input.Length > MaxSlugLength) return false;
        return SlugPattern().IsMatch(input);
    }

    public static bool IsValidEmail(string input)
    {
        if (input.Length > MaxEmailLength) return false;
        try
        {
            return EmailPattern().IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            // Log the anomaly; treat as invalid.
            return false;
        }
    }
}
```

## Language-specific gotchas
- `RegexOptions.NonBacktracking` is available from .NET 7 onward. It uses a hybrid NFA/DFA that guarantees `O(n)` time but does not support lookaheads, lookbehinds, atomic groups, or backreferences. If your pattern requires these, fall back to a timeout.
- `matchTimeoutMilliseconds` in `[GeneratedRegex]` sets a per-call timeout. Caught `RegexMatchTimeoutException` must never be silently swallowed — at minimum log it, because it signals either a buggy pattern or an active attack.
- `Regex.InfiniteMatchTimeout` is the default when no timeout is specified. A regex that blocks for 30 s on a single thread in an ASP.NET Core application consumes that thread for the duration, which is a DoS under Kestrel's thread-pool limits.
- Compiled vs. source-generated: `RegexOptions.Compiled` JITs the pattern at runtime; `[GeneratedRegex]` emits C# source at build time. Prefer source-generated — faster startup, AOT-compatible, and avoids the reflection required by `Compiled`.
- Never use `Regex.Replace(input, userPattern, replacement)` or any overload where the pattern argument comes from external input. Validate patterns against a static allowlist if users are permitted to customize search patterns.
- Input length cap must happen before `IsMatch`, not inside the regex via `{1,254}`. A length-bounded character class at the start of the pattern still forces the engine to attempt every position in an over-long string.

## Tests to write
- Slug happy path: `"hello-world"` returns `true`.
- Slug too long: a string of `MaxSlugLength + 1` characters returns `false` without invoking the regex.
- Email happy path: `"user@example.com"` returns `true`.
- Email too long: a 255-character string returns `false` without invoking the regex.
- Adversarial email: `"a" * 50 + "@"` completes in under 150 ms (regression for catastrophic backtracking).
- Timeout caught: confirm `RegexMatchTimeoutException` is caught and returns `false`, not an unhandled exception.
- NonBacktracking slug: a slug pattern with `RegexOptions.NonBacktracking` throws `NotSupportedException` when a lookahead is added — regression to detect accidental feature use.
