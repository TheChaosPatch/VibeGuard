---
schema_version: 1
archetype: http/ssrf
title: Server-Side Request Forgery Defense
summary: Stopping user-supplied URLs from turning your server into an attacker-controlled HTTP client.
applies_to: [csharp, python]
status: draft
keywords:
  - ssrf
  - url
  - fetch
  - http
  - proxy
  - metadata
  - webhook
  - outbound
  - request
  - allowlist
related_archetypes:
  - io/input-validation
  - persistence/secrets-handling
references:
  owasp_asvs: V12.6
  owasp_cheatsheet: Server-Side Request Forgery Prevention Cheat Sheet
  cwe: "918"
---

# Server-Side Request Forgery Defense — Principles

## When this applies
Any time your code makes an outbound network request to a URL that a caller can influence — directly or transitively. The obvious cases: an image-fetching proxy, a webhook delivery worker, a "load URL preview" feature, a PDF generator that pulls external resources, an OAuth callback that loads user profile data from an issuer URL. The non-obvious cases: a URL stored on a row that a user can update, a template that expands into a URL, an RSS feed aggregator, a test-this-webhook button in an admin panel, a Markdown renderer that follows image links. Any of those can be aimed at `http://169.254.169.254/latest/meta-data/` and used to steal cloud credentials.

## Architectural placement
Outbound HTTP goes through exactly one `SafeHttpClient` / `safe_fetch` helper that owns two things: the URL-validation policy and the HTTP client itself. The helper takes a *parsed* URL (never a raw string) and either returns a response or throws a `BlockedUrlException`. Handlers call `client.Fetch(url, context)`, never `new HttpClient().GetAsync(userString)`. The policy is a single named function that answers one question: "is this URL safe to fetch from this context?" All the loopback / link-local / cloud-metadata checks live there, and any change to the policy is a change to one file that a reviewer can audit in thirty seconds. "We need to fetch an internal URL for this one feature" is handled by introducing a *second* client with a different, equally-narrow policy — not by relaxing the main one.

## Principles
1. **Allowlist destinations by default.** If you can name the hosts you're supposed to reach (a specific webhook domain, a specific S3 bucket), check against a static allowlist. This is the most effective defense by a wide margin — blocklists of bad destinations are incomplete by construction.
2. **If you can't allowlist, blocklist aggressively.** Block loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`, `fe80::/10`), private RFC 1918 ranges (`10/8`, `172.16/12`, `192.168/16`), unique local IPv6 (`fc00::/7`), the cloud metadata address (`169.254.169.254`), and `0.0.0.0`. Also block URL schemes other than `http` and `https` — `file://`, `gopher://`, `dict://`, `ftp://` are all SSRF amplifiers.
3. **Validate after DNS resolution, not before.** A URL like `http://attacker.com/` can resolve to `127.0.0.1` via a controlled DNS record ("DNS rebinding"). Resolve the hostname yourself, validate every returned address, then pin the connection to a resolved address. Don't trust the library to re-validate on reconnect.
4. **Disable automatic redirects, or validate each hop.** A 302 to `http://169.254.169.254/` defeats any "only validate the first URL" check. Either set max-redirects to zero and handle redirects explicitly (re-validating each one), or configure the client to forbid cross-origin redirects entirely.
5. **Cap response size, time, and count.** An attacker who can make you fetch *anywhere* can also make you fetch a 10 GB file, or fetch forever. Set a response-size limit, a total-time deadline, and a per-client request budget.
6. **Strip credentials from outbound requests.** The outbound request must never carry your inbound `Authorization` header, your session cookie, or your cloud-SDK's ambient credentials. A proxy that forwards the caller's bearer token is an auth-bypass primitive.
7. **Fail closed with a uniform error.** A blocked URL returns the same shape of error as "host not found" — no differential responses that let an attacker map the internal network by timing.

## Anti-patterns
- `HttpClient.GetAsync(userSuppliedString)` — the canonical one-line SSRF.
- Parsing the URL once, validating it, then passing the *original string* to the HTTP client, which re-parses it differently. Validate what you'll fetch, fetch what you validated.
- Regex-based URL validation. URL parsing has dozens of edge cases that regexes miss — IPv6-in-IPv4 (`::ffff:127.0.0.1`), octal/hex/decimal IP forms (`0x7f000001`, `2130706433`), trailing-dot hosts, userinfo in the authority.
- "We only block `127.0.0.1`." The attacker will use `127.1`, `0`, `localhost`, `[::1]`, `0x7f.0.0.1`, or an externally-resolving name that points at a private IP.
- Trusting the `Location` header on a 301/302 response without re-validating.
- Forwarding the cloud SDK's ambient credentials (an IAM role, an IMDSv1 response, an environment variable) through to the user-controlled fetch path.
- Logging the failing URL with query parameters on block — often the parameters are exactly the exfiltration payload.

## References
- OWASP ASVS V12.6 — SSRF Protection
- OWASP Server-Side Request Forgery Prevention Cheat Sheet
- CWE-918 — Server-Side Request Forgery
