---
schema_version: 1
archetype: persistence/database-connections
title: Database Connection Security
summary: Securing database connections through TLS enforcement, connection pool limits, and automated credential rotation.
applies_to: [csharp, python, go, javascript, typescript, java, kotlin, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - connection
  - pool
  - tls
  - ssl
  - credential
  - rotation
  - timeout
related_archetypes:
  - persistence/secrets-handling
references:
  owasp_asvs: V9.2
  owasp_cheatsheet: Database Security Cheat Sheet
  cwe: "400"
---

# Database Connection Security — Principles

## When this applies
Every service that opens a persistent or pooled connection to a relational database, document store, cache, or message broker. Connection security encompasses three distinct threat vectors: credential theft (who can authenticate), transport interception (who can read the wire), and resource exhaustion (can a pool or thread-count spike take down the service). All three require deliberate configuration; drivers default to convenience, not security.

## Architectural placement
Connection lifetime is managed by a single, shared pool per application process. Connection strings and credentials are never embedded in source code or checked into version control; they are injected at runtime from a secrets manager or environment variable that the deployment platform populates. TLS configuration — certificate path, hostname verification mode, minimum protocol version — is specified at pool-construction time and is not overridable per-request. Credential rotation logic lives outside the application: the secrets manager rotates the password, writes the new value, and the pool drains old connections on next health-check. The application never knows a rotation happened.

## Principles
1. **Always require TLS; verify the server certificate.** Set `sslmode=verify-full` (PostgreSQL), `SslMode=Required` (MySQL), or the driver equivalent. Never set `TrustServerCertificate=true` in production. Certificate pinning is a stronger option for internal services where the CA is controlled.
2. **Set pool minimum, maximum, and connection lifetime.** A pool with no upper bound hands an attacker a resource exhaustion vector through query flooding. A pool with no connection lifetime accumulates stale, potentially broken connections. Typical production values: min=2, max=20–50 per instance, lifetime=10–30 minutes.
3. **Set command and connection timeouts.** Every query must have a finite timeout. An unbounded query ties up a pool connection and, at scale, exhausts the pool. Timeout values are business-logic decisions (5 s for OLTP, longer for batch) but they must be explicit.
4. **Keep credentials out of connection strings in source.** Inject via environment variable (`DATABASE_URL`), a secrets manager (AWS Secrets Manager, HashiCorp Vault, Azure Key Vault), or a platform-managed identity (IAM auth). Never commit usernames or passwords.
5. **Use least-privilege database accounts.** The application account has SELECT, INSERT, UPDATE, DELETE on its own schema — nothing else. A separate migration account has DDL rights and is only active during deployments. The application never needs `DROP` or `GRANT`.
6. **Validate connections on borrow.** Enable the pool's test-on-borrow option (or equivalent health check) to detect and evict dead connections before they surface as errors in user-facing requests.
7. **Close connections explicitly; do not rely on GC.** Use `using`/`with`/`defer` patterns so connections are returned to the pool immediately after use. Abandoned connections leak pool slots and can exhaust the pool even with a max set.
8. **Support zero-downtime credential rotation.** The pool should be able to refresh credentials without a restart. Use short-lived credentials (IAM tokens, Vault leases) and configure the driver to re-authenticate on lease expiry rather than caching the initial credential forever.

## Anti-patterns
- `Server=db;Database=app;User Id=admin;Password=secret123;` hardcoded in `appsettings.json` committed to git.
- `TrustServerCertificate=true` or `ssl_verify_cert=false` in any environment other than localhost development.
- Pools with no `maxPoolSize` — a slow query storm creates thousands of connections and crashes the database.
- Opening a new connection per HTTP request and closing it synchronously inside a request handler without a pool.
- Sharing one connection across threads without serialisation (common in scripts promoted to services).
- Rotating credentials by restarting all application instances simultaneously — this causes a brief outage and is unnecessary with a properly configured pool.
- Using the database superuser (`postgres`, `root`, `sa`) as the application account.

## References
- OWASP ASVS V9.2 — Server Communications Security
- OWASP Database Security Cheat Sheet
- CWE-400 — Uncontrolled Resource Consumption
