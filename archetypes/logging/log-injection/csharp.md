---
schema_version: 1
archetype: logging/log-injection
language: csharp
principles_file: _principles.md
libraries:
  preferred: Serilog
  acceptable:
    - Microsoft.Extensions.Logging
  avoid:
    - name: string interpolation into log message template
      reason: Interpolating user input into the message template string rather than passing it as a structured parameter embeds raw control characters into the log output.
minimum_versions:
  dotnet: "10.0"
---

# Log Injection Defense — C#

## Library choice
`Serilog` with a structured sink (e.g., `Serilog.Sinks.Console` in JSON output mode, `Serilog.Sinks.Seq`, or `Serilog.Sinks.OpenTelemetry`) serialises each named parameter as a JSON string, which escapes newlines automatically. `Microsoft.Extensions.Logging` with a structured provider does the same. The danger zone is unstructured sinks that render a plain text template — in those cases, you must sanitise values before logging.

## Reference implementation
```csharp
using Serilog;
using System.Text.RegularExpressions;

public static partial class LogSanitizer
{
    // Strip CR, LF, and other control characters from values that will be
    // logged in non-structured (plain text) contexts.
    [GeneratedRegex(@"[\r\n\x00-\x1F\x7F]")]
    private static partial Regex ControlChars();

    public static string Sanitize(string? value, int maxLength = 500)
    {
        if (value is null) return "<null>";
        var trimmed = value.Length > maxLength ? value[..maxLength] + "…" : value;
        return ControlChars().Replace(trimmed, " ");
    }
}

public sealed class AuthService(ILogger<AuthService> logger)
{
    public async Task<bool> LoginAsync(string username, string password)
    {
        // Correct: username is a named parameter — Serilog serialises it as a
        // JSON string in structured sinks, escaping any newlines.
        logger.LogInformation("Login attempt for {Username}", username);

        bool success = await ValidateAsync(username, password);

        if (!success)
            // Sanitize explicitly for plain-text fallback contexts.
            logger.LogWarning("Failed login for {Username}",
                LogSanitizer.Sanitize(username));

        return success;
    }

    private Task<bool> ValidateAsync(string u, string p) => Task.FromResult(false);
}
```

## Language-specific gotchas
- `logger.LogInformation($"Login attempt for {username}")` embeds the value into the template string before `ILogger` sees it. Any newlines in `username` appear verbatim in the log. Always use the overload with a template and separate arguments.
- Serilog's `Sinks.Console` in default (plain text) mode does not escape newlines in destructured properties. Use `new JsonFormatter()` or configure the sink with `outputTemplate` that does not inline raw values.
- `{@Username}` (destructuring operator) in Serilog serialises the object; `{Username}` serialises as a string scalar. For user-supplied strings, use `{Username}` (scalar) — destructuring a string does nothing extra but is semantically misleading.
- Unicode line separator (`\u2028`) and paragraph separator (`\u2029`) are not `\n` but cause visual line breaks in some log viewers. Include them in the sanitiser regex if targeting non-JSON log viewers.

## Tests to write
- `LogSanitizer.Sanitize("user\nroot")` returns `"user root"` (newline replaced with space).
- `LogSanitizer.Sanitize(new string('a', 600))` returns a string of length 501 (500 + ellipsis character).
- Integration: log a value containing `\r\n` using a structured JSON sink; parse the output JSON and assert the value field contains no literal newlines.
- Verify the structured log output contains `"Username":"attacker value"` as a string, not as a nested JSON key.
