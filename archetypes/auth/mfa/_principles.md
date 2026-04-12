---
schema_version: 1
archetype: auth/mfa
title: Multi-Factor Authentication
summary: Implementing TOTP, WebAuthn, and backup codes as a second authentication factor.
applies_to: [csharp, python, go]
status: draft
keywords:
  - mfa
  - 2fa
  - totp
  - otp
  - webauthn
  - fido2
  - backup-codes
  - authenticator
  - enrollment
  - recovery
  - rfc6238
  - second-factor
related_archetypes:
  - auth/password-hashing
  - auth/session-tokens
  - auth/api-endpoint-authentication
references:
  owasp_asvs: V2.8
  owasp_cheatsheet: Multifactor Authentication Cheat Sheet
  cwe: "308"
---

# Multi-Factor Authentication -- Principles

## When this applies
Any system where a compromised password should not be sufficient for full account access. That is nearly every system with human users. MFA adds a second factor -- something the user *has* (TOTP app, security key) or something the user *is* (biometric via WebAuthn) -- on top of something the user *knows* (password). This archetype covers the implementation of the second factor, not the password itself (see `auth/password-hashing`).

## Architectural placement
MFA is a discrete step in the authentication pipeline, not something bolted onto the login handler. After primary authentication succeeds (password verified), the pipeline checks whether the user has MFA enrolled. If yes, it issues a short-lived, narrowly-scoped "mfa-pending" session that permits exactly one operation: submitting a second-factor code. Only after the second factor is verified does the system issue a full session token (see `auth/session-tokens`). This two-phase structure prevents the common bug where a half-authenticated user can access protected resources because the handler only checks "is there a session" without checking "has MFA been completed."

## Principles
1. **TOTP is the baseline; WebAuthn is the upgrade.** TOTP (RFC 6238) works everywhere and requires no browser APIs. WebAuthn/FIDO2 is phishing-resistant and should be offered when clients support it. SMS-based OTP is a last resort due to SIM-swap and SS7 vulnerabilities -- if you must support it, treat it as the weakest tier.
2. **Accept the current window and one adjacent window, no more.** TOTP with a 30-second step and a skew tolerance of +/-1 window means codes are valid for roughly 90 seconds. Wider windows reduce security without meaningfully improving usability. Never accept codes older than two windows.
3. **Rate-limit verification attempts aggressively.** Cap at 5 failed attempts per pending session, then invalidate the session and force re-authentication from scratch. An attacker brute-forcing a 6-digit TOTP code has a 1-in-1,000,000 chance per attempt -- 5 attempts keeps that negligible. Lock the account temporarily after repeated cycles.
4. **Generate backup codes with CSPRNG, store them hashed.** Issue 8-10 single-use backup codes during enrollment. Each code should be at least 8 alphanumeric characters (roughly 48 bits of entropy). Store them hashed with a fast hash (SHA-256 is acceptable here -- they are high-entropy, not passwords). Mark each code as consumed after use.
5. **Protect the enrollment flow like a privilege escalation.** Enrolling a new TOTP secret or WebAuthn credential requires re-authentication (password entry) even if the user has an active session. The QR code / secret must be shown exactly once and transmitted only over TLS. Never send the TOTP secret in an email or store it in client-side logs.
6. **Never reveal whether MFA is enrolled in an error path.** The login response for "wrong password" and "correct password but MFA required" must be indistinguishable to an unauthenticated observer. Otherwise, an attacker learns which accounts have MFA enabled and targets the ones that don't.
7. **Recovery flow is the weakest link -- treat it accordingly.** Account recovery that bypasses MFA (email link, support override) must be at least as hard as the factor it replaces. Log every recovery event. Notify the user on every registered channel. Rate-limit recovery attempts.

## Anti-patterns
- Accepting TOTP codes across a window wider than +/-1 (60-90 seconds of validity is enough).
- Storing backup codes in plaintext in the database.
- Allowing unlimited TOTP verification attempts without lockout.
- Showing the TOTP secret (or QR code) on a "settings" page that can be revisited after enrollment.
- Enrolling a new MFA device without requiring re-authentication.
- Returning a distinct error message that tells an unauthenticated caller "this account has MFA enabled."
- Implementing MFA as a client-side gate (JavaScript check) that a modified client can skip.
- Using SMS as the only second factor without offering TOTP or WebAuthn as alternatives.

## References
- OWASP ASVS V2.8 -- One Time Verifier Requirements
- OWASP Multifactor Authentication Cheat Sheet
- CWE-308 -- Use of Single-factor Authentication
- RFC 6238 -- TOTP: Time-Based One-Time Password Algorithm
