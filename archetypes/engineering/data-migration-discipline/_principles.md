---
schema_version: 1
archetype: engineering/data-migration-discipline
title: Data Migration Discipline
summary: Schema changes are forward-only, migrations are code, backfills are idempotent and resumable, and deploy is decoupled from schema change.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-14"
keywords:
  - migration
  - schema-change
  - backfill
  - forward-only
  - idempotent
  - database-migration
  - ddl
  - zero-downtime
  - expand-contract
  - blue-green-migration
  - schema-evolution
  - data-integrity
related_archetypes:
  - engineering/api-evolution
  - engineering/deployment-discipline
  - engineering/continuous-integration
  - persistence/database-connections
  - persistence/sql-injection
references:
  book: "Refactoring Databases — Ambler & Sadalage"
  article: "Martin Fowler — Evolutionary Database Design"
  article: "Expand-Contract pattern (a.k.a. Parallel Change)"
---

# Data Migration Discipline -- Principles

## When this applies
Every time a schema change ships to a running system -- adding a table, dropping a column, renaming a field, changing a type, introducing a constraint, building an index, backfilling historical data. Also before a migration is written, to check that the shape of the change is safe for the deploy process. Data migrations are uniquely hard because they are not reversible by rollback alone, they often hold locks on production tables, and they interact with application code that is simultaneously running the old *and* new versions during deploy.

## Architectural placement
Migration discipline lives at the intersection of schema, code, and deploy. Unlike a code change -- which can be rolled back by redeploying a previous build -- a data migration persists. The discipline exists to ensure every schema change can be deployed without downtime, verified after landing, and continued forward even when the immediate next deploy has issues. It is the database-side counterpart to api-evolution: both are about letting a running system evolve without breaking the things that depend on it.

## Principles
1. **Forward-only migrations.** Migrations apply cleanly forward; they do not rely on being reversible. "Down migrations" that undo DDL are useful locally but dangerous in production -- rollback is by rolling forward with a fix, not by running `down`. Treating migrations as forward-only simplifies the model and matches real production practice.
2. **Migrations are code.** Every schema change lives in version control, is reviewed in a PR, runs in CI against a real database, and ships with the deploy that needs it. Manually applied DDL ("I ran that ALTER on prod") destroys reproducibility and makes the staging environment diverge silently from production.
3. **Expand-contract, never a breaking change in one step.** A column rename is three migrations: (1) add the new column and write to both, (2) backfill and switch reads, (3) stop writing to the old column and drop it. Each step is safe under rolling deploys where old and new code coexist. A single-step rename breaks every pod that has not yet redeployed.
4. **Decouple schema change from code deploy.** Ship the schema change first; verify; then ship the code that depends on it. Ship removal after the code stops using the thing. Coupling the two into one atomic release means either the DB change must land before the code (and tolerate old code) or vice versa -- both cannot require the other.
5. **Backfills are idempotent and resumable.** A backfill may be killed, restarted, crashed, or interrupted. Writing it so re-running picks up where it left off (WHERE NOT processed, batched with checkpoints) means one bad afternoon does not force a rewrite. Idempotent means running it twice produces the same result as running it once.
6. **Batch and throttle long migrations.** A single `UPDATE millionRowTable SET x = y` blocks everyone. Chunk the work, commit per chunk, sleep between chunks, watch replication lag and lock contention. Long migrations that respect production load can run during business hours; migrations that hold locks cannot.
7. **Constraints are added after data is valid.** Adding `NOT NULL` to a column with nulls fails. Adding a foreign key on rows that violate it fails. The sequence is: add the column nullable, backfill, then alter to add the constraint. Validating data before constraining it is non-negotiable for big tables.
8. **Indexes created concurrently in production.** On engines that support it (Postgres `CONCURRENTLY`, MySQL online DDL), always use the non-blocking variant in production. A blocking index creation on a large table is a scheduled outage.
9. **Test migrations on realistic data.** Running a migration against an empty staging database tells you nothing about production. Maintain a realistic-size copy (anonymized if sensitive) and run migrations there before production.
10. **Every migration has an observable outcome.** A migration that adds a column should be followed by a check: does the column exist, what percentage of rows have been backfilled, are reads returning the new shape. Without observability, "migration applied" conflates with "migration correct."

## Anti-patterns
- `DROP COLUMN` in the same deploy as the code that stops using the column -- the mid-deploy window has old code reading a column the new schema removed.
- `ALTER TABLE ADD COLUMN NOT NULL DEFAULT 'x'` on a large table without knowing whether the engine rewrites the whole table (causing a lock and an outage).
- Backfills written as one big `UPDATE` statement with no batching, killed by connection timeout after an hour, restarted from zero the next morning.
- "Reversible" migrations with `down` methods that are never actually tested, giving false confidence.
- Manually patching production schema with `psql` and "remembering to add the migration file later."
- A migration that requires a specific code version to be deployed first, without documentation or dependency wiring -- someone redeploys the wrong order and the system breaks.
- Adding a `UNIQUE` constraint without checking for existing duplicates first, producing a migration that fails late in the deploy.
- Dropping a table used by a reporting job no one remembered to check, breaking a downstream pipeline that failed silently for a week.

## References
- Scott Ambler & Pramod Sadalage -- *Refactoring Databases: Evolutionary Database Design*
- Martin Fowler -- "Evolutionary Database Design" (martinfowler.com/articles/evodb.html)
- "Expand and Contract" / "Parallel Change" pattern -- Danilo Sato
- PostgreSQL documentation -- "CREATE INDEX CONCURRENTLY" and locking semantics
- GitHub Engineering Blog -- "Online schema migrations" (gh-ost)
