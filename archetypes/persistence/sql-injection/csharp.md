---
schema_version: 1
archetype: persistence/sql-injection
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.EntityFrameworkCore
  acceptable:
    - Dapper
    - Microsoft.Data.SqlClient
  avoid:
    - name: System.Data.SqlClient
      reason: Legacy, unmaintained. Use Microsoft.Data.SqlClient instead.
    - name: string.Format / $"" / + for SQL
      reason: Not a library, but the universal anti-pattern. Ban in review.
minimum_versions:
  dotnet: "10.0"
---

# SQL Injection Defense — C#

## Library choice
`Microsoft.EntityFrameworkCore` is the default. Its LINQ surface compiles to parameterized SQL for free — every `.Where(u => u.Email == email)` becomes a bound parameter, with no way for user input to become part of the statement. For hot paths where LINQ is too heavy, `Dapper` is acceptable: it sends parameter objects over the wire and never splices values into the SQL text. `Microsoft.Data.SqlClient` directly is fine if you need fine-grained control, but you take responsibility for using `SqlParameter` everywhere.

## Reference implementation
```csharp
using Microsoft.Data.SqlClient;

public sealed class UserRepository(string connectionString)
{
    // Allowlist used for dynamic ORDER BY. Parameters cannot bind identifiers.
    private static readonly HashSet<string> SortableColumns =
        new(StringComparer.Ordinal) { "Email", "CreatedAt", "LastLogin" };

    public async Task<User?> FindByEmailAsync(string email, CancellationToken ct)
    {
        const string sql = "SELECT Id, Email, DisplayName FROM Users WHERE Email = @email";
        await using var cn = new SqlConnection(connectionString);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@email", SqlDbType.NVarChar, 320).Value = email;
        await cn.OpenAsync(ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new User(reader.GetGuid(0), reader.GetString(1), reader.GetString(2));
    }

    public async Task<IReadOnlyList<User>> ListSortedAsync(string orderBy, CancellationToken ct)
    {
        if (!SortableColumns.Contains(orderBy))
            throw new ArgumentException("Unknown sort column", nameof(orderBy));
        var sql = $"SELECT Id, Email, DisplayName FROM Users ORDER BY [{orderBy}]";
        // ^ safe because orderBy is a member of the static allowlist above.
        await using var cn = new SqlConnection(connectionString);
        await using var cmd = new SqlCommand(sql, cn);
        await cn.OpenAsync(ct);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var users = new List<User>();
        while (await reader.ReadAsync(ct))
            users.Add(new User(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return users;
    }
}

public readonly record struct User(Guid Id, string Email, string DisplayName);
```

## Language-specific gotchas
- Set the parameter's `SqlDbType` and size explicitly — the ADO.NET driver infers type from the CLR value otherwise, and width mismatches can cause implicit conversions that wreck index usage.
- `FromSqlRaw` and `FromSqlInterpolated` in EF Core are not the same. `FromSqlRaw("... WHERE id = " + id)` is injection. `FromSqlInterpolated($"... WHERE id = {id}")` is parameterized — EF Core intercepts the `FormattableString` and binds each hole.
- Async is the default for new code: always pass the `CancellationToken` through, and always `await using` the connection and command.
- Dapper's `QueryAsync("... WHERE email = @e", new { e = email })` is parameterized. Dapper's `QueryAsync("... WHERE email = '" + email + "'")` is injection. The distinction is entirely in how you call it.

## Tests to write
- Parameterization: assert that calling the repository with a value containing a single quote returns either a match or no match — never throws a SQL parse error. The parse error is the signature of string concatenation.
- Allowlist: `ListSortedAsync("Email; DROP TABLE Users--")` throws `ArgumentException` and does not touch the database.
- Happy path: exercise each query shape with realistic values and confirm the returned records round-trip.
- Schema drift: add a column test that fails loudly if the DTO and table diverge — broken mappings are where people reach for raw SQL shortcuts.
