---
schema_version: 1
archetype: persistence/database-connections
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.Data.SqlClient
  acceptable:
    - Npgsql
    - MySqlConnector
  avoid:
    - name: System.Data.SqlClient
      reason: Legacy, unmaintained. Use Microsoft.Data.SqlClient instead.
minimum_versions:
  dotnet: "10.0"
---

# Database Connection Security — C#

## Library choice
`Microsoft.Data.SqlClient` is the current SQL Server driver; `Npgsql` for PostgreSQL; `MySqlConnector` for MySQL/MariaDB. All three implement ADO.NET connection pooling by default. Connection strings are loaded from environment variables or `IConfiguration` backed by a secrets manager — never from string literals. Use `SqlConnectionStringBuilder` or the driver's equivalent to construct connection strings programmatically; this avoids injection in the connection string itself.

## Reference implementation
```csharp
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

public static class DatabaseConnectionFactory
{
    public static SqlConnection CreateConnection(IConfiguration config)
    {
        // Read from environment / secrets manager, never from a literal.
        var builder = new SqlConnectionStringBuilder
        {
            DataSource          = config["Db:Host"],
            InitialCatalog      = config["Db:Name"],
            UserID              = config["Db:User"],
            Password            = config["Db:Password"],
            Encrypt             = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = false,          // verify TLS cert
            ConnectTimeout      = 10,                // seconds
            CommandTimeout      = 30,
            MinPoolSize         = 2,
            MaxPoolSize         = 20,
            ConnectRetryCount   = 3,
            ConnectRetryInterval = 5,
            ApplicationName     = "MyApp",
        };
        return new SqlConnection(builder.ConnectionString);
    }
}

// Usage pattern — always await using to return the connection to the pool.
public sealed class UserRepository(IConfiguration config)
{
    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var cn = DatabaseConnectionFactory.CreateConnection(config);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM Users", cn)
        {
            CommandTimeout = 30,
        };
        return (int)await cmd.ExecuteScalarAsync(ct)!;
    }
}
```

## Language-specific gotchas
- `Encrypt=true` was the old flag name. In `Microsoft.Data.SqlClient` 4.x+, use `Encrypt=Mandatory` (`SqlConnectionEncryptOption.Mandatory`) to refuse unencrypted connections; `Encrypt=Optional` silently downgrades.
- `TrustServerCertificate=true` disables certificate verification entirely — only acceptable for localhost development against a self-signed cert. Never in staging or production.
- EF Core inherits connection settings from the underlying driver. Configuring the `DbContext` with a raw connection string skips `SqlConnectionStringBuilder` validation; use `UseSqlServer(connection)` with a pre-configured `SqlConnection` instead.
- Azure SQL supports passwordless auth via `Authentication=Active Directory Default` — prefer this over username/password rotation where available.
- `CancellationToken` must be threaded through every `OpenAsync` and `ExecuteReaderAsync` call so pool waits are cancellable.

## Tests to write
- Integration: open a connection, run `SELECT 1`, assert no exception and the return value is `1`.
- TLS: assert the connection string has `Encrypt=Mandatory` and `TrustServerCertificate=false` before connection is opened.
- Pool exhaustion: open `MaxPoolSize + 1` connections simultaneously; assert the last `OpenAsync` raises `InvalidOperationException` (pool exhausted) rather than hanging indefinitely — set `ConnectTimeout` to a small value in the test.
- Credentials: assert that no connection string component appears in source code; read them from `IConfiguration` and assert they are non-empty strings.
