---
schema_version: 1
archetype: persistence/orm-security
title: ORM Security
summary: Preventing mass assignment, query injection, and data leaks through ORM misuse.
applies_to: [csharp, python, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - orm
  - mass-assignment
  - over-posting
  - n+1
  - lazy-loading
  - entity-framework
  - sqlalchemy
  - gorm
  - serialization
  - data-leak
  - query
  - projection
related_archetypes:
  - persistence/sql-injection
  - io/input-validation
  - logging/sensitive-data
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: Mass Assignment Cheat Sheet
  cwe: "915"
---

# ORM Security — Principles

## When this applies
Any code that uses an ORM to map HTTP input to database entities, serialize entities to API responses, or build queries from user-controlled parameters. The ORM is not a security boundary — it is a convenience layer that hides the database wire protocol behind an object graph. Every time you let the framework decide which columns to write, which relationships to load, or which properties to serialize, you are delegating a security decision to default behavior that was designed for developer ergonomics, not defense.

## Architectural placement
Entities are internal types that never cross the API boundary. Every inbound request is deserialized into a command/DTO with an explicit allowlist of settable fields. Every outbound response is projected from the entity into a response DTO that contains only the fields the caller is authorized to see. The ORM operates in a repository or data-access layer behind this projection wall. Handlers never return entities directly, and model binders never write directly to entity properties.

## Principles
1. **Allowlist bindable fields, never denylist.** Define exactly which properties a request DTO exposes. If the entity has `IsAdmin`, `Balance`, or `Role` and the DTO does not, mass assignment cannot set them. Denylists rot — every new column is exposed until someone remembers to exclude it.
2. **Entities are not DTOs.** An entity is the persistence shape; a DTO is the API shape. They may look similar on day one but diverge immediately. Returning entities from controllers leaks navigation properties, soft-delete flags, internal IDs, and audit columns. Map explicitly.
3. **Raw SQL through the ORM still requires parameterization.** `FromSqlRaw`, `session.execute(text(...))`, and `db.Raw` drop you out of the safe query-builder surface. Every value must be bound as a parameter, exactly as if you were writing ADO.NET or psycopg calls. The ORM gives you no protection here.
4. **Eager-load deliberately; never rely on lazy loading in request paths.** Lazy loading trades an explicit query plan for implicit N+1 queries triggered by serialization or template rendering. In a list endpoint returning 100 items, lazy loading a relationship means 101 queries — a self-inflicted denial-of-service vector that also leaks data from relationships the caller should not see.
5. **Paginate and cap every list query.** An unbounded `SELECT *` through the ORM returns every row the table has. Always apply `.Take(limit)` / `LIMIT` with a server-enforced maximum, and require a cursor or offset. This is not a performance optimization — it is a resource-exhaustion defense.
6. **Disable lazy-loading proxies in serialization contexts.** When an entity graph reaches a JSON serializer, lazy proxies fire queries for every navigation property the serializer touches, including circular references. Disable proxies, use projection DTOs, or configure the serializer to ignore navigation properties.
7. **Audit column writes — never trust the client for CreatedBy, UpdatedAt, or Version.** Server-side interceptors or database defaults must own audit columns. If the client can POST a `CreatedAt` value and the ORM binds it, the audit trail is compromised.

## Anti-patterns
- Binding an HTTP request body directly to an EF Core entity: `[FromBody] User user` in a controller action.
- Returning an entity from a controller: `return Ok(user)` serializes every public property, including password hashes, internal flags, and navigation properties.
- Using `Include()` / `joinedload()` / `Preload()` without considering what the included relationship exposes to the caller.
- Letting the ORM auto-migrate in production — schema changes should be reviewed, not generated live.
- Calling `.ToList()` or `list()` before applying pagination — the entire table is loaded into memory.
- Writing `db.Raw("UPDATE users SET role = '" + role + "' WHERE id = " + id)` because "the ORM query builder felt complicated."
- Trusting `[JsonIgnore]` on entity properties as the security boundary — one missed attribute and the field leaks. Use separate DTO types.
- Relying on client-supplied `$expand` or `$select` (OData) without an allowlist — the client chooses which relationships to load.

## References
- OWASP ASVS V5.3 — Output Encoding and Injection Prevention
- OWASP Mass Assignment Cheat Sheet
- CWE-915 — Improperly Controlled Modification of Dynamically-Determined Object Attributes
