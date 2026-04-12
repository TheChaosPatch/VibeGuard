---
schema_version: 1
archetype: http/csrf
language: python
principles_file: _principles.md
libraries:
  preferred: Django CSRF middleware
  acceptable:
    - Flask-WTF (CSRFProtect)
    - Starlette CSRFMiddleware (starlette-csrf)
  avoid:
    - name: Custom token generation
      reason: Framework CSRF systems handle token rotation, cookie binding, BREACH mitigation, and HMAC validation; reimplementing them introduces subtle bugs.
minimum_versions:
  python: "3.10"
---

# Cross-Site Request Forgery Defense — Python

## Library choice
Django's `CsrfViewMiddleware` is the gold standard — it is enabled by default, handles token generation, cookie/header validation, BREACH mitigation (token masking), and rotation. For Flask, use `Flask-WTF`'s `CSRFProtect` extension, which provides similar coverage. For FastAPI/Starlette SPAs that use cookie auth, use `starlette-csrf` or validate a custom header manually. For APIs authenticated purely by bearer tokens, CSRF protection is unnecessary and should be skipped.

## Reference implementation
```python
# Django — settings.py
MIDDLEWARE = [
    "django.middleware.security.SecurityMiddleware",
    "django.contrib.sessions.middleware.SessionMiddleware",
    "django.middleware.csrf.CsrfViewMiddleware",  # must be before views
    # ...
]
CSRF_COOKIE_HTTPONLY = True
CSRF_COOKIE_SAMESITE = "Strict"
CSRF_COOKIE_SECURE = True
CSRF_COOKIE_NAME = "__Host-csrftoken"
CSRF_USE_SESSIONS = False  # cookie-based is fine with SameSite+Secure

# In templates — Django auto-includes with {% csrf_token %}:
# <form method="post">{% csrf_token %} ... </form>

# For SPA / AJAX with cookie auth:
# Read the csrftoken cookie, send it as X-CSRFToken header.
CSRF_HEADER_NAME = "HTTP_X_CSRFTOKEN"

# Flask — app setup with Flask-WTF
from flask import Flask
from flask_wtf.csrf import CSRFProtect

app = Flask(__name__)
app.config["SECRET_KEY"] = "load-from-env-not-hardcoded"
app.config["WTF_CSRF_TIME_LIMIT"] = 3600
app.config["WTF_CSRF_SSL_STRICT"] = True

csrf = CSRFProtect(app)

# Exempt a bearer-token API blueprint (not CSRF-vulnerable).
# csrf.exempt(api_blueprint)
```

## Language-specific gotchas
- Django's `@csrf_exempt` is the single most dangerous decorator for CSRF. Grep for it regularly — every use must be justified (webhooks from third parties, bearer-token APIs). A `@csrf_exempt` on a session-authenticated view is a CSRF vulnerability.
- `CSRF_COOKIE_HTTPONLY = True` means JavaScript cannot read the cookie to send it as a header. For SPAs, either set it to `False` (and accept the XSS-to-CSRF escalation risk) or use `CSRF_USE_SESSIONS = True` and provide the token via a template or dedicated endpoint.
- Django masks the CSRF token on each page load to mitigate BREACH compression attacks. Do not compare tokens by string equality in custom validation — use Django's `_check_token` or the middleware itself.
- Flask-WTF's `CSRFProtect` validates on all POST/PUT/PATCH/DELETE by default. The `@csrf.exempt` decorator is the escape hatch — audit it the same way you audit `@csrf_exempt` in Django.
- FastAPI with `Depends(OAuth2PasswordBearer(...))` is bearer-token auth and does not need CSRF. But if you add session/cookie auth alongside it, you must add CSRF protection for those routes.
- The `Referer` check in Django's CSRF middleware is strict for HTTPS: it requires the `Referer` header to match `CSRF_TRUSTED_ORIGINS`. Add your domain to this list or POST requests from your own pages will fail.

## Tests to write
- POST to a protected view without a CSRF token returns 403 (Django) or 400 (Flask-WTF).
- POST with a valid CSRF token returns the expected success response.
- POST with a stale or tampered token returns 403/400.
- A view decorated with `@csrf_exempt` accepts POST without a token (verify this is intentional).
- AJAX POST with the token in the `X-CSRFToken` header succeeds.
