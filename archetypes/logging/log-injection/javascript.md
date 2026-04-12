---
schema_version: 1
archetype: logging/log-injection
language: javascript
principles_file: _principles.md
libraries:
  preferred: pino
  acceptable:
    - winston
  avoid:
    - name: console.log with template literals
      reason: Template literals embed user input verbatim into the console output; control characters are not escaped.
minimum_versions:
  node: "22"
---

# Log Injection Defense — JavaScript

## Library choice
`pino` outputs newline-delimited JSON by default. Every field value passed as a key in the log object is serialised as a JSON string, so newlines and control characters are automatically escaped. `winston` with `winston.format.json()` does the same. `console.log` and `console.error` do not escape control characters and should not be used for user-supplied values in production code.

## Reference implementation
```javascript
import pino from "pino";

const log = pino({ level: "info" }); // outputs NDJSON to stdout by default

const MAX_LOG_VALUE = 500;
const CONTROL_CHARS = /[\x00-\x1f\x7f\r\n]/g;

function sanitize(value) {
    if (typeof value !== "string") return String(value);
    const truncated = value.length > MAX_LOG_VALUE
        ? value.slice(0, MAX_LOG_VALUE) + "…"
        : value;
    return truncated.replace(CONTROL_CHARS, " ");
}

export async function login(username, password) {
    // Correct: username is a field in the log object — pino serialises it as
    // a JSON string with newlines escaped.
    log.info({ username }, "login attempt");

    const success = await validate(username, password);

    if (!success) {
        log.warn({ username: sanitize(username) }, "login failed");
    }
    return success;
}

async function validate(u, p) { return false; }
```

## Language-specific gotchas
- `console.log(`Login attempt for ${username}`)` — the template literal evaluates before `console.log` is called. Any `\n` in `username` splits the console output into multiple lines. Some log aggregators interpret the second line as a new log entry.
- `pino`'s `redact` option can mask sensitive fields (e.g., `password`), but it does not sanitise control characters. Use `sanitize()` for user-supplied values that must appear in logs.
- `pino-pretty` transforms NDJSON to human-readable output in development. It does not sanitise control characters — this is acceptable in development but is the reason production sinks must use NDJSON or JSON.
- `winston` with `winston.format.simple()` or `winston.format.cli()` does not JSON-escape values. Always use `winston.format.json()` in production.
- Node.js `process.stdout.write(msg)` accepts strings with raw control characters. Never construct log lines manually.

## Tests to write
- `sanitize("user\nroot")` returns `"user root"`.
- `sanitize("a".repeat(600))` returns a string of length 501.
- pino integration: capture stdout; call `log.info({ username: "a\nb" }, "test")`; parse the NDJSON line; assert the `username` field value contains no literal newline.
- `console.log` negative test: log `"a\nb"` and assert it produces two lines — document this as the reason `console.log` is avoided.
