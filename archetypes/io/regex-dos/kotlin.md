---
schema_version: 1
archetype: io/regex-dos
language: kotlin
principles_file: _principles.md
libraries:
  preferred: kotlin.text.Regex (wraps java.util.regex.Pattern) with input length cap
  acceptable:
    - com.google.re2j (RE2/J — linear-time)
  avoid:
    - name: Regex(userInput)
      reason: Attacker-supplied pattern on a backtracking NFA; ReDoS and injection vector.
    - name: String.matches(regex) in hot paths
      reason: Compiles a new Pattern on every call; hides O(n) compile cost.
minimum_versions:
  kotlin: "2.0"
  java: "21"
---

# ReDoS Defense — Kotlin

## Library choice
Kotlin's `Regex` class wraps `java.util.regex.Pattern` and inherits its backtracking NFA engine. All Java ReDoS defenses apply: pre-compile patterns as `object`-level or companion object constants, cap input length before evaluation, and consider RE2/J for high-risk patterns. Kotlin's `object` declarations make singleton pattern holders idiomatic. The `containsMatchIn` / `matches` extension functions are convenient but create a new `Matcher` per call, which is correct for thread safety.

## Reference implementation
```kotlin
object InputValidation {
    private const val MAX_EMAIL_LEN = 254
    private const val MAX_SLUG_LEN  = 128

    // Regex is pre-compiled at object initialization. Regex is thread-safe; MatchResult is not shared.
    private val SLUG_RE  = Regex("""^[a-z0-9]+(?:-[a-z0-9]+)*$""")
    private val EMAIL_RE = Regex("""^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,253}\.[a-zA-Z]{2,63}$""")

    fun isValidSlug(value: String): Boolean {
        if (value.length > MAX_SLUG_LEN) return false
        return SLUG_RE.matches(value)
    }

    fun isValidEmail(value: String): Boolean {
        if (value.length > MAX_EMAIL_LEN) return false
        return EMAIL_RE.matches(value)
    }
}
```

## Language-specific gotchas
- Kotlin's `Regex` is a thin wrapper; the underlying `java.util.regex.Pattern` does the matching. All Java-side thread-safety rules apply: `Regex` (the wrapper) is safe to share; do not hold `MatchResult` across threads.
- `Regex.matches(input)` anchors the match to the entire input, equivalent to Java's `Matcher.matches()`. This is correct for validation. `Regex.containsMatchIn(input)` scans for any position, equivalent to `Matcher.find()` — slower and more backtracking-prone for validation use cases.
- Raw string literals (`"""..."""`) avoid double-escaping backslashes in regex patterns, which makes patterns more readable and less error-prone than escaped Java strings.
- Kotlin `object` declarations are initialized lazily (on first access) and are thread-safe via class loading semantics. Placing patterns here means they are compiled exactly once, even in a multi-threaded application.
- RE2/J (`com.google.re2j:re2j`) can be used directly from Kotlin: `import com.google.re2j.Pattern; val p = Pattern.compile(...)`. The Kotlin `Regex` wrapper cannot be used over RE2/J — call the Java `Matcher` API directly.
- Kotlin coroutines run on thread pools; a coroutine blocked on a catastrophic regex match pins the dispatcher thread and prevents other coroutines from running on that thread, degrading throughput across all coroutines on that dispatcher.

## Tests to write
- Slug happy path: `isValidSlug("hello-world")` returns `true`.
- Slug too long: a string of `MAX_SLUG_LEN + 1` characters returns `false`.
- Email happy path: `isValidEmail("user@example.com")` returns `true`.
- Email too long: a 255-character string returns `false`.
- Adversarial slug: `"a".repeat(200) + "!"` completes in under 100 ms.
- Thread safety: dispatch `isValidEmail` to 20 coroutines simultaneously; assert no exception and all results are `false` for an invalid input.
- Object singleton: assert `InputValidation.SLUG_RE === InputValidation.SLUG_RE` (same reference, compiled once).
