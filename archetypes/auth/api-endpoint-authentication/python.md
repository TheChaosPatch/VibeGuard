---
schema_version: 1
archetype: auth/api-endpoint-authentication
language: python
principles_file: _principles.md
libraries:
  preferred: fastapi
  acceptable:
    - pyjwt
    - authlib
  avoid:
    - name: Manual base64 split of JWT payload
      reason: Skips signature verification entirely — the classic "it parsed so it's valid" bug.
    - name: Per-route @login_required decorator as the only defense
      reason: A forgotten decorator is an unauthenticated endpoint; use a router-level dependency instead.
minimum_versions:
  python: "3.11"
---

# API Endpoint Authentication — Python

## Library choice
`fastapi` is the preferred framework here, not because of its request/response ergonomics, but because its *dependency injection* system gives you the right structural hook: attach an authentication dependency to a `APIRouter`, and every route on that router requires authentication without per-route ceremony. Pair it with `PyJWT` for token verification (battle-tested, maintained, no surprising defaults) or `authlib` if you need OAuth/OIDC flows beyond raw JWT verification. Django REST Framework has an analogous pattern via `DEFAULT_AUTHENTICATION_CLASSES` + `DEFAULT_PERMISSION_CLASSES` — the archetype is the same shape even if the framework isn't.

## Reference implementation
```python
from fastapi import APIRouter, Depends, FastAPI, HTTPException, Request, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
import jwt  # PyJWT

_bearer = HTTPBearer(auto_error=True)


def current_user(
    creds: HTTPAuthorizationCredentials = Depends(_bearer),
) -> dict:
    try:
        claims = jwt.decode(
            creds.credentials,
            key=_signing_key(),  # resolved from SecretsProvider at startup
            algorithms=["RS256"],
            audience="guardcode-api",
            issuer="https://auth.example.com/",
            leeway=30,
        )
    except jwt.PyJWTError:
        # Uniform failure — never leak which check failed.
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "unauthorized")
    return {"sub": claims["sub"], "scopes": claims.get("scope", "").split()}


# Authenticated router: every route on it requires a verified JWT.
api = APIRouter(dependencies=[Depends(current_user)])


@api.get("/orders/{order_id}")
def get_order(order_id: str, user: dict = Depends(current_user)) -> dict:
    return {"order_id": order_id, "caller": user["sub"]}


# Public allowlist — one router, easy to audit.
public = APIRouter()


@public.get("/health")
def health() -> dict:
    return {"status": "ok"}


app = FastAPI()
app.include_router(api)
app.include_router(public)
```

## Language-specific gotchas
- `APIRouter(dependencies=[Depends(current_user)])` is the line that makes authentication the default for every route on that router. Individual routes don't need to repeat it. New contributors adding a route to `api` get authentication for free, which is exactly the point.
- `HTTPBearer(auto_error=True)` returns a 403 on a missing `Authorization` header, not a 401. If that matters to your API contract, wrap it in a custom dependency that re-raises as 401 — and make the response body identical to the "invalid token" case so the two are indistinguishable.
- `jwt.decode` with `algorithms=["RS256"]` (or whatever you actually use) is mandatory. If you pass `algorithms=None` or omit it, PyJWT historically accepted `alg: none` tokens, which is the worst bug in the history of JWT. Pin the algorithm.
- Never catch `jwt.PyJWTError` and fall through to anonymous access "for backwards compatibility." If the token can't be verified, the request fails.
- Do not log `creds.credentials`. Do not log `claims` if your logs go anywhere untrusted. A JWT in a log file is a replayable credential until expiry.
- For Django: set `DEFAULT_PERMISSION_CLASSES = ["rest_framework.permissions.IsAuthenticated"]` and mark public views with an explicit `permission_classes = [AllowAny]`. Same architecture, different syntax.

## Tests to write
- Unauthenticated request to `/orders/abc` returns 401 with body `{"detail":"unauthorized"}` — and never reaches the handler (assert via a handler that raises on entry).
- Request with a token signed by the wrong key returns 401 — generate a token with a throwaway key and confirm rejection.
- Request with an expired token returns 401 — sign a token with `exp` in the past.
- Request with wrong `aud` returns 401.
- Missing `Authorization` header and invalid token both produce the *same* response body and status — compare the raw bytes.
- `alg: none` rejection: construct a token manually with `alg: none`, send it, confirm 401. This is the specific historical footgun worth guarding against with a regression test.
- Public-router allowlist: collect every route on `public` and assert the set matches a hardcoded expected list.
