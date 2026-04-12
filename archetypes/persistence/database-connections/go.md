---
schema_version: 1
archetype: persistence/database-connections
language: go
principles_file: _principles.md
libraries:
  preferred: lib/pq or pgx/v5
  acceptable:
    - database/sql with pgx/v5
    - go-sql-driver/mysql
  avoid:
    - name: database/sql with sslmode=disable
      reason: Disables TLS on the connection; traffic is transmitted in plaintext.
minimum_versions:
  go: "1.23"
---

# Database Connection Security — Go

## Library choice
`pgx/v5` is the idiomatic high-performance PostgreSQL driver. Used directly (`pgxpool.New`) it provides a built-in connection pool with TLS configuration via `pgxpool.Config`. Alternatively, use `pgx` as a `database/sql` driver (`pgxdriver`) for compatibility with third-party libraries. For MySQL, `go-sql-driver/mysql` with `tls=custom` and a registered `tls.Config`. Credentials come from environment variables or a secrets client such as `aws/aws-sdk-go-v2/service/secretsmanager`.

## Reference implementation
```go
package persistence

import (
    "context"
    "crypto/tls"
    "fmt"
    "os"
    "time"

    "github.com/jackc/pgx/v5/pgxpool"
)

func NewPool(ctx context.Context) (*pgxpool.Pool, error) {
    cfg, err := pgxpool.ParseConfig(fmt.Sprintf(
        "host=%s dbname=%s user=%s password=%s sslmode=verify-full",
        os.Getenv("DB_HOST"),
        os.Getenv("DB_NAME"),
        os.Getenv("DB_USER"),
        os.Getenv("DB_PASSWORD"),
    ))
    if err != nil {
        return nil, fmt.Errorf("parse pool config: %w", err)
    }

    cfg.MinConns = 2
    cfg.MaxConns = 20
    cfg.MaxConnLifetime = 30 * time.Minute
    cfg.MaxConnIdleTime = 5 * time.Minute
    cfg.ConnConfig.ConnectTimeout = 10 * time.Second
    // TLS config is derived from sslmode=verify-full; override if pinning needed.
    cfg.ConnConfig.TLSConfig = &tls.Config{
        MinVersion: tls.VersionTLS12,
    }

    return pgxpool.NewWithConfig(ctx, cfg)
}

// Usage: always acquire via pool.Acquire or use pool.QueryRow directly.
func CountUsers(ctx context.Context, pool *pgxpool.Pool) (int64, error) {
    ctx, cancel := context.WithTimeout(ctx, 5*time.Second)
    defer cancel()
    var n int64
    err := pool.QueryRow(ctx, "SELECT COUNT(*) FROM users").Scan(&n)
    return n, err
}
```

## Language-specific gotchas
- `sslmode=verify-full` in the DSN performs hostname verification. `sslmode=require` encrypts but does not verify the certificate. `sslmode=disable` sends plaintext — never acceptable in production.
- `pgxpool.Pool` is goroutine-safe and designed to be shared. Create one pool at startup; do not create pools per-request.
- Always pass a `context.Context` with a deadline to every query. A `context.Background()` without timeout allows queries to block goroutines indefinitely, exhausting the pool.
- `pgxpool.Acquire` returns a `*pgxpool.Conn` that must be released with `conn.Release()`. Use `defer conn.Release()` immediately after a successful acquire.
- For MySQL with `go-sql-driver/mysql`: register a `tls.Config` with `mysql.RegisterTLSConfig("custom", tlsConfig)` and use `tls=custom` in the DSN rather than `tls=true` (which skips verification).

## Tests to write
- Integration: `CountUsers` returns a non-negative int64 and no error against a real database.
- TLS: assert DSN contains `sslmode=verify-full`; connect to a server with an invalid certificate and assert connection is refused.
- Pool max: acquire `MaxConns + 1` connections with a short context timeout; assert the last acquire returns `context.DeadlineExceeded`.
- Credentials: assert no literal password appears in source files — check via a grep in CI.
