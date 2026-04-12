---
schema_version: 1
archetype: logging/log-injection
language: typescript
principles_file: _principles.md
libraries:
  preferred: pino
  acceptable:
    - winston
  avoid:
    - name: console.log with template literals
      reason: Template literals embed user input verbatim; control characters are not escaped.
minimum_versions:
  node: "22"
  typescript: "5.7"
---

# Log Injection Defense — TypeScript

## Library choice
`pino` with `@types/pino` (or the bundled types in recent versions). The structured logging pattern is identical to JavaScript; TypeScript adds compile-time checks that the log object shape is correct and that typed fields are not accidentally omitted. Use a branded type or validation wrapper to mark sanitised log values.

## Reference implementation
```typescript
import pino from "pino";

const log = pino({ level: "info" });

const MAX_LOG_VALUE = 500;
const CONTROL_CHARS = /[\x00-\x1f\x7f\r\n]/g;

// Branded type — marks a value as having passed through sanitize().
type SafeLogValue = string & { readonly __brand: "SafeLogValue" };

function sanitize(value: string, maxLength = MAX_LOG_VALUE): SafeLogValue {
    const truncated = value.length > maxLength
        ? value.slice(0, maxLength) + "…"
        : value;
    return truncated.replace(CONTROL_CHARS, " ") as SafeLogValue;
}

interface LoginLogFields {
    username: SafeLogValue;
}

export async function login(username: string, password: string): Promise<boolean> {
    const safeUsername = sanitize(username);
    // safeUsername is SafeLogValue — type-checked at compile time.
    log.info<LoginLogFields>({ username: safeUsername }, "login attempt");

    const success = await validate(username, password);

    if (!success) {
        log.warn<LoginLogFields>({ username: safeUsername }, "login failed");
    }
    return success;
}

async function validate(_u: string, _p: string): Promise<boolean> { return false; }
```

## Language-specific gotchas
- The `SafeLogValue` branded type enforces at compile time that only sanitised values are passed to log fields typed as `SafeLogValue`. Raw `string` is not assignable without an explicit cast, which is visible in code review.
- TypeScript generic parameters on `log.info<T>` constrain the shape of the log object; mismatched field types are caught at compile time.
- `pino.Logger` is not compatible with `console.log`'s signature. If you define a logger interface for dependency injection, do not use `Console` as the type — define a minimal `Logger` interface with `info`, `warn`, `error` methods that accept objects.
- `strictNullChecks: true` must be on — without it, `null` values pass `string` type checks silently, bypassing `sanitize`.
- When using `pino-http` middleware, request fields (URL, headers) are logged automatically. Configure `pino-http`'s `customProps` to pass those values through `sanitize()`.

## Tests to write
- `sanitize("user\nroot")` returns a value equal to `"user root"` and satisfies the `SafeLogValue` brand.
- Compile-time test: assert that passing a raw `string` where `SafeLogValue` is expected produces a TypeScript type error (use `ts-expect-error`).
- pino integration: capture stdout; log `{ username: sanitize("a\nb") }`; parse JSON; assert no literal newline in `username`.
- `sanitize("a".repeat(600)).length` equals 501.
