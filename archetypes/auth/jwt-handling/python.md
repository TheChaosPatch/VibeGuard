---
schema_version: 1
archetype: auth/jwt-handling
language: python
principles_file: _principles.md
libraries:
  preferred: python-jose[cryptography]
  acceptable:
    - PyJWT
  avoid:
    - name: itsdangerous
      reason: General-purpose signing, not a JOSE implementation — lacks claim validation.
    - name: Manual base64 decode
      reason: Reads claims without verifying the signature.
minimum_versions:
  python: "3.10"
---

# JWT Handling — Python

## Library choice
`PyJWT` (with the `cryptography` extra) is the most widely used and actively maintained JOSE library for Python. It validates signatures, claims, and algorithm allowlists in a single call. `python-jose[cryptography]` is an alternative with full JOSE suite support (JWE, JWK sets). For most access-token issuance and validation, `PyJWT` is sufficient and has a simpler API.

## Reference implementation
```python
from datetime import datetime, timedelta, UTC
from pathlib import Path

import jwt
from cryptography.hazmat.primitives.serialization import load_pem_private_key

_ALGORITHM = "RS256"
_ISSUER = "https://auth.example.com"
_AUDIENCE = "https://api.example.com"
_PRIVATE_KEY = load_pem_private_key(Path("/run/secrets/jwt_private.pem").read_bytes(), password=None)
_PUBLIC_KEY = _PRIVATE_KEY.public_key()

def issue_token(subject: str, extra_claims: dict) -> str:
    now = datetime.now(UTC)
    payload = {
        "sub": subject,
        "iss": _ISSUER,
        "aud": _AUDIENCE,
        "iat": now,
        "exp": now + timedelta(minutes=15),
        **extra_claims,
    }
    return jwt.encode(payload, _PRIVATE_KEY, algorithm=_ALGORITHM)

def validate_token(token: str) -> dict:
    """Raises jwt.PyJWTError on any validation failure."""
    return jwt.decode(
        token,
        _PUBLIC_KEY,
        algorithms=[_ALGORITHM],   # explicit allowlist
        issuer=_ISSUER,
        audience=_AUDIENCE,
        options={"require": ["exp", "iat", "sub", "iss", "aud"]},
    )
```

## Language-specific gotchas
- Always pass `algorithms` as an explicit list to `jwt.decode`. Omitting it raises a `DecodeError` in modern PyJWT — this is intentional protection against the `alg: none` attack.
- `options={"require": [...]}` enforces that listed claims are present and non-null. Without it, a token missing `exp` will not be rejected on expiry validation.
- `PyJWT` raises `jwt.ExpiredSignatureError`, `jwt.InvalidAudienceError`, etc. — all subclasses of `jwt.PyJWTError`. Catch the base class in middleware and return 401; do not expose the subclass message to the caller.
- Load the private key from a file or secret store at module import time, not per request. RSA key parsing is expensive.
- When rotating keys, serve the JWKS endpoint and use `kid` in the token header. `PyJWT` supports `jwk` objects directly via `jwt.PyJWKClient`.

## Tests to write
- `validate_token(issue_token("u1", {}))` succeeds and returns `sub == "u1"`.
- Token with a past `exp` → `ExpiredSignatureError`.
- Token signed with a different private key → `InvalidSignatureError`.
- Token with `alg` header changed to `none` → `DecodeError`.
- Token missing `exp` claim → `MissingRequiredClaimError`.
