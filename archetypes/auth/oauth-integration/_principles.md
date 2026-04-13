---
schema_version: 1
archetype: auth/oauth-integration
title: OAuth 2.0 / OIDC Client Integration
summary: Securely integrating with OAuth 2.0 and OpenID Connect as a relying party.
applies_to: [all]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - oauth
  - oauth2
  - oidc
  - openid-connect
  - pkce
  - authorization-code
  - redirect-uri
  - state-parameter
  - refresh-token
  - id-token
  - access-token
  - csrf
related_archetypes:
  - auth/session-tokens
  - auth/api-endpoint-authentication
  - persistence/secrets-handling
references:
  owasp_asvs: V3.1
  owasp_cheatsheet: OAuth Cheat Sheet
  cwe: "346"
---

# OAuth 2.0 / OIDC Client Integration -- Principles

## When this applies
Any time your application delegates authentication to an external identity provider (Google, Entra ID, Okta, GitHub, a corporate SSO) using OAuth 2.0 Authorization Code flow or OpenID Connect. This archetype covers the *client* (relying party) side -- how you redirect, receive callbacks, validate tokens, and store credentials. It does not cover building your own authorization server.

## Architectural placement
OAuth integration lives in a dedicated authentication module that owns the redirect, callback, token exchange, and token refresh lifecycle. Route handlers never construct authorization URLs, parse callback parameters, or call the token endpoint directly. The module exposes two operations to the rest of the app: "start login" (returns a redirect URL) and "handle callback" (exchanges the code, validates the ID token, and returns an authenticated user). Tokens obtained from the provider are stored server-side (encrypted at rest), never exposed to the browser. The session layer (see `auth/session-tokens`) issues your application's own session after the OAuth flow completes -- the provider's tokens are internal state, not the user's session credential.

## Principles
1. **Always use PKCE, even for confidential clients.** PKCE (RFC 7636) with S256 challenge method prevents authorization code interception regardless of client type. "PKCE is only for public clients" is outdated guidance -- the OAuth 2.1 draft mandates it for all clients. Generate a fresh `code_verifier` per flow with at least 43 characters from a CSPRNG.
2. **Validate the `state` parameter to prevent CSRF.** Generate a cryptographically random `state` value, bind it to the user's pre-login session (or a signed cookie), and reject any callback where the returned `state` does not match. This is your CSRF protection for the OAuth redirect -- without it, an attacker can complete the flow with their own authorization code and bind your user's session to the attacker's account.
3. **Match redirect URIs exactly -- no wildcards, no open redirects.** Register the exact callback URL with the provider. In your callback handler, verify that the request arrived at the registered URI. Never accept a `redirect_uri` from a query parameter or allow partial matching. Open redirect on the callback path is a token theft vector.
4. **Validate the ID token fully: signature, issuer, audience, expiry, nonce.** Use a vetted JWT library configured with the provider's JWKS endpoint. Verify `iss` matches the provider, `aud` contains your client ID, `exp` is in the future, and `nonce` (if used) matches the value you sent. Do not decode without verifying. Do not skip `aud` validation.
5. **Never store access tokens in localStorage or sessionStorage.** Browser-accessible storage is readable by any XSS payload. Store tokens server-side (database, encrypted cookie, or in-memory session store). If a SPA must hold a token, use the Backend-for-Frontend (BFF) pattern: the SPA talks to your backend, your backend holds the tokens and proxies API calls.
6. **Implement refresh token rotation.** When you use a refresh token to obtain a new access token, expect the provider to issue a new refresh token and invalidate the old one. Store only the latest refresh token. If a refresh request fails with "token reuse detected," treat it as a compromise: revoke the session and force re-authentication.
7. **Scope requests to the minimum needed.** Request only the scopes your application actually uses. Over-scoping creates unnecessary blast radius if tokens are leaked. Review scopes on every provider integration update.

## Anti-patterns
- Omitting PKCE because "we're a server-side app with a client secret."
- Using a static or predictable `state` parameter (or omitting it entirely).
- Registering wildcard redirect URIs (`https://*.example.com/callback`).
- Decoding the ID token payload with base64 without verifying the signature.
- Storing provider access tokens or refresh tokens in the browser (localStorage, sessionStorage, non-HttpOnly cookies).
- Ignoring refresh token rotation -- reusing the same refresh token indefinitely.
- Requesting `openid profile email phone address` when the app only needs `openid email`.
- Treating the access token as proof of identity instead of validating the ID token.

## References
- OWASP ASVS V3.1 -- Fundamental Session Management Security
- OWASP OAuth Cheat Sheet
- CWE-346 -- Origin Validation Error
- RFC 7636 -- Proof Key for Code Exchange (PKCE)
- OAuth 2.1 Draft (consolidates OAuth 2.0 security best practices)
