---
schema_version: 1
archetype: logging/audit-trail
language: csharp
principles_file: _principles.md
libraries:
  preferred: Custom audit service over append-only table
  acceptable:
    - Serilog (dedicated audit sink)
    - MediatR (pipeline behavior for automatic audit)
  avoid:
    - name: ILogger for audit events
      reason: Application logs are sampled, rotated, and dropped under load. Audit events must never be dropped.
    - name: EF Core SaveChanges interceptor as sole audit mechanism
      reason: Interceptors can be bypassed with raw SQL. Use an explicit audit service.
minimum_versions:
  dotnet: "10.0"
---

# Security Audit Trail — C#

## Library choice
Audit events go through a dedicated `IAuditWriter` interface, not through `ILogger<T>`. The implementation writes to an append-only database table where the application's database user has INSERT but not UPDATE or DELETE. For tamper evidence, each event includes an HMAC of its content plus the previous event's hash. `Serilog.Sinks.File` with `AuditTo` semantics (throws on write failure) is an acceptable secondary sink. `MediatR` pipeline behaviors can automatically emit audit events for commands, but the audit writer is the authoritative store, not the MediatR pipeline.

## Reference implementation
```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed record AuditEvent
{
    public required Guid Id { get; init; }
    public required string ActorId { get; init; }
    public required string Action { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string CorrelationId { get; init; }
    public required string Outcome { get; init; } // "success" | "denied" | "failure"
    public string? TargetId { get; init; }
    public string? Detail { get; init; }
    public required string PreviousHash { get; init; }
    public string Hash { get; init; } = "";
}

public interface IAuditWriter
{
    Task WriteAsync(AuditEvent ev, CancellationToken ct);
}

public sealed class DbAuditWriter(
    IDbContextFactory<AuditDbContext> dbFactory, byte[] hmacKey) : IAuditWriter
{
    public async Task WriteAsync(AuditEvent ev, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(ev with { Hash = "" });
        var hash = ComputeHmac(ev.PreviousHash + payload);
        var signed = ev with { Hash = hash };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.AuditEvents.Add(signed);
        await db.SaveChangesAsync(ct); // INSERT only — no UPDATE/DELETE grants
    }

    private string ComputeHmac(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hmac = HMACSHA256.HashData(hmacKey, bytes);
        return Convert.ToHexStringLower(hmac);
    }
}
```

## Language-specific gotchas
- The audit table's database user must have only INSERT permission. If the application can UPDATE or DELETE audit rows, a compromised application can cover its tracks. Use a separate connection string with restricted grants.
- `DateTimeOffset.UtcNow` is the correct timestamp source, not `DateTime.Now`. `DateTime.Now` is local time and ambiguous during DST transitions. Audit timestamps must be UTC and server-generated — never accept a timestamp from the client.
- `SaveChangesAsync` must not be called inside a `try/catch` that swallows the exception. If the audit write fails, the business operation must also fail. This is the opposite of application logging, where a failed log line should not crash the request.
- The HMAC key belongs in a secrets manager (Azure Key Vault, AWS Secrets Manager), not in `appsettings.json`. If the key is compromised, an attacker can forge audit events with valid hashes.
- EF Core's `SaveChangesInterceptor` can capture entity changes automatically, but it can be bypassed by raw SQL, bulk operations, or direct database access. Use it as a convenience layer on top of the explicit `IAuditWriter`, not as a replacement.
- Do not log PII in the `Detail` field. Use actor IDs and target IDs that can be resolved through the identity service at query time. This limits blast radius on audit-log compromise.
- `CorrelationId` should flow from the HTTP request (via middleware that reads `X-Correlation-Id` or generates one) and be passed to every `AuditEvent`. Without it, correlating a user action across multiple services requires timestamp guessing.

## Tests to write
- Required fields: constructing an `AuditEvent` without `ActorId`, `Action`, `Timestamp`, `CorrelationId`, or `Outcome` fails at compile time (`required` keyword).
- Hash chain: write two events — assert the second event's `PreviousHash` matches the first event's `Hash`, and `ComputeHmac` on the chain is consistent.
- Tamper detection: modify a stored event's `Detail` field and recompute — assert the hash no longer matches.
- Insert-only: attempt an UPDATE on the audit table with the application's connection string — assert a permission error.
- Audit failure propagation: mock the database to throw on `SaveChangesAsync` — assert the business operation also fails (the audit write is not fire-and-forget).
