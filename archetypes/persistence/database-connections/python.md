---
schema_version: 1
archetype: persistence/database-connections
language: python
principles_file: _principles.md
libraries:
  preferred: psycopg[binary]
  acceptable:
    - asyncpg
    - SQLAlchemy[asyncio]
    - pymysql
  avoid:
    - name: psycopg2
      reason: Superseded by psycopg 3 (psycopg[binary]); older driver lacks native async support and some TLS verification options.
minimum_versions:
  python: "3.12"
---

# Database Connection Security — Python

## Library choice
`psycopg[binary]` (psycopg 3) is the modern PostgreSQL driver; it supports async natively and configures TLS via `sslmode=verify-full`. `SQLAlchemy[asyncio]` provides a pooled engine on top of `asyncpg` or `psycopg`. For MySQL, `pymysql` or `aiomysql` with `ssl_verify_cert=True`. Credentials come from environment variables (`os.environ`) or a secrets client, never from source code literals.

## Reference implementation
```python
from __future__ import annotations
import os
import psycopg
from psycopg_pool import AsyncConnectionPool

def make_pool() -> AsyncConnectionPool:
    dsn = (
        f"host={os.environ['DB_HOST']} "
        f"dbname={os.environ['DB_NAME']} "
        f"user={os.environ['DB_USER']} "
        f"password={os.environ['DB_PASSWORD']} "
        f"sslmode=verify-full "
        f"connect_timeout=10"
    )
    return AsyncConnectionPool(
        conninfo=dsn,
        min_size=2,
        max_size=20,
        open=False,           # opened explicitly at startup
        max_idle=300,         # seconds before idle conn is closed
        max_lifetime=1800,    # seconds before conn is recycled
    )

# Usage pattern in an async handler.
async def count_users(pool: AsyncConnectionPool) -> int:
    async with pool.connection() as conn:
        async with conn.cursor() as cur:
            await cur.execute("SELECT COUNT(*) FROM users")
            row = await cur.fetchone()
            return row[0] if row else 0
```

## Language-specific gotchas
- `sslmode=verify-full` validates both the certificate chain and the hostname. `sslmode=require` encrypts but does not validate the certificate — susceptible to MITM. Never use `sslmode=disable` outside a local loopback connection.
- `AsyncConnectionPool` must be opened with `await pool.open()` at application startup and closed with `await pool.close()` at shutdown. Using it before `open()` raises an error.
- SQLAlchemy's `create_engine` has a `pool_pre_ping=True` flag that validates connections on borrow — enable it to catch stale connections silently recycled by the database.
- Never store the DSN with the password in logs. Log `f"host={host} dbname={db} user={user} sslmode=verify-full"` only.
- For credential rotation with Vault: use a short-lived lease and call `pool.check()` to evict connections that were authenticated with the old password after rotation.

## Tests to write
- Unit: build the DSN from environment variables, assert `sslmode=verify-full` is present and no literal password appears in source.
- Integration: call `count_users`, assert return is a non-negative integer.
- Pool limit: open `max_size + 1` concurrent connections, assert the extra acquire raises `PoolTimeout` within the configured timeout.
- TLS: connect to a test server with a self-signed cert and `sslmode=verify-full`; assert the connection is refused with a certificate error.
