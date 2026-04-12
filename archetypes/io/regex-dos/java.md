---
schema_version: 1
archetype: io/regex-dos
language: java
principles_file: _principles.md
libraries:
  preferred: java.util.regex.Pattern (pre-compiled, with input length cap)
  acceptable:
    - com.google.re2j (RE2/J — linear-time, no backtracking)
  avoid:
    - name: Pattern.compile(userInput)
      reason: Attacker-supplied pattern can be exponential on Java's backtracking NFA; also exposes injection via pattern syntax.
    - name: String.matches(pattern) in hot paths
      reason: Compiles the pattern on every invocation; O(n) compile cost hidden behind a convenience method.
minimum_versions:
  java: "21"
---

# ReDoS Defense — Java

## Library choice
`java.util.regex.Pattern` uses a backtracking NFA. Pre-compile all patterns as `static final` fields to avoid per-request compilation overhead. For patterns that process untrusted high-volume input or that have complex alternations, use `com.google.re2j` (RE2/J), which provides a `java.util.regex`-compatible API backed by Google RE2's linear-time engine. RE2/J does not support lookaheads, lookbehinds, or backreferences. Apply an input length cap before every `Matcher.matches()` or `Matcher.find()` call.

## Reference implementation
```java
import java.util.regex.Pattern;

public final class InputValidation {
    private static final int MAX_EMAIL_LEN = 254;
    private static final int MAX_SLUG_LEN  = 128;

    // Pre-compiled at class load -- Pattern is thread-safe; Matcher is not.
    private static final Pattern SLUG_RE = Pattern.compile(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$"
    );
    private static final Pattern EMAIL_RE = Pattern.compile(
        "^[a-zA-Z0-9._%+\\-]{1,64}@[a-zA-Z0-9.\\-]{1,253}\\.[a-zA-Z]{2,63}$"
    );

    private InputValidation() {}

    public static boolean isValidSlug(String value) {
        if (value == null || value.length() > MAX_SLUG_LEN) return false;
        // Matcher.matches() is not thread-safe; create a new Matcher per call.
        return SLUG_RE.matcher(value).matches();
    }

    public static boolean isValidEmail(String value) {
        if (value == null || value.length() > MAX_EMAIL_LEN) return false;
        return EMAIL_RE.matcher(value).matches();
    }
}
```

## Language-specific gotchas
- `Pattern` is thread-safe; `Matcher` is not. Always call `pattern.matcher(input)` to get a fresh `Matcher` per thread or per invocation — never store a `Matcher` in a static or shared field.
- `String.matches(regex)` compiles a new `Pattern` on every call. In a request handler, this means pattern compilation at every request. Use `static final Pattern` constants.
- `Pattern.compile` with `Pattern.DOTALL` and a `.*` in a quantifier dramatically worsens backtracking worst cases. Be especially cautious with patterns like `(?s)(.*foo)+` — the `DOTALL` flag means `.` now matches newlines, widening the set of strings that trigger catastrophic backtracking.
- RE2/J (`com.google.re2j`) is available from Maven Central as `com.google.re2j:re2j`. Its API mirrors `java.util.regex`: `com.google.re2j.Pattern.compile()` and `Matcher`. Drop it in where you currently use `java.util.regex` for high-risk patterns.
- Java 21 introduced Virtual Threads (Project Loom). A regex that pins a virtual thread (via a native or synchronized block in the regex engine) prevents the carrier thread from being reused. Regex matching in `java.util.regex` uses synchronized sections internally; long-running matches on virtual threads can cause carrier thread starvation.
- `Matcher.find()` scans for a match at any position, which means the engine tries every position in the string. For whole-string validation use `Matcher.matches()`, which anchors implicitly and is faster.

## Tests to write
- Slug happy path: `"hello-world"` returns `true`.
- Slug null input: `null` returns `false` without NullPointerException.
- Slug too long: a string of `MAX_SLUG_LEN + 1` characters returns `false`.
- Email happy path: `"user@example.com"` returns `true`.
- Email too long: a 255-character string returns `false`.
- Adversarial slug: `"a".repeat(200) + "!"` completes in under 100 ms.
- Thread safety: call `isValidEmail` from 20 threads simultaneously; assert no exception and all results are correct.
- Pattern field: use reflection to assert `EMAIL_RE` and `SLUG_RE` are `static final` fields of type `Pattern`.
