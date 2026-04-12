---
schema_version: 1
archetype: auth/password-reset
title: Secure Password Reset
summary: Issuing, validating, and expiring password reset tokens without leaking account existence or allowing token reuse.
applies_to: [csharp, python, go, javascript, typescript, java, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - password
  - reset
  - token
  - forgot
  - recovery
  - email
  - account
  - enumeration
  - single-use
related_archetypes:
  - auth/password-hashing
  - auth/session-tokens
  - auth/rate-limiting
  - auth/api-endpoint-authentication
references:
  owasp_asvs: V2.5
  owasp_cheatsheet: Forgot Password Cheat Sheet
  cwe: "640"
---

# Secure Password Reset — Principles

## When this applies
Any flow that allows a user to regain account access without their current password: "forgot password" email links, admin-initiated resets, and any out-of-band credential recovery. This archetype does not cover MFA recovery codes (see `auth/mfa`) or account takeover through social engineering — it governs the cryptographic and UX mechanics of the reset token lifecycle.

## Architectural placement
Password reset is a three-step flow: (1) request — the user submits an email address; (2) delivery — the system sends a token via an out-of-band channel; (3) redemption — the user submits the token and a new password. Each step has a dedicated handler. The token is generated and consumed by a `PasswordResetService` that the handlers call; no handler touches the token store directly. Rate limiting (see `auth/rate-limiting`) wraps both the request and the redemption endpoints. The new password passes through the same `PasswordHasher` used at registration (see `auth/password-hashing`).

## Principles
1. **Generate tokens with CSPRNG, at least 128 bits of entropy.** A reset token is a temporary credential. Generate it with the platform's cryptographic random generator — not UUID v4, not a timestamp, not `Math.random()`. 32 bytes from a CSPRNG encoded as hex or base64url is the right primitive.
2. **Store only the hash of the token.** Keep the hash (SHA-256 is appropriate for high-entropy tokens) in the database, not the plaintext token. If the database is compromised, stored hashes cannot be redeemed without the plaintext tokens that were emailed.
3. **Expire tokens in 15–60 minutes.** A reset link valid for 24 hours is a 24-hour window for an attacker who accessed the user's email. 15 minutes is the minimum; 60 minutes is the practical maximum for most users.
4. **Invalidate the token immediately after one use.** Mark the token as consumed on the first redemption attempt that succeeds. Validate that the token record is in an unconsumed state before accepting it. This prevents replay attacks.
5. **Invalidate all outstanding tokens when the password changes.** A user who resets their password has, by definition, just secured their account. Any previous reset token must be voided. Also invalidate all active sessions except the one being established.
6. **Return a uniform response to the reset request regardless of whether the email exists.** "We sent a reset email if that address is registered" is the correct response — not "we did not find that email." Account existence is sensitive information. The response time must also be uniform: if the email is unknown, still wait the same amount of time before responding.
7. **Bind the token to the user's current credential state.** Include a HMAC over the user's current password hash (or a server-side state version) in the token. This ensures that if the user resets their password via another path before redeeming the link, the link becomes invalid — preventing token reuse across password changes.
8. **Deliver tokens over a verified channel only.** Email is the baseline. Do not send reset links via SMS as the primary channel (SIM-swap). Never expose the token in a URL query parameter in a GET response body that could be logged by a proxy or referrer header.

## Anti-patterns
- Generating reset tokens from `rand()`, `time()`, UUIDs, or user-derived data.
- Storing plaintext reset tokens in the database.
- Never expiring reset tokens ("valid until used").
- Accepting the token more than once (token replay).
- Returning "email not found" when the address is unknown (account enumeration).
- Sending the user's current password in the reset email ("password reminder" emails).
- Failing to invalidate existing sessions after a successful password reset.
- Embedding the new password in a GET request (appears in server logs and referrer headers).

## References
- OWASP ASVS V2.5 — Credential Recovery Requirements
- OWASP Forgot Password Cheat Sheet
- CWE-640 — Weak Password Recovery Mechanism for Forgotten Password
