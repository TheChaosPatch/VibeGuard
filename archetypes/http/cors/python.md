---
schema_version: 1
archetype: http/cors
language: python
principles_file: _principles.md
libraries:
  preferred: django-cors-headers
  acceptable:
    - Flask-CORS
    - Starlette CORSMiddleware (FastAPI)
  avoid:
    - name: Manual Access-Control header setting in views
      reason: Inconsistent, misses preflight and error responses, and duplicates logic across views.
minimum_versions:
  python: "3.10"
---

# Cross-Origin Resource Sharing Configuration — Python

## Library choice
For Django, use `django-cors-headers` — it adds CORS middleware with configurable origin allowlists, method restrictions, and credential support. For Flask, `Flask-CORS` provides equivalent functionality. For FastAPI/Starlette, the built-in `CORSMiddleware` is included in the framework and requires only configuration. In all three cases, configure CORS once at the middleware level with explicit origin allowlists. Never set `allow_origins=["*"]` with `allow_credentials=True`.

## Reference implementation
```python
# Django — settings.py with django-cors-headers
INSTALLED_APPS = [
    "corsheaders",
    # ...
]
MIDDLEWARE = [
    "corsheaders.middleware.CorsMiddleware",  # must be before CommonMiddleware
    "django.middleware.common.CommonMiddleware",
    # ...
]
CORS_ALLOWED_ORIGINS = [
    "https://app.example.com",
    "https://staging.example.com",
]
CORS_ALLOW_CREDENTIALS = True
CORS_ALLOW_METHODS = ["GET", "POST", "PUT", "DELETE", "OPTIONS"]
CORS_ALLOW_HEADERS = ["content-type", "authorization", "x-request-id"]
CORS_PREFLIGHT_MAX_AGE = 3600
# Do NOT set CORS_ALLOW_ALL_ORIGINS = True in production.

# FastAPI — built-in CORSMiddleware
from fastapi import FastAPI
from starlette.middleware.cors import CORSMiddleware

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["https://app.example.com", "https://staging.example.com"],
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "DELETE"],
    allow_headers=["content-type", "authorization", "x-request-id"],
    max_age=3600,
)
# FastAPI's CORSMiddleware handles OPTIONS preflight automatically.
```

## Language-specific gotchas
- `CORS_ALLOW_ALL_ORIGINS = True` in `django-cors-headers` is the equivalent of `Access-Control-Allow-Origin: *`. It is safe only if `CORS_ALLOW_CREDENTIALS = False`. The library raises no error if you set both to `True` — it silently reflects the origin, creating a security hole.
- `corsheaders.middleware.CorsMiddleware` must be placed **before** `django.middleware.common.CommonMiddleware` in `MIDDLEWARE`. If CommonMiddleware runs first, it may return early (e.g., on a slash redirect) without CORS headers, breaking cross-origin requests.
- `CORS_ALLOWED_ORIGIN_REGEXES` in `django-cors-headers` accepts regex patterns for dynamic subdomain matching. Always anchor the regex: `r"^https://[\w-]+\.example\.com$"`. An unanchored pattern like `r"example\.com"` matches `evil-example.com`.
- Flask-CORS's `resources` parameter lets you apply different CORS policies to different URL patterns. Use it to restrict CORS to API routes and exclude admin/internal routes.
- FastAPI's `CORSMiddleware` does not add `Vary: Origin` when `allow_origins` is `["*"]` because the response is the same for all origins. But if you use a specific allowlist, verify `Vary: Origin` is present — some CDN configurations require it.
- In development, adding `http://localhost:3000` to the allowlist is fine. But use environment-specific configuration (`os.environ`) so the development origin does not leak into production settings.

## Tests to write
- Request from an allowed origin includes `Access-Control-Allow-Origin` matching that origin.
- Request from an unlisted origin does not include `Access-Control-Allow-Origin`.
- Preflight OPTIONS request returns 200 with correct `Access-Control-Allow-Methods`.
- `Vary: Origin` is present on responses when the allowlist is not `["*"]`.
- `CORS_ALLOW_ALL_ORIGINS` and `CORS_ALLOW_CREDENTIALS` are not both `True` in production configuration (a settings-level test).
