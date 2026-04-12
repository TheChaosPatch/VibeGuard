---
schema_version: 1
archetype: concurrency/race-conditions
language: csharp
principles_file: _principles.md
libraries:
  preferred: Entity Framework Core (optimistic concurrency)
  acceptable:
    - Npgsql (advisory locks)
    - StackExchange.Redis (distributed locks)
  avoid:
    - name: In-process lock for cross-instance coordination
      reason: Only protects the current process. Invisible to other instances.
    - name: Thread.Sleep for retry backoff
      reason: Blocks the thread pool thread. Use Task.Delay with jitter.
minimum_versions:
  dotnet: "10.0"
---

# Race Condition Defense — C#

## Library choice
EF Core's `[ConcurrencyCheck]` and `[Timestamp]` attributes provide optimistic concurrency out of the box — the generated SQL includes `WHERE [Version] = @old` and throws `DbUpdateConcurrencyException` when zero rows are affected. For pessimistic locking, use raw SQL with `SELECT ... FOR UPDATE` (PostgreSQL) or `UPDLOCK` hints (SQL Server). For distributed coordination, `StackExchange.Redis` with RedLock or PostgreSQL advisory locks are the established options. In-process `SemaphoreSlim` protects only the current process and must not be used for cross-instance invariants.

## Reference implementation
```csharp
using Microsoft.EntityFrameworkCore;

public class AccountEntity
{
    public Guid Id { get; set; }
    public decimal Balance { get; set; }

    [Timestamp]
    public byte[] Version { get; set; } = [];
}

public sealed class PaymentService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<bool> DebitAsync(
        Guid accountId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Idempotency: unique index on IdempotencyKey rejects duplicates
        if (await db.Payments.AnyAsync(p => p.IdempotencyKey == idempotencyKey, ct))
        {
            await tx.CommitAsync(ct);
            return true; // already processed
        }

        var account = await db.Accounts
            .FromSqlInterpolated(
                $"SELECT * FROM Accounts WITH (UPDLOCK) WHERE Id = {accountId}")
            .SingleAsync(ct);

        if (account.Balance < amount)
            return false;

        account.Balance -= amount;
        db.Payments.Add(new PaymentEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            Amount = amount,
            IdempotencyKey = idempotencyKey,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
```

## Language-specific gotchas
- `[Timestamp]` maps to SQL Server's `rowversion`, which the database manages automatically. On PostgreSQL, use a manual `[ConcurrencyCheck]` integer column and increment it in application code. EF Core checks the old value in the `WHERE` clause either way.
- `DbUpdateConcurrencyException` means another writer won. Do not retry blindly — reload the entity, re-evaluate the business rule, and retry with a bounded attempt count and exponential backoff (using `Task.Delay` with jitter, never `Thread.Sleep`).
- `SemaphoreSlim` and `lock` protect in-process state only. If you deploy two instances behind a load balancer, the lock does nothing. Use database locks or a distributed lock for invariants that span instances.
- EF Core's `SaveChangesAsync` sends all tracked changes in a single round-trip but does not wrap them in an explicit transaction by default. For check-then-act patterns, use `BeginTransactionAsync` to ensure the read and write are in the same transaction.
- `FromSqlInterpolated` with `UPDLOCK` (SQL Server) or `FOR UPDATE` (PostgreSQL) acquires a row lock for the transaction's duration. Keep the transaction short — do not call external services while holding the lock.
- Idempotency keys must be enforced by a unique index, not by an application-level `Dictionary` or `HashSet`. The index survives process restarts and protects across instances.

## Tests to write
- Double-debit: submit two concurrent debits with different idempotency keys for an account with insufficient balance for both — assert only one succeeds.
- Idempotency: submit the same idempotency key twice — assert the balance is debited once.
- Concurrency conflict: load an entity, modify it in a separate context, then save the original — assert `DbUpdateConcurrencyException`.
- Concurrent inserts: insert 50 rows with the same unique key in parallel — assert exactly one succeeds and 49 get a constraint violation.
