---
schema_version: 1
archetype: logging/sensitive-data
title: Sensitive Data in Logs
summary: Keeping secrets, credentials, and personal data out of the log stream.
applies_to: [csharp, python]
status: draft
keywords:
  - log
  - logging
  - pii
  - secret
  - redaction
  - structured
  - sanitize
  - authorization
  - header
related_archetypes:
  - persistence/secrets-handling
  - io/input-validation
references:
  owasp_asvs: V7.1
  owasp_cheatsheet: Logging Cheat Sheet
  cwe: "532"
---

# Sensitive Data in Logs — Principles

## When this applies
Every log line your application emits, plus every log line your frameworks emit on your behalf — HTTP access logs, ORM query logs, middleware trace logs, crash dumps, exception reports. The threat model is not "someone tails the log file." It's "logs flow to a log aggregator, get indexed, get backed up, get queried by SREs, get mirrored to a SaaS, and live for years." Anything you write to a log line is effectively published to a large, weakly-controlled audience with a very long memory. If you wouldn't paste it into a Slack channel that includes contractors, don't put it in a log.

## Architectural placement
Logging goes through one named logger interface — `ILogger<T>`, `structlog.get_logger`, whatever your framework calls it — configured with a redaction layer that sits *between* the application code and the sinks. The redaction layer knows the shape of your sensitive types (a `SecretStr`, a `SecretOptions<T>`, a `PiiString`) and strips them before serialization. It also has an allowlist of header names (`Authorization`, `Cookie`, `X-Api-Key`, `Set-Cookie`, `Proxy-Authorization`) that are always redacted regardless of whose log line they appear on. Handlers log *structured events* with named fields — `logger.info("order created", order_id=id, customer_id=customer.id)` — not formatted strings that splice in whatever happens to be in scope. The single seam between "what the handler wants to say" and "what hits the wire" is the thing enforcing the policy.

## Principles
1. **Structured logs, named fields, no format strings that splice unknowns.** `log.Information("order created {OrderId}", id)` is safe because the template is a constant and `id` is a specific value. `log.Information($"order created {order}")` is unsafe because the object may have `ToString()` overridden to dump every property — including the ones you didn't want logged.
2. **Sensitive types are opaque.** A `SecretStr` / `SecretOptions<T>` / `PiiString` renders as `"<redacted>"` in every context: `ToString()`, `repr()`, JSON serialization, structured log destructuring. This is the single most effective primitive — once a value is wrapped, you can't accidentally unwrap it in a log.
3. **Redact by field name, not by content.** A "pattern that looks like a credit card" detector is both prone to false negatives (tokenized formats) and useless against fields whose content doesn't self-identify. Redact fields named `authorization`, `cookie`, `password`, `token`, `ssn`, `api_key`, `secret`, regardless of value. Unknown fields go through; the allowlist is a floor, not a ceiling.
4. **Authentication failures never log the credential.** "We logged the bearer token on auth failure for debugging" is how every major JWT-in-logs incident has started. Log the *shape* of the failure (issuer, expiry, signature-invalid) — never the token itself.
5. **Bodies are not log-safe by default.** HTTP request and response bodies may contain any of the above. Log them only behind a debug flag, only with redaction applied, and only for routes you've audited.
6. **Error objects don't pass through redaction automatically.** An exception message can include a failing SQL query with inlined values, a failing URL with query parameters, or a failing JSON body. Format exceptions through the same redaction layer that handles regular fields.
7. **Log volume is a privacy surface too.** Every extra field you log expands the blast radius on breach. Log what you'd need to diagnose an incident and nothing else. "Just log everything, we'll sort it out later" is how you end up with customer PII in a SaaS search index.

## Anti-patterns
- `log.Information("config loaded: {@Config}", config)` — Serilog's `@` destructures every property, including the secret ones. Use a projection: `config.ToSafeView()`.
- `logger.info(f"got request: {request.headers}")` — dumps `Authorization` and `Cookie` in plain text.
- `logger.error(exception)` where the exception's message was built by formatting the failing input — the redaction layer never sees the raw fields.
- `log.Debug("query: {Sql}", rawSqlWithInlinedValues)` — the query and its values are now in logs, and the SQL log was the *reason* you built the parameterized repository in the first place.
- Catching an exception, wrapping it with `new Exception($"failed for user {user}")`, and rethrowing. The new message now contains every property on `user`.
- Logging the full response body of an outbound call to "the payments API" — you've just written the payment-provider's session token to your logs.
- Enabling DEBUG-level logging in production as a troubleshooting shortcut and leaving it on.

## References
- OWASP ASVS V7.1 — Log Content
- OWASP Logging Cheat Sheet
- CWE-532 — Insertion of Sensitive Information into Log File
