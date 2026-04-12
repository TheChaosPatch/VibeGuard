---
schema_version: 1
archetype: logging/log-injection
language: java
principles_file: _principles.md
libraries:
  preferred: org.slf4j:slf4j-api with ch.qos.logback:logback-classic
  acceptable:
    - org.apache.logging.log4j:log4j-api
  avoid:
    - name: String.format / + concatenation into logger.info
      reason: Builds the message string before SLF4J sees it; control characters in user input appear verbatim in the log line.
minimum_versions:
  java: "21"
---

# Log Injection Defense — Java

## Library choice
SLF4J with Logback is the standard Java logging stack. Configure Logback with a JSON encoder (e.g., `net.logstash.logback:logstash-logback-encoder`) to output structured JSON that escapes all control characters in field values. SLF4J's parameterised message API (`logger.info("Login attempt for {}", username)`) defers rendering, but with a plain text encoder the value is still embedded verbatim — the JSON encoder is the structural fix.

## Reference implementation
```java
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import java.util.regex.Pattern;

public final class AuthService {
    private static final Logger log = LoggerFactory.getLogger(AuthService.class);
    private static final Pattern CONTROL_CHARS = Pattern.compile("[\\r\\n\\x00-\\x1F\\x7F]");
    private static final int MAX_LOG_VALUE = 500;

    public static String sanitize(String value) {
        if (value == null) return "<null>";
        String truncated = value.length() > MAX_LOG_VALUE
            ? value.substring(0, MAX_LOG_VALUE) + "…"
            : value;
        return CONTROL_CHARS.matcher(truncated).replaceAll(" ");
    }

    public boolean login(String username, String password) {
        // SLF4J parameterised — deferred rendering, but use JSON encoder for
        // automatic escaping. Pass sanitized value for belt-and-suspenders.
        log.info("Login attempt for {}", sanitize(username));

        boolean success = validate(username, password);

        if (!success) {
            log.warn("Login failed for {}", sanitize(username));
        }
        return success;
    }

    private boolean validate(String u, String p) { return false; }
}
```

## Language-specific gotchas
- SLF4J's `logger.info("Login for " + username)` — string concatenation evaluates before the log call. Control characters in `username` appear in the formatted message string passed to the appender. Always use `{}` placeholders.
- Log4j 2 had the notorious JNDI lookup vulnerability (CVE-2021-44228) via `${}` in logged values. Even after patching, avoid logging user input into Log4j's message template with `${` sequences. Sanitise or use `log4j2.formatMsgNoLookups=true`.
- Logback's `PatternLayout` does not escape control characters. Use `logstash-logback-encoder` (JSON) in production.
- MDC (Mapped Diagnostic Context) values are user-supplied in many frameworks (e.g., request ID from a header). Sanitise before calling `MDC.put(key, value)`.
- Exception messages may contain user input. Do not log `e.getMessage()` directly — log the sanitised message or log the exception object and let the JSON encoder serialise it.

## Tests to write
- `AuthService.sanitize("user\nroot")` returns `"user root"`.
- `AuthService.sanitize("a".repeat(600)).length()` equals 501 (500 chars + "…").
- Logback integration: configure a `ListAppender`; log a value containing `\r\n`; assert the formatted message contains no literal newline.
- MDC injection: set `MDC.put("userId", "a\nb")`; assert the sanitised value `"a b"` is what the JSON encoder emits.
