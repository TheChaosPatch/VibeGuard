---
schema_version: 1
archetype: persistence/database-connections
language: javascript
principles_file: _principles.md
libraries:
  preferred: pg
  acceptable:
    - knex
    - mysql2
  avoid:
    - name: mysql (legacy)
      reason: Unmaintained; lacks native Promise support and modern TLS options. Use mysql2 instead.
minimum_versions:
  node: "22"
---

# Database Connection Security — JavaScript

## Library choice
`pg` (node-postgres) provides a connection pool via `pg.Pool`. TLS is configured through the `ssl` option with `rejectUnauthorized: true`. Credentials come from `process.env` or a secrets SDK, not from source code. `knex` wraps `pg` or `mysql2` and passes through the same TLS and pool options.

## Reference implementation
```javascript
import pg from "pg";

const { Pool } = pg;

const pool = new Pool({
    host:     process.env.DB_HOST,
    database: process.env.DB_NAME,
    user:     process.env.DB_USER,
    password: process.env.DB_PASSWORD,
    port:     5432,
    ssl: {
        rejectUnauthorized: true,   // verify the server certificate
        // ca: fs.readFileSync('/path/to/ca.crt'),  // pin to internal CA if needed
    },
    min:              2,
    max:              20,
    idleTimeoutMillis:  30_000,
    connectionTimeoutMillis: 10_000,
    statement_timeout:  30_000,     // ms — hard limit per query
});

pool.on("error", (err) => {
    // Log the error; do not crash the process on idle client errors.
    console.error("Unexpected pool error", err.message);
});

export async function countUsers() {
    const client = await pool.connect();
    try {
        const result = await client.query("SELECT COUNT(*)::int AS n FROM users");
        return result.rows[0].n;
    } finally {
        client.release();
    }
}
```

## Language-specific gotchas
- `ssl: { rejectUnauthorized: false }` disables certificate verification — equivalent to trusting any certificate, including a MITM's. Never use this outside localhost development.
- `pg.Pool` has no built-in maximum lifetime per connection. Stale connections after a database failover or certificate rotation are only detected on next use. Set `allowExitOnIdle: false` and implement a periodic `pool.query("SELECT 1")` health-check if the database does failovers.
- Always call `client.release()` in a `finally` block. An un-released client is a leaked pool slot.
- `statement_timeout` is a PostgreSQL session parameter set on connect. It applies to all queries on that connection and prevents runaway queries from holding pool slots indefinitely.
- Avoid `DATABASE_URL` with the password in it being logged — mask it in any application startup log.

## Tests to write
- Integration: `countUsers()` returns a number and does not reject.
- Pool exhaustion: acquire `max + 1` clients simultaneously; assert the last `pool.connect()` rejects with a timeout error.
- TLS: assert `rejectUnauthorized` is `true` in the pool config; connect to a server with an untrusted certificate and assert rejection.
- Error handler: emit a pool error event and assert the process does not crash.
