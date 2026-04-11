---
schema_version: 1
archetype: persistence/sql-injection
language: rust
principles_file: _principles.md
libraries:
  preferred: sqlx
  acceptable:
    - tokio-postgres
    - diesel
  avoid:
    - name: "format! / write! / + for SQL"
      reason: Not a library, but the universal anti-pattern. Ban in review.
    - name: rusqlite with string concatenation
      reason: rusqlite supports named and positional parameters — use them.
minimum_versions:
  rust: "1.75"
---

# SQL Injection Defense — Rust

## Library choice
`sqlx` is the default. Its `query!` and `query_as!` macros verify SQL against a live database schema at compile time and expose parameter binding as the only way to get values into a statement — there is no API surface for stringly-built SQL. For runtime-composed queries (sort order, dynamic filters), the non-macro `sqlx::query_as::<_, T>(...)` still takes parameters via `.bind(value)` and still refuses to splice. For lower-level Postgres access, `tokio-postgres` exposes server-side parameter binding through `execute`/`query` with `&[&(dyn ToSql + Sync)]`; use it that way, never by `format!`-ing values into the SQL string. `diesel` is fine if you want its typed DSL — it compiles to parameterized SQL by construction. Avoid any library that encourages building SQL text and reaches for `format!` when a parameter would do.

## Reference implementation
```rust
use sqlx::postgres::PgPool;
use sqlx::FromRow;

#[derive(Debug, FromRow)]
pub struct User {
    pub id: uuid::Uuid,
    pub email: String,
    pub display_name: String,
}

const SORTABLE_COLUMNS: &[&str] = &["email", "created_at", "last_login"];

pub struct UserRepository {
    pool: PgPool,
}

impl UserRepository {
    pub fn new(pool: PgPool) -> Self {
        Self { pool }
    }

    pub async fn find_by_email(&self, email: &str) -> sqlx::Result<Option<User>> {
        sqlx::query_as::<_, User>(
            "SELECT id, email, display_name FROM users WHERE email = $1",
        )
        .bind(email)
        .fetch_optional(&self.pool)
        .await
    }

    pub async fn list_sorted(&self, order_by: &str) -> sqlx::Result<Vec<User>> {
        if !SORTABLE_COLUMNS.contains(&order_by) {
            return Err(sqlx::Error::ColumnNotFound(order_by.to_owned()));
        }
        // Identifier allowlisted above; parameters cannot bind column names.
        let sql = format!(
            "SELECT id, email, display_name FROM users ORDER BY {order_by}"
        );
        sqlx::query_as::<_, User>(&sql).fetch_all(&self.pool).await
    }
}
```

## Language-specific gotchas
- `sqlx::query!` and `sqlx::query_as!` (the compile-time-checked macros) require `DATABASE_URL` at build time and reject any SQL that the server cannot parse. That is a feature, not a hassle — it catches injection-shaped mistakes before the binary ships.
- Positional placeholders are `$1`, `$2`, ... on Postgres and `?` on MySQL/SQLite. `sqlx` follows the driver; do not try to make them uniform with a formatter.
- `format!` is the rust reach-for-it tool and the one that turns safe code into injection. The rule: `format!` is fine for building the literal *shape* of a query from a pre-validated allowlist (as in `list_sorted` above). It is never fine for splicing a user value into SQL.
- `tokio-postgres` parameters go into a `&[&(dyn ToSql + Sync)]` slice. If you find yourself wanting to call `.execute(format!(...), &[])`, stop — the value you are formatting in is the one that should be in the parameter slice.
- `rusqlite` supports both positional (`?1`) and named (`:email`) parameters. `conn.execute("... WHERE email = ?1", params![email])` is safe. `conn.execute(&format!("... WHERE email = '{email}'"), [])` is injection — and clippy will not catch it.
- `diesel`'s typed DSL (`users::table.filter(users::email.eq(email))`) compiles to parameterized SQL. Its `sql_query` escape hatch is raw and needs `bind::<Text, _>(email)` to stay safe — reviewer sign-off required.
- Returning `sqlx::Error::ColumnNotFound` from `list_sorted` is a pragmatic placeholder for "unknown sort column"; in a real codebase you'd define a domain-specific error enum with `thiserror` so the allowlist failure has a name of its own.

## Tests to write
- Parameterization: calling the repository with `"bob' OR '1'='1"` returns `Ok(None)` and does not return a parse error. A `sqlx::Error::Database` with a syntax error is the signature of string concatenation — fail the test loudly on it.
- Allowlist: `list_sorted("email; DROP TABLE users--")` returns the allowlist error and never opens a connection. Prove it with a connection-count probe around the call.
- Happy path: round-trip realistic users including Unicode display names and addresses with apostrophes. Assert that the read value is byte-equal to the written value.
- Connection lifecycle: async drops on error still return the connection to the pool. `sqlx` handles this correctly — the test exists to catch a future regression when someone refactors to manual transactions.
