---
schema_version: 1
archetype: persistence/database-connections
language: typescript
principles_file: _principles.md
libraries:
  preferred: pg
  acceptable:
    - knex
    - mysql2
    - "@prisma/client"
  avoid:
    - name: mysql (legacy)
      reason: Unmaintained; use mysql2 instead.
minimum_versions:
  node: "22"
  typescript: "5.7"
---

# Database Connection Security — TypeScript

## Library choice
`pg` with `@types/pg` provides a fully typed pool API. `@prisma/client` manages its own connection pool (configured via the `DATABASE_URL` env variable with TLS parameters embedded). The principle is identical to JavaScript: TLS on, certificate verification on, credentials from environment, pool bounds set.

## Reference implementation
```typescript
import pg from "pg";
const { Pool } = pg;

function buildPoolConfig() {
    const required = ["DB_HOST", "DB_NAME", "DB_USER", "DB_PASSWORD"] as const;
    for (const key of required) {
        if (!process.env[key]) throw new Error(`Missing env var: ${key}`);
    }
    return {
        host: process.env.DB_HOST!, database: process.env.DB_NAME!,
        user: process.env.DB_USER!, password: process.env.DB_PASSWORD!,
        max: 20, ssl: { rejectUnauthorized: true },
        idleTimeoutMillis: 30_000, connectionTimeoutMillis: 10_000,
    };
}

const pool = new Pool(buildPoolConfig());

export async function countUsers(): Promise<number> {
    const client = await pool.connect();
    try {
        const result = await client.query<{ n: number }>("SELECT COUNT(*)::int AS n FROM users");
        return result.rows[0].n;
    } finally {
        client.release();
    }
}

export async function closePool(): Promise<void> {
    await pool.end();
}
```

## Language-specific gotchas
- `buildPoolConfig()` throws at startup if any required environment variable is missing — this is intentional. A missing credential at startup is better surfaced as a crash than as a mysterious connection failure at runtime.
- TypeScript's `!` non-null assertion after the guard is safe here because the guard throws first. Avoid `!` without a preceding check.
- Prisma reads `DATABASE_URL`; embed TLS params as `?sslmode=verify-full` in the URL and do not override them in `datasource` blocks in `schema.prisma`.
- `pool.end()` must be called during graceful shutdown to drain in-flight queries before the process exits. Hook it into the SIGTERM handler.
- `@types/pg` may lag behind the `pg` release. Pin the same minor version of both packages.

## Tests to write
- `buildPoolConfig()` throws `Error` when any required env var is absent.
- `countUsers()` resolves to a non-negative integer in an integration test.
- `rejectUnauthorized: true` is present in the config object returned by `buildPoolConfig()`.
- Graceful shutdown: call `closePool()` and assert no pending queries are left (use `pool.totalCount === 0`).
