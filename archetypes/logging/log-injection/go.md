---
schema_version: 1
archetype: logging/log-injection
language: go
principles_file: _principles.md
libraries:
  preferred: log/slog
  acceptable:
    - go.uber.org/zap
    - github.com/rs/zerolog
  avoid:
    - name: fmt.Sprintf into log.Println
      reason: fmt.Sprintf evaluates control characters into the string before the logging package sees it, producing multi-line log entries.
minimum_versions:
  go: "1.23"
---

# Log Injection Defense — Go

## Library choice
`log/slog` (stdlib, Go 1.21+) with `slog.NewJSONHandler` outputs structured JSON, escaping newlines and control characters in all string values automatically. `go.uber.org/zap` and `github.com/rs/zerolog` also produce JSON output with the same automatic escaping. Plain `log.Printf` and `fmt.Fprintf(os.Stderr, ...)` do not escape control characters — avoid them for values that include user input.

## Reference implementation
```go
package auth

import (
    "context"
    "log/slog"
    "os"
    "regexp"
    "unicode/utf8"
)

var controlChars = regexp.MustCompile(`[\x00-\x1F\x7F\r\n]`)

const maxLogValue = 500

// Sanitize strips control characters and truncates for plain-text log contexts.
// Not needed when using slog with JSONHandler — provided for belt-and-suspenders.
func Sanitize(s string) string {
    if utf8.RuneCountInString(s) > maxLogValue {
        runes := []rune(s)
        s = string(runes[:maxLogValue]) + "…"
    }
    return controlChars.ReplaceAllString(s, " ")
}

var logger = slog.New(slog.NewJSONHandler(os.Stderr, nil))

type AuthService struct{}

func (a *AuthService) Login(ctx context.Context, username, password string) bool {
    // Correct: username is a structured attribute — slog JSONHandler serialises
    // it as a JSON string, escaping any newlines.
    logger.InfoContext(ctx, "login attempt", slog.String("username", username))

    success := a.validate(username, password)
    if !success {
        logger.WarnContext(ctx, "login failed", slog.String("username", username))
    }
    return success
}

func (a *AuthService) validate(u, p string) bool { return false }
```

## Language-specific gotchas
- `slog.NewTextHandler` (key=value plain text) does quote string values but does NOT escape `\n` inside the quoted string. A value `"a\nb"` appears as `username="a\nb"` which many log parsers split into two lines. Use `NewJSONHandler` for production.
- `fmt.Sprintf("login attempt for %s", username)` passed as the `slog` message string is safe because slog logs the message as a JSON string field that gets escaped. However, the message field should be a static string — put dynamic values in attributes.
- `zerolog`'s `Str("username", username)` method escapes control characters in JSON output. `zerolog`'s `Msg(username)` embeds the value directly into the message field — avoid it for user-controlled values.
- `log.Printf("login: %s", username)` writes to stdout (or the configured writer) without JSON escaping — do not use for user-supplied values in production.
- Go strings may contain null bytes (`\x00`). Include `\x00` in the sanitiser regex — some log aggregators split on null bytes.

## Tests to write
- `Sanitize("user\nroot")` returns `"user root"`.
- `Sanitize(strings.Repeat("a", 600))` returns a string of rune length 501.
- slog JSONHandler: log a value containing `\r\n`; capture output; unmarshal JSON; assert no literal newline in the `username` field value.
- Negative: `slog.NewTextHandler` output with `\n` in a value — assert it produces a visually broken log line, documenting why JSON output is preferred.
