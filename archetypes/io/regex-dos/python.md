---
schema_version: 1
archetype: io/regex-dos
language: python
principles_file: _principles.md
libraries:
  preferred: re (stdlib) with input length cap
  acceptable:
    - google-re2 (linear-time RE2 bindings for Python)
    - regex (PyPI) with timeout parameter
  avoid:
    - name: re.match / re.fullmatch on unbounded input without length cap
      reason: Python's re module is a backtracking NFA with no built-in timeout; catastrophic patterns block the GIL-holding thread indefinitely.
    - name: re.compile(user_input)
      reason: Attacker-controlled pattern is a direct ReDoS and injection vector.
minimum_versions:
  python: "3.11"
---

# ReDoS Defense — Python

## Library choice
Python's `re` module is a backtracking NFA with no timeout mechanism. The correct defense is a combination of strict input length caps (applied before the regex is called) and patterns audited for catastrophic structure. For patterns that are high-risk or performance-sensitive, replace `re` with `google-re2`, which provides Python bindings to Google's RE2 engine and guarantees linear-time matching. The third-party `regex` module adds a `timeout` parameter but is still a backtracking engine — use it as a fallback, not a primary defense.

## Reference implementation
```python
from __future__ import annotations

import re
from typing import Final

# Audit note: these patterns were reviewed for catastrophic backtracking.
# Max input lengths are documented and enforced before regex evaluation.
_MAX_EMAIL_LEN: Final[int] = 254
_MAX_SLUG_LEN: Final[int]  = 128

# Simple slug: no nested quantifiers, no alternation overlap.
_SLUG_RE: Final[re.Pattern[str]] = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")

# Email: deliberately simple; does not attempt full RFC 5322 compliance.
# Catastrophic email regexes are one of the most common ReDoS sources.
_EMAIL_RE: Final[re.Pattern[str]] = re.compile(
    r"^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,255}\.[a-zA-Z]{2,63}$"
)


def is_valid_slug(value: str) -> bool:
    # Length cap before regex -- O(1) check eliminates the worst-case input class.
    if len(value) > _MAX_SLUG_LEN:
        return False
    return bool(_SLUG_RE.fullmatch(value))


def is_valid_email(value: str) -> bool:
    if len(value) > _MAX_EMAIL_LEN:
        return False
    return bool(_EMAIL_RE.fullmatch(value))


# RE2 alternative -- guaranteed linear time, no timeout needed.
try:
    import re2  # google-re2

    _EMAIL_RE2: re2.Pattern[str] = re2.compile(
        r"^[a-zA-Z0-9._%+\-]{1,64}@[a-zA-Z0-9.\-]{1,255}\.[a-zA-Z]{2,63}$"
    )

    def is_valid_email_safe(value: str) -> bool:
        if len(value) > _MAX_EMAIL_LEN:
            return False
        return bool(_EMAIL_RE2.fullmatch(value))

except ImportError:
    is_valid_email_safe = is_valid_email
```

## Language-specific gotchas
- Python's `re` module holds the GIL during backtracking. A regex that spins for 10 seconds in a web worker process blocks all other coroutines or threads sharing that process — it is a process-level denial of service, not just a slow request.
- `re.fullmatch` anchors both ends implicitly and is slightly more efficient than `^pattern$` with `re.match` for whole-string validation. Use `fullmatch` for validation patterns.
- `google-re2` (`import re2`) is a pip-installable package (`pip install google-re2`) that wraps the C++ RE2 library. It does not support lookaheads, lookbehinds, or backreferences. If your pattern requires these, use a pre-checked `re` pattern with a strict length cap.
- The `regex` module (PyPI, `pip install regex`) adds `timeout` and possessive quantifiers. The `timeout` parameter accepts a float (seconds). Use it when you need features RE2 does not support but cannot afford to simplify the pattern. It is still a backtracking engine.
- Never call `re.compile(user_input)`. If you must allow user-defined search, compile inside a `try/except re.error` block, immediately test the compiled pattern against an adversarial string with a short signal-based timeout, and reject patterns that take too long. This is hard to do correctly — prefer a safer query language.
- `re.match` only matches at the start of the string; `re.search` scans for any position. Using `re.search` with a pattern that lacks anchors significantly increases the number of positions the engine tries, amplifying ReDoS risk.

## Tests to write
- Slug happy path: `"hello-world"` returns `True`.
- Slug too long: a string of `_MAX_SLUG_LEN + 1` characters returns `False` without regex invocation (mock the compiled pattern to assert it was not called).
- Email happy path: `"user@example.com"` returns `True`.
- Email too long: a 255-character string returns `False`.
- Adversarial slug: `"a" * 100 + "!"` completes in under 50 ms.
- Adversarial email: `"a" * 50 + "@"` completes in under 50 ms.
- RE2 import: if `google-re2` is installed, `is_valid_email_safe` uses the RE2 pattern (assert via `type(_EMAIL_RE2)`).
- Pattern immutability: assert `_SLUG_RE` and `_EMAIL_RE` are module-level constants that are not reassigned between tests.
