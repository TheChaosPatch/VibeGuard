---
schema_version: 1
archetype: logging/log-injection
title: Log Injection Defense
summary: Preventing log forging and CRLF injection by sanitizing newlines and control characters from all externally-sourced values before they enter log entries.
applies_to: [csharp, python, go, javascript, typescript, java, kotlin, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - log
  - injection
  - forging
  - crlf
  - newline
  - escape
related_archetypes:
  - logging/sensitive-data
references:
  owasp_asvs: V7.3
  owasp_cheatsheet: Logging Cheat Sheet
  cwe: "117"
---

# Log Injection Defense — Principles

## When this applies
Every log statement that includes a value the application received from outside the process: HTTP request parameters, headers, bodies, file upload names, message-bus payloads, database values originally set by users, third-party API responses. Log injection is exploitable whenever an attacker can insert a newline (`\n`, `\r\n`) or a control character into a logged value and the logging layer renders the line verbatim. The result: the attacker synthesises fake log entries, hides evidence of an attack, or — in systems that parse structured logs — corrupts the structured format (JSON, CEF, GELF) with crafted keys.

## Architectural placement
Sanitisation happens at the boundary where the external value enters the log statement, not inside the logging library. The logging library is responsible for serialising a message to an output channel; it is the application's responsibility to ensure the values it passes are safe to log. Structured logging (passing a dictionary of key-value pairs rather than an interpolated string) significantly reduces the surface area: a well-implemented structured logger serialises each value as a JSON string, which escapes newlines automatically. Unstructured string interpolation directly into a log message is the source of most log injection bugs.

## Principles
1. **Use structured logging; never interpolate user input into log messages.** Pass user-supplied values as named parameters to the logging API (`logger.Warning("Login failed for {Username}", username)`) rather than building a string (`"Login failed for " + username`). A structured logger serialises `username` as a JSON string with newlines escaped.
2. **Strip or replace CR and LF before logging unstructured values.** If you must log a value in a non-structured context, replace `\r` and `\n` with a safe placeholder (e.g., a space or the literal `<CR>`) before passing it to the log API. Do not merely detect them and abort — log the sanitised value so the event is still auditable.
3. **Do not trust that the logging library escapes newlines.** Many popular logging libraries (Log4j in text mode, Python's `logging` module with a plain `Formatter`) do not escape control characters in interpolated values. Validate the behaviour of your specific library and version.
4. **Validate and truncate logged values from external sources.** Log entries with 10 000-character user-agent strings are both an injection vector and a denial-of-service risk against log storage. Enforce a maximum length on values before logging.
5. **Prefer JSON output format end-to-end.** When logs flow from application to log shipper to SIEM in JSON, each layer re-parses and re-serialises. A newline injected into a raw text log cannot survive intact through a JSON decode/encode cycle. Use JSON output from the application log sink to eliminate the vector structurally.
6. **Sanitise all fields — not just the obvious ones.** User-agent, Referer, X-Forwarded-For, file names, error messages from third-party services — all can carry attacker-controlled newlines. Treat every external string as tainted.
7. **Do not log raw exceptions from user input without stripping the message.** Exception messages often contain the value that caused the exception. A `FormatException` message may include the malformed input verbatim; that input may contain CRLF sequences.

## Anti-patterns
- `logger.Info("Request from: " + request.Headers["User-Agent"])` — the header value is passed raw.
- Logging the raw HTTP request body for debugging without sanitising it first, then leaving that code in production.
- Assuming URL encoding prevents log injection — `%0a` decoded before logging becomes `\n`.
- Logging only "suspicious" requests and assuming clean requests do not need sanitisation — attackers craft clean-looking requests that encode newlines differently.
- Using a regex to remove obvious `\n` but missing `\r`, `\r\n`, Unicode line separator (`\u2028`), or paragraph separator (`\u2029`).
- Disabling structured logging output to "simplify" the log format — plain text without structured escaping reintroduces the injection surface.

## References
- OWASP ASVS V7.3 — Log Protection
- OWASP Logging Cheat Sheet
- CWE-117 — Improper Output Neutralization for Logs
