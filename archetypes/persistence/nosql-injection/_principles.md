---
schema_version: 1
archetype: persistence/nosql-injection
title: NoSQL Injection Defense
summary: Preventing query operator abuse and data exfiltration in MongoDB, Redis, and document stores through structural query construction.
applies_to: [csharp, python, go, javascript, typescript, java, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - nosql
  - mongodb
  - redis
  - injection
  - query
  - operator
related_archetypes:
  - persistence/sql-injection
  - io/input-validation
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: Injection Prevention Cheat Sheet
  cwe: "943"
---

# NoSQL Injection Defense — Principles

## When this applies
Any code that constructs a query against a document store, key-value store, or graph database where any portion of the query — filter keys, operator values, pipeline stages, Lua scripts, or key patterns — is influenced by user-controlled input. NoSQL injections differ from SQL injection: the payload is often a JSON operator (`$where`, `$gt`, `$ne`) or a glob pattern (`*`) that bypasses the intent of the query rather than breaking out of a string delimiter. MongoDB's server-side JavaScript (`$where`) enables full code execution. Redis KEYS pattern injection can enumerate or delete arbitrary keys. The risk is real even when the data layer is "not a SQL database."

## Architectural placement
All query construction lives behind a typed repository layer. Callers pass typed DTOs — not raw maps, not deserialised request bodies. The repository is the only place that knows operator names, collection names, and pipeline shapes. Input that arrives from outside the process is validated against a strict schema before it reaches the data layer. Schema validation rejects unknown fields and unexpected types, which eliminates the class of attacks where a user sends `{"password": {"$ne": ""}}` in place of a plain string.

## Principles
1. **Accept typed values, not raw query fragments.** Repository methods receive `string email`, `int age`, `Guid id` — never `Dictionary<string, object> filter` built from a request body. The caller cannot inject an operator because the method signature does not accept one.
2. **Validate type and shape before the query is built.** If the API layer deserialises a JSON body, bind to a strongly-typed DTO and reject the request if the field contains anything other than a scalar. A `password` field that deserialises to an object is an injection attempt.
3. **Never pass `$where` or server-side JavaScript expressions.** MongoDB's `$where` evaluates arbitrary JavaScript in the database process. It is an execution primitive, not a query operator. Disable it at the driver or connection level if available; never enable it in application code.
4. **Allowlist Redis key patterns.** If you build a Redis key from user input (e.g., `session:{userId}`), validate the user-supplied segment against a pattern that contains only expected characters. A user who controls `*` or `..` can turn a targeted key lookup into a keyspace scan or path traversal.
5. **Use the driver's query-builder API, not raw command strings.** Drivers expose typed filter builders (`Builders<T>.Filter.Eq`, `FilterDefinitionBuilder`) that produce safe BSON. Raw command documents assembled from user maps bypass this safety.
6. **Disable features you do not use.** MongoDB: turn off `$where` and `mapReduce` unless required. Redis: use ACL rules to restrict the commands a connection can issue. Least-privilege access at the driver level reduces the blast radius of any injection.
7. **Sanitize Lua scripts passed to Redis EVAL.** If dynamic values must appear in a Lua script, pass them as `ARGV` parameters rather than embedding them in the script string. `ARGV[1]` in Lua is never interpreted as a Lua expression; a concatenated string is.

## Anti-patterns
- Deserialising a JSON request body directly into a MongoDB filter document without field-level type checking.
- Accepting an `orderBy` parameter and building `{ [userField]: 1 }` without an allowlist of valid field names.
- Using `$where: "this.role == '" + userRole + "'"` to filter documents.
- Building Redis keys with unvalidated user input: `KEYS user:` + `*` is not a lookup, it is a full keyspace scan.
- Catching a driver exception from a malformed query and retrying with the input relaxed.
- Logging the full query document including user-supplied values — both leaks schema and aids attackers probing query structure.
- Using `eval` or `db.runCommand({ eval: script })` with any user-supplied content.

## References
- OWASP ASVS V5.3 — Output Encoding and Injection Prevention
- OWASP Injection Prevention Cheat Sheet
- CWE-943 — Improper Neutralization of Special Elements in Data Query Logic
