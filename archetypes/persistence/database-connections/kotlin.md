---
schema_version: 1
archetype: persistence/database-connections
language: kotlin
principles_file: _principles.md
libraries:
  preferred: com.zaxxer:HikariCP
  acceptable:
    - org.postgresql:postgresql
    - exposed
  avoid:
    - name: com.mchange:c3p0
      reason: Outdated connection pool; use HikariCP.
minimum_versions:
  kotlin: "2.1"
  jvm: "21"
---

# Database Connection Security — Kotlin

## Library choice
Kotlin on the JVM uses the same JDBC ecosystem as Java. `HikariCP` is the pool of choice. `Exposed` (JetBrains' SQL framework) works on top of HikariCP and provides a typed DSL. Credentials come from environment variables or a secrets client — Kotlin's `System.getenv()` or Spring's `Environment` abstraction.

## Reference implementation
```kotlin
import com.zaxxer.hikari.HikariConfig
import com.zaxxer.hikari.HikariDataSource
import javax.sql.DataSource

object DataSourceFactory {
    fun create(): DataSource {
        val config = HikariConfig().apply {
            jdbcUrl = "jdbc:postgresql://${env("DB_HOST")}/${env("DB_NAME")}?sslmode=verify-full"
            username = env("DB_USER")
            password = env("DB_PASSWORD")
            minimumIdle        = 2
            maximumPoolSize    = 20
            connectionTimeout  = 10_000L   // ms
            idleTimeout        = 300_000L  // ms
            maxLifetime        = 1_800_000L
            poolName           = "app-pool"
        }
        return HikariDataSource(config)
    }

    private fun env(key: String): String =
        System.getenv(key) ?: error("Missing required env var: $key")
}

// Usage with Kotlin coroutines via Exposed.
suspend fun countUsers(ds: DataSource): Long =
    kotlinx.coroutines.Dispatchers.IO.run {
        ds.connection.use { conn ->
            conn.prepareStatement("SELECT COUNT(*) FROM users").use { ps ->
                ps.executeQuery().use { rs ->
                    if (rs.next()) rs.getLong(1) else 0L
                }
            }
        }
    }
```

## Language-specific gotchas
- Kotlin's `error()` function throws `IllegalStateException`. Using it in the `env` helper means a missing credential crashes the application at startup — earlier and more visibly than a lazy connection failure.
- `ds.connection.use { }` closes the JDBC connection proxy (returning it to the HikariCP pool) even on exception, equivalent to Java's try-with-resources.
- Exposed's `Database.connect(dataSource)` accepts a pre-configured HikariCP `DataSource`; pass it rather than a raw connection string so TLS settings are not duplicated.
- Coroutine code that performs JDBC calls must run on `Dispatchers.IO` — blocking JDBC calls on the default dispatcher starve coroutine threads.
- Ktor with the `exposed` plugin configures HikariCP via `install(Database)` — ensure the same TLS and pool settings are carried through.

## Tests to write
- `DataSourceFactory.create()` throws `IllegalStateException` when a required env var is absent.
- Integration: `countUsers` returns a non-negative long against a real database.
- JDBC URL contains `sslmode=verify-full` — assert in unit test without connecting.
- Pool max: acquire `maximumPoolSize + 1` connections with a short timeout; assert exception is thrown.
