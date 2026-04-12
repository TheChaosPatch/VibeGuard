---
schema_version: 1
archetype: auth/rate-limiting
title: Rate Limiting and Brute Force Defense
summary: Throttling authentication attempts and sensitive endpoints to defeat credential stuffing and brute force attacks.
applies_to: [csharp, python, go, javascript, typescript, java, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - rate
  - limit
  - throttle
  - brute
  - force
  - backoff
  - ddos
  - lockout
  - captcha
  - credential-stuffing
  - sliding-window
  - token-bucket
related_archetypes:
  - auth/api-endpoint-authentication
  - auth/password-reset
  - auth/mfa
references:
  owasp_asvs: V2.2
  owasp_cheatsheet: Blocking Brute Force Attacks
  cwe: "307"
---

# Rate Limiting and Brute Force Defense — Principles

## When this applies
Every endpoint where an attacker gains value from repeated attempts: login, password reset requests, MFA code submission, account registration, email/phone verification, and any endpoint that reveals whether a credential or identifier is valid. General API rate limiting (protecting backend capacity) overlaps but is a secondary concern — start with the authentication-path endpoints where the attacker's incentive is highest.

## Architectural placement
Rate limiting runs as middleware before the route handler, using a shared, distributed store (Redis is the standard) so that limits are enforced across all application instances. A single-node in-memory counter is bypassed by sending requests to different pods. The middleware reads the rate limit key (IP address, account identifier, or a combination), checks the counter, rejects the request with `429 Too Many Requests` if over the limit, and increments the counter on pass-through. Authentication handlers do not implement their own counting — that would duplicate logic and create gaps. The rate limit configuration (window, threshold, lockout duration) is externalized so it can be adjusted without a deploy.

## Principles
1. **Limit by account identifier, not only by IP.** IP-based limiting is bypassed by distributed botnets. Credential stuffing tools rotate IPs to stay under per-IP thresholds. Track failed attempts per account (username or email) as the primary signal. Apply IP-based limits as a secondary, defense-in-depth layer.
2. **Use a sliding window or token bucket algorithm.** Fixed windows (reset at the top of the minute) allow a burst of attempts at the window boundary. A sliding window or token bucket smooths the rate enforcement over time, closing the burst opportunity.
3. **Distinguish transient throttling from account lockout.** Throttling (exponential backoff, `Retry-After` headers) is applied early — after 5 failed attempts within a short window. Hard lockout is a last resort, applied after repeated throttled abuse, because it enables denial-of-service against legitimate users. Never lock permanently — use a time-based lockout (15 minutes to 1 hour) that auto-releases.
4. **Add jitter and exponential backoff on the response side.** Return `429` with a `Retry-After` header that increases exponentially with failure count. Jitter (random ±10% of the backoff period) prevents synchronized retry storms from clients that respect backoff.
5. **Return `429` before the handler runs expensive operations.** Password hashing is intentionally slow. A rate-limit rejection before the hash check eliminates the hashing cost for blocked requests and prevents timing-based account enumeration from hash timing differences.
6. **Apply CAPTCHA or device fingerprinting at the threshold, not as the first defense.** CAPTCHA is user-hostile and should appear only after the automated-abuse signal is strong (e.g., 3 failed attempts from an IP that has never logged in successfully). Device fingerprinting (browser fingerprint, TLS fingerprint) can strengthen the rate limit key for web clients without impacting API clients.
7. **Alert on rate limit events, do not only block.** A spike in `429` responses on the login endpoint is an ongoing attack. Emit structured log events for every rate limit trigger, aggregate them, and alert on anomalies. This turns passive blocking into an observable signal.
8. **Rate-limit the reset and verification channels too.** An attacker who cannot brute-force the login will pivot to the password reset endpoint. Apply the same per-account limits to reset requests, MFA verification attempts, and email change confirmations.

## Anti-patterns
- Counting failed attempts only in application memory on a single node (bypassed by load balancing).
- Using IP-only rate limiting (bypassed by botnets and Tor).
- Implementing a permanent account lockout (turns rate limiting into a denial-of-service vector).
- Resetting the failure counter on a successful login from the same IP (allows an attacker to reset counts by logging in with a known-good account between stuffing attempts).
- Applying rate limiting only to the `POST /login` path and not to password reset, MFA, or registration.
- Returning a distinct error for "account locked" versus "wrong password" (leaks whether an account exists and has been targeted).
- Not including a `Retry-After` header in `429` responses (legitimate clients cannot back off correctly).
- Setting the lockout threshold so high (1000 attempts) that brute-force of a 4-digit PIN is trivial within the window.

## References
- OWASP ASVS V2.2 — General Authenticator Security
- OWASP Blocking Brute Force Attacks Cheat Sheet
- CWE-307 — Improper Restriction of Excessive Authentication Attempts
