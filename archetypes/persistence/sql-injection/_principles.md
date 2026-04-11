---
schema_version: 1
archetype: persistence/sql-injection
title: SQL Injection Defense
summary: Keeping user input out of SQL statements by construction.
applies_to: [csharp, python, rust]
status: draft
keywords:
  - sql
  - injection
  - query
  - parameterized
  - prepared
  - statement
  - database
  - orm
related_archetypes:
  - io/input-validation
  - persistence/secrets-handling
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: SQL Injection Prevention Cheat Sheet
  cwe: "89"
---

# SQL Injection Defense — Principles

## When this applies
Any code that sends a SQL statement to a database and any part of that statement is influenced, directly or transitively, by a value the user can control. "User" here means the network: HTTP request bodies, query strings, headers, uploaded file contents, message-bus payloads, even values you read back from another service. If the value did not come from a literal you typed into the source file, it is untrusted.

## Architectural placement
Query construction lives behind a narrow data-access layer — a repository, a mapper, or the ORM's query builder — and the layer above it never sees raw SQL. Handlers and services call methods like `users.FindByEmail(email)`, not `db.Query("SELECT * FROM users WHERE email='" + email + "'")`. The repository is the only place in the codebase that writes SQL text, and it exclusively uses parameterized statements. This is the single most effective structural choice against SQL injection: if raw SQL cannot be assembled outside one file, injection cannot happen outside one file.

## Principles
1. **Parameterized queries, always.** Parameter placeholders (`?`, `@name`, `$1`) bind at the protocol level. The database receives the statement and the values on separate wires and never interprets the values as SQL. This is the defense. Everything else is compensation for not doing this.
2. **Never build SQL with string concatenation, interpolation, or formatting.** Not `"WHERE id = " + id`, not `f"WHERE id = {id}"`, not `String.Format`. If you are tempted to, the answer is a parameter, not a cleverer quoting function.
3. **Identifiers cannot be parameterized — allowlist them.** Table and column names cannot be bound. If you need a dynamic column for `ORDER BY`, compare the user-supplied value against a static set of allowed column names and fail closed on miss. Never pass the raw string through.
4. **Use the ORM's typed API, not its raw-SQL escape hatch.** If you are calling `.raw()`, `.execute_sql()`, `FromSqlRaw`, or `db.Raw`, you have stepped outside the abstraction and taken responsibility for parameterization yourself. Do it only with a documented reason and reviewer sign-off.
5. **Fail closed on query errors.** A query that cannot be built or executed returns an error to the caller; it does not fall through to a default row, a broader query, or a retry with less input validation.
6. **Do not rely on escaping.** Per-engine escaping functions exist (`mysqli_real_escape_string`, `pg_escape_string`) but they are easier to misuse than parameters, and charset-dependent bugs have repeatedly bypassed them. Parameterized statements are simpler and safer.

## Anti-patterns
- Building a query with `f""`, `$""`, string concatenation, or `.Format` — even "just this once for a table name."
- Using a manual quoting function to sanitize user input before embedding it in SQL.
- Passing a dynamic `ORDER BY` column through unchecked because "parameters don't work for ORDER BY anyway."
- Catching a SQL exception and retrying with a relaxed query (the classic "if it didn't parse, try stripping quotes").
- Logging the failing SQL statement with the user-supplied values inline — it leaks both the injection attempt and, often, the schema.
- Disabling the ORM's query-parameterization warnings to "clean up the build output."

## References
- OWASP ASVS V5.3 — Output Encoding and Injection Prevention
- OWASP SQL Injection Prevention Cheat Sheet
- CWE-89 — Improper Neutralization of Special Elements used in an SQL Command
