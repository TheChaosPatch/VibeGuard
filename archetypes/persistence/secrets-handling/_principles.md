---
schema_version: 1
archetype: persistence/secrets-handling
title: Secrets Handling
summary: Storing, loading, and using API keys, credentials, and signing keys safely.
applies_to: [csharp, python]
status: draft
keywords:
  - secret
  - credential
  - api
  - key
  - token
  - vault
  - env
  - config
  - rotation
related_archetypes:
  - auth/password-hashing
  - auth/api-endpoint-authentication
references:
  owasp_asvs: V6.4
  owasp_cheatsheet: Secrets Management Cheat Sheet
  cwe: "798"
---

# Secrets Handling — Principles

## When this applies
Any time your application needs a value that would harm the system, your users, or your customers if it became public: database passwords, cloud-provider access keys, third-party API tokens, OAuth client secrets, JWT signing keys, webhook HMAC keys, encryption keys, SSH keys, TLS private keys. This is **not** about user-chosen passwords — those are hashed and stored, not loaded as configuration (see `auth/password-hashing`). This archetype is about *machine* secrets your code needs in memory to function.

## Architectural placement
Secrets live in exactly one place at rest: a secrets manager designed for the job — a cloud KMS (AWS Secrets Manager, GCP Secret Manager, Azure Key Vault), HashiCorp Vault, SOPS-encrypted files in a locked-down repo, or at minimum a local `.env` file that is never committed. The application reads them at startup through a single `SecretsProvider` abstraction, caches the resolved values in memory, and hands them to the subsystems that need them. No secret ever appears in source code, in a committed config file, in a build artifact, or in a command-line argument logged by a shell.

## Principles
1. **Never commit a secret.** Not to the main branch, not to a feature branch, not to a private repo, not to a gist. The commit is forever. If it lands, rotate the secret *and then* remove the commit — rotation first because the Git history is already indexed by a scraper somewhere.
2. **Load at startup, fail closed on miss.** If a required secret is missing, the process exits before serving its first request with a diagnostic that names the missing key but not its value. Lazy-loading a secret on first use spreads the failure window and makes outages harder to correlate.
3. **One provider, one abstraction.** Route all secret access through a single `SecretsProvider` interface with one production implementation. Tests inject an in-memory implementation. Subsystems never read environment variables directly.
4. **Never log a secret, or anything that contains one.** No "debug" prints of a full config object. No error messages that echo a rejected credential. No HTTP request loggers that dump headers with `Authorization:` intact.
5. **Rotate, and make rotation cheap.** Every secret has a known owner, a known rotation procedure, and a cadence. Secrets that "can't be rotated without downtime" are bugs — the fix is to support two concurrent values during the rotation window.
6. **Scope secrets narrowly.** A service that only reads a Stripe customer should have a Stripe key with read-only customer access, not a root key. Blast radius is the single strongest lever you have when a secret leaks.
7. **Treat `.env` as source-of-truth for *development only*.** Commit `.env.example` with dummy values so a new developer knows what keys to populate. Commit nothing else.

## Anti-patterns
- Hardcoding an API key as a string constant "until we figure out the config story."
- Committing `appsettings.Production.json` (or any name-equivalent) with real values.
- Logging the full HTTP request including `Authorization` or `Cookie` headers.
- Fetching secrets from the cloud provider on every request ("we'll add caching later").
- Passing a secret on the command line (`myapp --db-password=hunter2`) — it lands in shell history, in the process table, in audit logs.
- Using the same API key for dev, staging, and production.
- "Encrypting" a secret with a key that lives next to the ciphertext.
- Writing a secret to a temp file to pass it between processes.

## References
- OWASP ASVS V6.4 — Secret Management
- OWASP Secrets Management Cheat Sheet
- CWE-798 — Use of Hard-coded Credentials
