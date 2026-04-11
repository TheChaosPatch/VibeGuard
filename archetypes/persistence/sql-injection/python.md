---
schema_version: 1
archetype: persistence/sql-injection
language: python
principles_file: _principles.md
libraries:
  preferred: SQLAlchemy
  acceptable:
    - psycopg
    - asyncpg
  avoid:
    - name: "str.format / f-strings / % for SQL"
      reason: Not a library, but the universal anti-pattern. Ban in review.
    - name: sqlite3 with string concatenation
      reason: The DB-API supports parameters — use them.
minimum_versions:
  python: "3.11"
---

# SQL Injection Defense — Python

## Library choice
`SQLAlchemy` (2.x core or ORM) is the default. The `text()` construct accepts bound parameters with a named-placeholder syntax, and the ORM's query surface compiles to parameterized SQL by construction. For low-level Postgres access, `psycopg` (v3) and `asyncpg` both expose server-side parameter binding and should be used that way — never with Python string formatting. Avoid libraries that encourage f-string assembly of SQL, and avoid the urge to reach for `str.format` because "just this once."

## Reference implementation
```python
from dataclasses import dataclass
from sqlalchemy import create_engine, text
from sqlalchemy.engine import Engine

SORTABLE_COLUMNS = frozenset({"email", "created_at", "last_login"})


@dataclass(frozen=True, slots=True)
class User:
    id: str
    email: str
    display_name: str


class UserRepository:
    def __init__(self, engine: Engine) -> None:
        self._engine = engine

    def find_by_email(self, email: str) -> User | None:
        sql = text(
            "SELECT id, email, display_name FROM users WHERE email = :email"
        )
        with self._engine.connect() as conn:
            row = conn.execute(sql, {"email": email}).one_or_none()
        return User(*row) if row else None

    def list_sorted(self, order_by: str) -> list[User]:
        if order_by not in SORTABLE_COLUMNS:
            raise ValueError(f"Unknown sort column: {order_by!r}")
        # Identifier allowlisted above; parameters cannot bind column names.
        sql = text(
            f"SELECT id, email, display_name FROM users ORDER BY {order_by}"
        )
        with self._engine.connect() as conn:
            return [User(*row) for row in conn.execute(sql)]
```

## Language-specific gotchas
- SQLAlchemy's `text()` uses `:name` placeholders, not `%s` or `?`. Mixing styles is a common source of silent bugs — stick with `:name` and pass a dict.
- The ORM's `.filter()` and `.where()` methods compile to parameterized SQL. `.filter_by(email=email)` is safe. `session.execute(text(f"... WHERE email = '{email}'"))` is injection.
- `psycopg` (v3) parameters use `%s`, **not** Python's `%` operator — that confuses everyone at least once. Write `cur.execute("SELECT ... WHERE email = %s", (email,))`, never `cur.execute("SELECT ... WHERE email = %s" % email)`.
- `asyncpg` uses PostgreSQL-native `$1`, `$2` positional placeholders. It's the fastest of the async drivers but its API is lower-level than psycopg.
- Django's ORM compiles to parameterized SQL by default. `User.objects.raw("SELECT * FROM users WHERE email = %s", [email])` is safe. `User.objects.raw(f"... WHERE email = '{email}'")` is injection.

## Tests to write
- Parameterization: calling the repository with `"bob' OR '1'='1"` returns no rows and does not raise a parse error. Parse errors prove the value was interpreted as SQL.
- Allowlist: `list_sorted("email; DROP TABLE users--")` raises `ValueError` and does not open a database connection.
- Happy path: round-trip a handful of realistic users including Unicode names and addresses with apostrophes.
- Connection lifecycle: the `with self._engine.connect()` block releases the connection back to the pool even on exception — test by injecting a raising SQL and asserting the pool count is unchanged.
