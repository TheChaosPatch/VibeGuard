---
schema_version: 1
archetype: logging/log-injection
language: kotlin
principles_file: _principles.md
libraries:
  preferred: io.github.oshai:kotlin-logging-jvm with logback-classic
  acceptable:
    - org.slf4j:slf4j-api with logback-classic
  avoid:
    - name: string template into logger call
      reason: Kotlin string templates evaluate before the logging framework is called, embedding raw control characters in the log message.
minimum_versions:
  kotlin: "2.1"
  jvm: "21"
---

# Log Injection Defense — Kotlin

## Library choice
`kotlin-logging-jvm` wraps SLF4J with idiomatic Kotlin APIs (`logger.info { "..." }`). The lambda-based API delays string construction until the logging level is active, saving allocations, but the constructed string is still subject to the same control-character concern. Use Logback with `logstash-logback-encoder` for JSON output to get automatic escaping.

## Reference implementation
```kotlin
import io.github.oshai.kotlinlogging.KotlinLogging
import java.util.regex.Pattern

private val log = KotlinLogging.logger {}

private val CONTROL_CHARS: Pattern = Pattern.compile("""[\r\n\x00-\x1F\x7F]""")
private const val MAX_LOG_VALUE = 500

fun sanitize(value: String?): String {
    if (value == null) return "<null>"
    val truncated = if (value.length > MAX_LOG_VALUE) value.take(MAX_LOG_VALUE) + "…" else value
    return CONTROL_CHARS.matcher(truncated).replaceAll(" ")
}

class AuthService {
    fun login(username: String, password: String): Boolean {
        // Lambda is only evaluated when INFO level is active.
        // sanitize() provides belt-and-suspenders for plain-text appenders.
        log.info { "Login attempt for ${sanitize(username)}" }

        val success = validate(username, password)

        if (!success) {
            log.warn { "Login failed for ${sanitize(username)}" }
        }
        return success
    }

    private fun validate(u: String, p: String): Boolean = false
}
```

## Language-specific gotchas
- `log.info("Login for $username")` — the string template evaluates unconditionally regardless of log level. Kotlin-logging's lambda form `log.info { "Login for $username" }` is lazily evaluated but still embeds the value into the string. Sanitise within the lambda for plain-text appenders.
- SLF4J-compatible backends accept `{}` placeholders. `kotlin-logging` supports both the lambda style and `log.info("Login for {}", sanitize(username))` — the latter maps cleanly to Logback's JSON encoder field rendering.
- MDC in Kotlin: use `withLoggingContext("userId" to sanitize(userId)) { ... }` from `kotlin-logging` instead of manual `MDC.put`/`MDC.remove` pairs.
- Coroutine context: `withLoggingContext` propagates MDC across coroutine boundaries via `MDCContext` from `kotlinx-coroutines-slf4j`. Use it to avoid MDC leaking between coroutines.
- Exception handling: `log.error(e) { "Error processing ${sanitize(input)}" }` — `e.message` is not included in the lambda string; the appender formats the exception separately, usually with its message JSON-escaped.

## Tests to write
- `sanitize("user\nroot")` returns `"user root"`.
- `sanitize("a".repeat(600))`.length equals 501.
- Logback `ListAppender` integration: log a value containing `\r\n`; assert no literal newline in the formatted message.
- MDC test: set a value via `withLoggingContext`; assert the value in the log record has had control characters removed.
