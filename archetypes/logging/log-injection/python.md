---
schema_version: 1
archetype: logging/log-injection
language: python
principles_file: _principles.md
libraries:
  preferred: structlog
  acceptable:
    - logging (stdlib)
  avoid:
    - name: f-string into logging.info/warning
      reason: An f-string evaluates the value into the message string before the logging framework sees it, embedding raw control characters verbatim.
minimum_versions:
  python: "3.12"
---

# Log Injection Defense — Python

## Library choice
`structlog` outputs structured JSON by default, which escapes newlines and control characters in all values. The stdlib `logging` module with a `json` formatter (e.g., `python-json-logger`) achieves the same result. The critical requirement: never use f-strings or `%` formatting to build the log message; always pass extra fields as keyword arguments or a dict.

## Reference implementation
```python
from __future__ import annotations
import re
import structlog

_CONTROL_CHARS = re.compile(r"[\r\n\x00-\x1f\x7f]")
_MAX_LOG_VALUE = 500

log = structlog.get_logger()


def sanitize(value: str | None, max_length: int = _MAX_LOG_VALUE) -> str:
    """Strip control characters; truncate to max_length."""
    if value is None:
        return "<null>"
    truncated = value[:max_length] + ("…" if len(value) > max_length else "")
    return _CONTROL_CHARS.sub(" ", truncated)


class AuthService:
    def login(self, username: str, password: str) -> bool:
        # Correct: username is a keyword argument — structlog serialises it as
        # a JSON string field, escaping any control characters.
        log.info("login_attempt", username=username)

        success = self._validate(username, password)

        if not success:
            log.warning("login_failed", username=sanitize(username))

        return success

    def _validate(self, u: str, p: str) -> bool:
        return False
```

## Language-specific gotchas
- `logging.info(f"Login attempt for {username}")` — the f-string executes before `logging.info` is called. The formatted string, including any `\n` in `username`, is stored as the log message. Never do this.
- `logging.info("Login attempt for %s", username)` — `%s` formatting is deferred until the record is rendered, but the stdlib `Formatter` still renders the value verbatim into a plain-text line. Use a JSON formatter to get automatic escaping.
- `structlog` processors run in order. Add `structlog.processors.format_exc_info` before `structlog.processors.JSONRenderer` to ensure exception messages (which may contain user input) are serialised as JSON strings.
- The `extra={"username": username}` pattern with stdlib `logging` adds the field to the `LogRecord`. A plain `Formatter` does not include `extra` fields in the output; a JSON formatter does — verify your formatter includes extras.
- `logging.captureWarnings(True)` routes Python `warnings` through the logging system. If a warning message includes user input (e.g., a `DeprecationWarning` from a library that embeds the offending value), it is logged unescaped unless you use a JSON formatter.

## Tests to write
- `sanitize("user\nroot")` returns `"user root"`.
- `sanitize("a" * 600)` returns a string of length 501.
- structlog integration: capture log output in JSON mode; log a value containing `\r\n`; parse the JSON and assert the value field has no literal newline characters.
- Assert that `log.info("login_attempt", username=username)` does NOT produce a log line with a raw newline when `username = "a\nb"` — verify via captured output.
