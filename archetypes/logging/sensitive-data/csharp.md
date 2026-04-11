---
schema_version: 1
archetype: logging/sensitive-data
language: csharp
principles_file: _principles.md
libraries:
  preferred: Serilog
  acceptable:
    - Microsoft.Extensions.Logging
    - NLog
  avoid:
    - name: Console.WriteLine for logging
      reason: No redaction hook, no structured fields, no sink configuration — impossible to apply a policy.
    - name: string interpolation in Serilog templates
      reason: Bypasses structured destructuring; the whole interpolated string becomes opaque.
minimum_versions:
  dotnet: "10.0"
---

# Sensitive Data in Logs — C#

## Library choice
Serilog is the preferred logger here because its destructuring pipeline is the easiest place to plug in a redaction policy. You register a `IDestructuringPolicy` that knows about your sensitive types (any record/class carrying `SecretStr`-like fields, any type derived from `SensitiveOptions`), and every log event flows through it automatically. `Microsoft.Extensions.Logging` works with the same redaction idea but requires more plumbing — you configure a `LogEnrichmentOptions` and a `LogRedactor` from `Microsoft.Extensions.Compliance.Redaction` (which is the Microsoft-official redaction package worth knowing). NLog is acceptable and broadly similar in shape. Avoid rolling your own: the redaction layer must see *every* log event, and that only works when it's bolted to the library's pipeline.

## Reference implementation
```csharp
using Serilog;
using Serilog.Core;
using Serilog.Events;

// A sensitive-value wrapper that only renders its own type name.
public readonly struct Redacted<T>(T value)
{
    public T Value { get; } = value; // accessed in crypto / network code only
    public override string ToString() => "<redacted>";
}

// Destructuring policy: every Redacted<T> becomes "<redacted>" in the log event.
public sealed class RedactionPolicy : IDestructuringPolicy
{
    public bool TryDestructure(
        object value, ILogEventPropertyValueFactory factory, out LogEventPropertyValue? result)
    {
        if (value is not null && value.GetType() is { IsGenericType: true } t
            && t.GetGenericTypeDefinition() == typeof(Redacted<>))
        {
            result = new ScalarValue("<redacted>");
            return true;
        }
        result = null;
        return false;
    }
}

public static class LoggingSetup
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
        { "Authorization", "Cookie", "Set-Cookie", "Proxy-Authorization", "X-Api-Key" };

    public static void ConfigureLogger(LoggerConfiguration lc) => lc
        .Destructure.With(new RedactionPolicy())
        .Destructure.ByTransforming<HttpRequestInfo>(r => new
        {
            r.Method, r.Path,
            Headers = r.Headers
                .Where(h => !Sensitive.Contains(h.Key))
                .ToDictionary(h => h.Key, h => h.Value),
        })
        .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);
}
```

## Language-specific gotchas
- **Never** use C# string interpolation inside a Serilog call: `log.Information($"order created {order.Id}")`. The entire interpolated string arrives at the logger as a single message template that Serilog can't destructure. Use template syntax with named parameters: `log.Information("order created {OrderId}", order.Id)`.
- Serilog's `{@Object}` syntax (the `@` destructuring prefix) recursively walks public properties. A config record with a `SecretKey` property will leak unless the `RedactionPolicy` fires *first*. Make sure secret-bearing types use `Redacted<T>` or a projection, and test that `{@Config}` on them renders `<redacted>`.
- `record` types in C# generate `ToString()` that dumps every property. If you're tempted to log one, project it through a "safe view" method first: `log.Information("config {@View}", config.ToSafeView())`.
- `ILogger<T>.LogInformation("token was {Token}", token)` during auth failures is the classic bearer-in-logs bug. Add a repo-level lint rule that disallows the parameter name `token`, `password`, `apiKey`, or `secret` in log templates.
- Exceptions caught and rethrown with a constructed message — `throw new Exception($"failed for user {user}")` — bypass the redaction pipeline because the string is already formed by the time the logger sees it. Either log the exception with structured context separately, or wrap it with a known-safe message and no inlined values.
- `Microsoft.Extensions.Compliance.Redaction` is the "official" redaction package and integrates with `ILogger` enrichment. Consider it if you need attribute-driven redaction (`[DataClassification(...)]`) rather than type-based.
- Production log level stays at `Information`. Do not ship a `Debug` default "for the first week after launch" — body logging at debug is exactly where secrets leak.

## Tests to write
- `Redacted<T>.ToString()` returns `"<redacted>"` — regression test, because this is the one primitive everything else depends on.
- `log.Information("config {@Config}", configWithSecretKey)` writes `"<redacted>"` for the secret field — capture the event via a test sink.
- Sensitive header stripping: log an `HttpRequestInfo` carrying `Authorization` and assert the rendered event contains neither the header name nor the value.
- Exception context: an exception whose message was constructed from a secret value is *not* logged verbatim — this requires the exception-construction site to use a redaction-aware wrapper, so the test is really a lint against `new Exception($"...{secret}...")`.
- Template-parameter lint: a repo-wide test that parses every logging call and fails if a parameter name is `token`, `password`, `secret`, `apiKey`, `cookie`, or `authorization`.
- Log level: assert the production configuration sets minimum level to `Information`, not `Debug` or `Verbose`.
