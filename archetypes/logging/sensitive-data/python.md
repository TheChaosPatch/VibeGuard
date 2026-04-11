---
schema_version: 1
archetype: logging/sensitive-data
language: python
principles_file: _principles.md
libraries:
  preferred: structlog
  acceptable:
    - logging (stdlib) + contextvars
    - loguru
  avoid:
    - name: print() for logging
      reason: No level, no structure, no redaction hook; lands in whatever stream the process happens to own.
    - name: f-strings inside log calls
      reason: The template becomes opaque to any redaction or formatter; the entire formed string arrives at the handler.
minimum_versions:
  python: "3.11"
---

# Sensitive Data in Logs — Python

## Library choice
`structlog` is the preferred logger because it makes the structured-event shape the *default* — you pass named kwargs, not format strings — and it has a clean processor-chain model where a redaction function can run before any formatter sees the event. The stdlib `logging` module is acceptable and often unavoidable, but it needs a hand-rolled redaction `Filter` plus discipline from every caller to pass `extra={...}` rather than interpolating into the message. `loguru` is fine for smaller services but its "happy path" encourages f-string formatting, which is exactly what we're trying to prevent.

## Reference implementation
```python
from __future__ import annotations
from dataclasses import dataclass
from typing import Any
import structlog

_SENSITIVE = frozenset({
    "password", "token", "secret", "api_key", "apikey", "authorization",
    "cookie", "set-cookie", "proxy-authorization", "x-api-key", "credit_card", "ssn",
})
_SENSITIVE_HEADERS = frozenset(k for k in _SENSITIVE if "-" in k or k.startswith("x-"))
_HEADER_KEYS = ("headers", "request_headers", "response_headers")


@dataclass(frozen=True, slots=True)
class Redacted:
    """Wrap a secret so every stringification is '<redacted>'."""
    _value: str  # unwrap via .reveal() only in crypto / network code
    def reveal(self) -> str: return self._value
    def __repr__(self) -> str: return "<redacted>"
    __str__ = __repr__


def _redact(key: str, value: Any) -> Any:
    if key.lower() in _SENSITIVE or isinstance(value, Redacted):
        return "<redacted>"
    if key.lower() in _HEADER_KEYS and isinstance(value, dict):
        return {h: ("<redacted>" if h.lower() in _SENSITIVE_HEADERS else v)
                for h, v in value.items()}
    return value


def redact_processor(_logger: Any, _name: str, event: dict[str, Any]) -> dict[str, Any]:
    """structlog processor: strip sensitive keys before any rendering."""
    return {k: _redact(k, v) for k, v in event.items()}


structlog.configure(
    processors=[
        structlog.contextvars.merge_contextvars,
        redact_processor,  # runs BEFORE the renderer
        structlog.processors.add_log_level,
        structlog.processors.TimeStamper(fmt="iso"),
        structlog.processors.JSONRenderer(),
    ],
    wrapper_class=structlog.make_filtering_bound_logger(20),  # INFO
    cache_logger_on_first_use=True,
)
log = structlog.get_logger()
```

## Language-specific gotchas
- **Never** write `log.info(f"order created for {customer}")`. The f-string is evaluated at the call site before the logger sees it, and the entire object is now stringified through `__str__`, which bypasses every redaction processor. Use kwargs: `log.info("order.created", customer_id=customer.id)`.
- The event name (`"order.created"`) is a stable string, not a sentence. This makes logs groupable, searchable, and translatable to metrics. "Formatted English" logs are a sign that someone's been using the logger like `print()`.
- `Redacted.__repr__` *and* `__str__` both return `"<redacted>"`. Override both — Python falls back to `__repr__` when `__str__` is absent but uses `__str__` in format contexts, and forgetting one causes an inconsistent leak on the first `%s` that catches it.
- The redaction processor runs on `event_dict`, which is what structlog passes through its pipeline. It does *not* see values embedded in an `Exception.args[0]`. If you log `log.error("failed", exc_info=exc)` where `exc` was constructed with a formatted secret, the formatted string is already in the exception and the redaction layer has no hook to rewrite it.
- stdlib `logging` equivalents: install a `logging.Filter` subclass on the root logger that walks `record.args` and `record.__dict__` and swaps sensitive keys. This works but is more fragile because not every caller uses `extra={}` — some pass `%s` format strings, and the filter sees the formatted message too late.
- Uvicorn / gunicorn access logs are a separate pipeline — they log `Authorization` headers by default on some misconfigurations. Configure the access logger's format explicitly, not just the application logger.
- Log level in production stays at INFO. DEBUG-in-prod is where the body logs live, and the body logs are where the secrets live.

## Tests to write
- `Redacted.__repr__` returns `"<redacted>"`; `f"{Redacted('hunter2')}"` does too — this covers both str/repr paths.
- `log.info("event", password="hunter2")` → captured log dict has `password == "<redacted>"`.
- `log.info("event", token=Redacted("secret"))` → captured dict has `token == "<redacted>"`.
- Header dict redaction: `log.info("http.call", headers={"Authorization": "Bearer x", "Content-Type": "application/json"})` → authorization is `<redacted>` and content-type is not.
- Unknown field passthrough: `log.info("event", item_count=5)` → `item_count == 5` (the redactor is an allowlist of sensitive names, not a blocklist of safe names).
- No f-strings lint: repo-wide AST test that scans every `log.*()` / `logger.*()` call and fails if the first positional argument is an f-string.
- Exception leakage regression: construct an exception via `Exception(f"failed for {secret}")`, log it, and assert the captured log *does* contain the secret — then add a comment pointing at this test as the reason exception messages must be built from known-safe strings.
- Production level: assert configuration sets the filtering bound logger to INFO, not DEBUG.
