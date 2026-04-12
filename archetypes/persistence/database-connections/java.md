---
schema_version: 1
archetype: persistence/database-connections
language: java
principles_file: _principles.md
libraries:
  preferred: com.zaxxer:HikariCP
  acceptable:
    - org.postgresql:postgresql
    - com.mysql:mysql-connector-j
  avoid:
    - name: com.mchange:c3p0
      reason: Outdated connection pool; lacks modern TLS configuration and is rarely maintained. Use HikariCP.
minimum_versions:
  java: "21"
---

# Database Connection Security — Java

## Library choice
`HikariCP` is the standard high-performance connection pool for Java. Configure it with a `HikariConfig` object; the underlying JDBC URL or `DataSource` carries the TLS settings. For PostgreSQL, use `org.postgresql:postgresql` with `sslmode=verify-full`. For MySQL, use `com.mysql:mysql-connector-j` with `sslMode=VERIFY_IDENTITY`. Credentials are injected from environment variables or a secrets manager — never from properties files committed to version control.

## Reference implementation
```java
import com.zaxxer.hikari.HikariConfig;
import com.zaxxer.hikari.HikariDataSource;
import javax.sql.DataSource;

public final class DataSourceFactory {
    private DataSourceFactory() {}

    public static DataSource create() {
        HikariConfig cfg = new HikariConfig();
        cfg.setJdbcUrl(String.format(
            "jdbc:postgresql://%s/%s?sslmode=verify-full",
            System.getenv("DB_HOST"),
            System.getenv("DB_NAME")
        ));
        cfg.setUsername(System.getenv("DB_USER"));
        cfg.setPassword(System.getenv("DB_PASSWORD"));
        cfg.setMinimumIdle(2);
        cfg.setMaximumPoolSize(20);
        cfg.setConnectionTimeout(10_000);   // ms
        cfg.setIdleTimeout(300_000);        // ms
        cfg.setMaxLifetime(1_800_000);      // ms — 30 min
        cfg.setConnectionTestQuery("SELECT 1");
        cfg.setPoolName("app-pool");
        return new HikariDataSource(cfg);
    }
}
```

## Language-specific gotchas
- `sslmode=verify-full` in the PostgreSQL JDBC URL validates both the certificate chain and the hostname. `sslmode=require` encrypts but does not verify. `sslmode=disable` sends plaintext.
- HikariCP's `maxLifetime` should be shorter than the database server's `wait_timeout` / `tcp_keepalives_idle`. Connections recycled before the server closes them avoid silent errors.
- `setConnectionTestQuery("SELECT 1")` validates the connection on borrow only when the pool cannot use JDBC 4 connection validation. For PostgreSQL JDBC 42+, this is not needed — the driver's `isValid()` is used automatically.
- Spring Boot auto-configures HikariCP from `spring.datasource.*` properties. Ensure `spring.datasource.hikari.ssl` properties map to the correct JDBC URL parameters — Spring does not add `?sslmode=` automatically.
- Use `try-with-resources` for `Connection`, `PreparedStatement`, and `ResultSet`; HikariCP returns the connection to the pool when the proxy is closed.

## Tests to write
- Integration: `DataSource.getConnection()` succeeds; run `SELECT 1` and assert result is 1.
- TLS: assert the JDBC URL contains `sslmode=verify-full`; connect to a server with an untrusted certificate and assert `SQLException` is thrown.
- Pool max: open `maximumPoolSize + 1` connections simultaneously; assert the extra `getConnection()` throws within `connectionTimeout` ms.
- Credentials: assert `System.getenv("DB_PASSWORD")` is used and the literal string is not present in any source file (CI grep check).
