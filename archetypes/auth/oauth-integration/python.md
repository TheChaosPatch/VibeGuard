---
schema_version: 1
archetype: auth/oauth-integration
language: python
principles_file: _principles.md
libraries:
  preferred: authlib
  acceptable:
    - requests-oauthlib
    - python-jose
  avoid:
    - name: Manual HTTP requests to authorization/token endpoints
      reason: PKCE, state, nonce, and token validation are too easy to get wrong by hand.
    - name: Storing tokens in Flask's client-side session cookie
      reason: Signed but not encrypted. Tokens are readable by anyone with the cookie.
minimum_versions:
  python: "3.10"
---

# OAuth 2.0 / OIDC Client Integration -- Python

## Library choice
`authlib` is the preferred library for OAuth 2.0 and OpenID Connect client integration in Python. It supports Authorization Code with PKCE, state parameter, ID token validation with JWKS, and refresh token handling out of the box. It integrates with Flask, Django, and Starlette/FastAPI. `requests-oauthlib` is acceptable but requires more manual configuration for PKCE and OIDC. For JWT validation only (not the full flow), `python-jose` or `PyJWT` are fine -- but never use them to build the redirect/callback/exchange cycle yourself.

## Reference implementation
```python
import secrets
from authlib.integrations.flask_client import OAuth
from flask import Flask, redirect, session, url_for, abort

app = Flask(__name__)
app.secret_key = secrets.token_bytes(32)

oauth = OAuth(app)
oauth.register(
    name="idp",
    client_id="your-client-id",
    client_secret="your-client-secret",
    server_metadata_url="https://idp.example.com/.well-known/openid-configuration",
    client_kwargs={
        "scope": "openid email",
        "code_challenge_method": "S256",
    },
)

@app.get("/login")
def login():
    nonce = secrets.token_urlsafe(32)
    session["nonce"] = nonce
    redirect_uri = url_for("callback", _external=True)
    return oauth.idp.authorize_redirect(redirect_uri, nonce=nonce)

@app.get("/callback")
def callback():
    token = oauth.idp.authorize_access_token()
    nonce = session.pop("nonce", None)
    if nonce is None:
        abort(403)
    id_token = oauth.idp.parse_id_token(token, nonce=nonce)
    # id_token is validated: signature, iss, aud, exp, nonce checked.
    session["user"] = {"sub": id_token["sub"], "email": id_token.get("email")}
    session.permanent = True
    return redirect("/")

@app.get("/logout")
def logout():
    session.clear()
    return redirect("/")
```

## Language-specific gotchas
- `code_challenge_method: "S256"` in `client_kwargs` enables PKCE. `authlib` generates the `code_verifier` and `code_challenge` automatically when this is set. Confirm it appears in the authorization URL during testing.
- `authlib` manages the `state` parameter internally -- it generates, stores, and validates it across the redirect. Do not override this with a static value. If you use `requests-oauthlib`, you must generate and validate `state` yourself.
- `parse_id_token(token, nonce=nonce)` validates the ID token signature (via JWKS), issuer, audience, expiry, and nonce in one call. Never decode the ID token with `jwt.decode` without full validation.
- `session.permanent = True` activates Flask's `PERMANENT_SESSION_LIFETIME` (default 31 days -- override it). Without it, the session cookie is a browser-session cookie that may persist indefinitely in some browsers.
- Store refresh tokens server-side (database, encrypted), never in the Flask session cookie. Flask's default session is a signed cookie readable by the client. Use `flask-session` with a server-side backend if you need to store tokens in the session.
- The `redirect_uri` passed to `authorize_redirect` must exactly match what is registered with the provider. Generate it with `url_for(..., _external=True)`, never from user input.

## Tests to write
- PKCE presence: intercept the authorization redirect URL and confirm `code_challenge` and `code_challenge_method=S256` parameters are present.
- State parameter: intercept the redirect, confirm `state` is present. Replay the callback with a modified `state`, confirm 403.
- Nonce validation: complete the flow with a mismatched nonce, confirm rejection.
- Redirect URI: confirm the authorization URL contains the exact registered callback path.
- ID token validation: supply a callback with a forged ID token (wrong `iss`, wrong `aud`, expired), confirm each is rejected.
- Scope minimality: intercept the redirect and confirm only `openid email` in the `scope` parameter.
- Logout clears session: after logout, confirm `/callback` cannot be replayed to restore the session.
