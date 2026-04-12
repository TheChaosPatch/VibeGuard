---
schema_version: 1
archetype: auth/oauth-integration
language: go
principles_file: _principles.md
libraries:
  preferred: golang.org/x/oauth2
  acceptable:
    - github.com/coreos/go-oidc/v3
  avoid:
    - name: Manual HTTP calls to authorization/token endpoints
      reason: PKCE code_verifier generation, state validation, and token parsing are too easy to get wrong.
    - name: Storing tokens in cookies without encryption
      reason: Tokens are readable by the client and any XSS payload.
minimum_versions:
  go: "1.22"
---

# OAuth 2.0 / OIDC Client Integration -- Go

## Library choice
`golang.org/x/oauth2` is the semi-official Go OAuth 2.0 client. It handles the authorization code flow, token exchange, token refresh, and credential transport. Pair it with `github.com/coreos/go-oidc/v3` for OpenID Connect: JWKS fetching, ID token verification (signature, issuer, audience, expiry, nonce), and provider discovery. Together they cover the full OIDC client lifecycle. Do not build the redirect/callback/exchange sequence from `net/http` and `encoding/json` -- the edge cases (PKCE verifier entropy, state binding, clock skew in token validation) are where hand-rolled implementations fail.

## Reference implementation
```go
package auth

import (
	"context"; "crypto/rand"; "encoding/base64"; "net/http"
	"github.com/coreos/go-oidc/v3/oidc"
	"golang.org/x/oauth2"
)

type OIDCClient struct{ cfg *oauth2.Config; verifier *oidc.IDTokenVerifier }

func NewOIDCClient(issuer, clientID, secret, callbackURL string) (*OIDCClient, error) {
	p, err := oidc.NewProvider(context.Background(), issuer)
	if err != nil { return nil, err }
	return &OIDCClient{
		cfg: &oauth2.Config{ClientID: clientID, ClientSecret: secret,
			RedirectURL: callbackURL, Endpoint: p.Endpoint(),
			Scopes: []string{oidc.ScopeOpenID, "email"}},
		verifier: p.Verifier(&oidc.Config{ClientID: clientID}),
	}, nil
}

func (c *OIDCClient) LoginRedirect(w http.ResponseWriter, r *http.Request) {
	b := make([]byte, 32); _, _ = rand.Read(b)
	state := base64.URLEncoding.EncodeToString(b)
	setSessionValue(r, "oauth_state", state)
	v := oauth2.GenerateVerifier(); setSessionValue(r, "pkce_verifier", v)
	http.Redirect(w, r, c.cfg.AuthCodeURL(state, oauth2.S256ChallengeOption(v)), http.StatusFound)
}

func (c *OIDCClient) HandleCallback(w http.ResponseWriter, r *http.Request) {
	q, state := r.URL.Query(), getSessionValue(r, "oauth_state")
	if q.Get("state") != state || state == "" {
		http.Error(w, "forbidden", http.StatusForbidden); return
	}
	tok, err := c.cfg.Exchange(r.Context(), q.Get("code"),
		oauth2.VerifierOption(getSessionValue(r, "pkce_verifier")))
	if err != nil { http.Error(w, "forbidden", http.StatusForbidden); return }
	raw, ok := tok.Extra("id_token").(string)
	if !ok { http.Error(w, "forbidden", http.StatusForbidden); return }
	idTok, err := c.verifier.Verify(r.Context(), raw)
	if err != nil { http.Error(w, "forbidden", http.StatusForbidden); return }
	var claims struct{ Sub, Email string }
	_ = idTok.Claims(&claims)
	createAppSession(w, r, claims.Sub, claims.Email, tok)
}
```

## Language-specific gotchas
- `oauth2.S256ChallengeOption(verifier)` and `oauth2.VerifierOption(verifier)` are the PKCE API in `golang.org/x/oauth2`. `oauth2.GenerateVerifier()` produces a cryptographically random verifier of the correct length. Do not generate your own verifier with `math/rand`.
- The `state` parameter must be bound to the user's pre-login session. Store it server-side (session store, encrypted cookie) -- never in a query parameter or unprotected cookie. Compare it with constant-time comparison if the value is a raw token; URL-safe base64 of 32 random bytes is long enough that timing attacks are impractical but the habit is still correct.
- `oidc.NewProvider` fetches the provider's `.well-known/openid-configuration` and JWKS keys. Cache the provider instance -- do not create one per request. The JWKS keys are cached internally and refreshed on rotation.
- `c.verifier.Verify()` checks the ID token signature, issuer, audience, and expiry. It does not check `nonce` by default -- if you send a nonce in the authorization request, extract it from the claims and compare it manually.
- Store `token.RefreshToken` encrypted at rest in your database. When refreshing, call `c.oauth2Cfg.TokenSource(ctx, token)` which handles refresh automatically. If the provider rotates refresh tokens, the new token is returned in the refreshed `*oauth2.Token` -- persist it immediately.
- `setSessionValue` / `getSessionValue` / `createAppSession` are placeholders for your session-tokens implementation. Do not store provider tokens in browser-accessible cookies.

## Tests to write
- PKCE presence: capture the authorization URL from `LoginRedirect`, confirm `code_challenge` and `code_challenge_method=S256` are in the query.
- State validation: call `HandleCallback` with a modified `state` parameter, confirm 403 response.
- Missing state: call `HandleCallback` without a `state` parameter, confirm 403.
- ID token validation: mock the token endpoint to return a forged ID token (wrong issuer, wrong audience, expired), confirm each is rejected.
- Redirect URI: confirm the authorization URL contains exactly the registered `RedirectURL`, not a user-controllable value.
- Scope minimality: confirm the authorization URL scope parameter contains only `openid email`.
- Refresh token storage: after a successful flow, confirm the refresh token is stored server-side and not present in any `Set-Cookie` header.
