---
schema_version: 1
archetype: persistence/database-connections
language: php
principles_file: _principles.md
libraries:
  preferred: PDO
  acceptable:
    - Doctrine DBAL
  avoid:
    - name: mysqli with ssl_verify_peer disabled
      reason: Disabling certificate verification exposes the connection to MITM attacks.
minimum_versions:
  php: "8.4"
---

# Database Connection Security — PHP

## Library choice
`PDO` with the `pgsql` or `mysql` driver is the standard. `Doctrine DBAL` wraps PDO and supports centrally configured connection parameters. PHP does not have a built-in connection pool; pooling is provided by PgBouncer, ProxySQL, or php-fpm's persistent connections (`PDO::ATTR_PERSISTENT`). Credentials come from environment variables via `$_ENV` or `getenv()`, never from source code.

## Reference implementation
```php
<?php
declare(strict_types=1);

function createPdo(): PDO
{
    $host   = getenv('DB_HOST')     ?: throw new \RuntimeException('Missing DB_HOST');
    $name   = getenv('DB_NAME')     ?: throw new \RuntimeException('Missing DB_NAME');
    $user   = getenv('DB_USER')     ?: throw new \RuntimeException('Missing DB_USER');
    $pass   = getenv('DB_PASSWORD') ?: throw new \RuntimeException('Missing DB_PASSWORD');

    $dsn = "pgsql:host=$host;dbname=$name;sslmode=verify-full";

    $pdo = new PDO($dsn, $user, $pass, [
        PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_TIMEOUT            => 10,
        PDO::ATTR_PERSISTENT         => false,  // use external pooler instead
    ]);

    // Set query timeout (PostgreSQL specific).
    $pdo->exec("SET statement_timeout = '30s'");

    return $pdo;
}

function countUsers(PDO $pdo): int
{
    $stmt = $pdo->query("SELECT COUNT(*) AS n FROM users");
    return (int) $stmt->fetchColumn();
}
```

## Language-specific gotchas
- `sslmode=verify-full` in the PostgreSQL DSN validates certificate and hostname. For MySQL: `PDO::MYSQL_ATTR_SSL_VERIFY_SERVER_CERT => true` must be set explicitly; without it, `PDO::MYSQL_ATTR_SSL_CA` alone encrypts but does not verify.
- `PDO::ATTR_PERSISTENT => true` reuses connections across requests within the same php-fpm worker. This can exhaust database connection limits when the pool is not sized correctly; use an external pooler (PgBouncer) instead and keep persistent false.
- `PDO::ERRMODE_EXCEPTION` must be set — the default `ERRMODE_SILENT` suppresses errors, causing silent failures that are hard to diagnose.
- Never put credentials in the DSN string that gets logged. Log only `host`, `dbname`, and `sslmode`.
- `getenv()` returns `false` when the variable is absent, not `null`. The `?: throw` idiom handles this correctly.

## Tests to write
- `createPdo()` throws `RuntimeException` when `DB_HOST` is unset.
- DSN string contains `sslmode=verify-full` — assert in unit test by inspecting the constructed DSN without connecting.
- Integration: `countUsers($pdo)` returns a non-negative integer.
- TLS: connect with `sslmode=disable` to a TLS-required PostgreSQL server; assert a `PDOException` is thrown.
