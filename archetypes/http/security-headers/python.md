---
schema_version: 1
archetype: http/security-headers
language: python
principles_file: _principles.md
libraries:
  preferred: Django SecurityMiddleware + django-csp
  acceptable:
    - Flask-Talisman
    - Starlette middleware (custom)
    - secure (pypi)
  avoid:
    - name: Setting headers in individual views
      reason: Decentralized header management guarantees gaps when new endpoints are added.
minimum_versions:
  python: "3.10"
---

# HTTP Security Headers — Python

## Library choice
Django's `SecurityMiddleware` handles HSTS, `X-Content-Type-Options`, SSL redirect, and `Referrer-Policy` through `settings.py`. For CSP, use `django-csp`, which adds CSP headers via middleware with per-view override support and nonce generation. For Flask, `Flask-Talisman` wraps all security headers (HSTS, CSP, X-Frame-Options, etc.) in a single extension. For FastAPI/Starlette, write a simple ASGI middleware or use the `secure` package. The critical rule: headers are configured once in middleware, not scattered across view functions.

## Reference implementation
```python
# Django — settings.py
MIDDLEWARE = [
    "django.middleware.security.SecurityMiddleware",
    "csp.middleware.CSPMiddleware",  # django-csp
    # ...
]

# HSTS
SECURE_HSTS_SECONDS = 31536000
SECURE_HSTS_INCLUDE_SUBDOMAINS = True
SECURE_HSTS_PRELOAD = True

# Transport
SECURE_SSL_REDIRECT = True
SECURE_REDIRECT_EXEMPT = []  # no exemptions

# Content sniffing
SECURE_CONTENT_TYPE_NOSNIFF = True

# Referrer
SECURE_REFERRER_POLICY = "strict-origin-when-cross-origin"

# CSP via django-csp
CSP_DEFAULT_SRC = ("'none'",)
CSP_SCRIPT_SRC = ("'self'",)
CSP_STYLE_SRC = ("'self'",)
CSP_IMG_SRC = ("'self'",)
CSP_CONNECT_SRC = ("'self'",)
CSP_FONT_SRC = ("'self'",)
CSP_BASE_URI = ("'self'",)
CSP_FORM_ACTION = ("'self'",)
CSP_FRAME_ANCESTORS = ("'none'",)

# X-Frame-Options (fallback for older browsers)
X_FRAME_OPTIONS = "DENY"

# Flask — app setup with Flask-Talisman
# from flask_talisman import Talisman
# talisman = Talisman(app, content_security_policy={...},
#     strict_transport_security=True,
#     strict_transport_security_max_age=31536000)
```

## Language-specific gotchas
- Django's `SecurityMiddleware` must be first (or near-first) in `MIDDLEWARE` so it applies to all responses, including error pages and redirects.
- `django-csp` supports `CSP_INCLUDE_NONCE_IN` to automatically generate and inject nonces for `script-src` and `style-src`. Use `{{ request.csp_nonce }}` in templates to reference the nonce.
- `SECURE_SSL_REDIRECT = True` in Django causes infinite redirects if your load balancer terminates TLS and forwards plain HTTP. Set `SECURE_PROXY_SSL_HEADER = ("HTTP_X_FORWARDED_PROTO", "https")` to trust the proxy header.
- Flask-Talisman forces HTTPS by default (`force_https=True`). In development, either disable it or use a local TLS proxy — do not set `force_https=False` globally and forget to re-enable it.
- `X_FRAME_OPTIONS = "DENY"` in Django is the default since Django 3.0. Verify it has not been changed to `SAMEORIGIN` or removed.
- Gunicorn and uvicorn do not add a `Server` header by default, but nginx/Apache in front of them will. Configure the reverse proxy to suppress `Server` and `X-Powered-By`.

## Tests to write
- Every response includes `X-Content-Type-Options: nosniff`.
- Every HTTPS response includes `Strict-Transport-Security` with `max-age=31536000`.
- `Content-Security-Policy` header includes `default-src 'none'` and does not contain `'unsafe-inline'` or `'unsafe-eval'`.
- `X-Frame-Options: DENY` is present on all responses.
- HTTP requests are redirected to HTTPS (test `SECURE_SSL_REDIRECT` or Talisman's `force_https`).
