---
schema_version: 1
archetype: http/content-security-policy
language: python
principles_file: _principles.md
libraries:
  preferred: django-csp (Django) / flask-talisman (Flask)
  acceptable:
    - Manual middleware / WSGI middleware with secrets.token_urlsafe
  avoid:
    - name: Meta tag injection
      reason: frame-ancestors and report-uri are ignored in meta tags; cannot be the sole CSP mechanism.
minimum_versions:
  python: "3.10"
---

# Content Security Policy — Python

## Library choice
For Django, `django-csp` (maintained by Mozilla) integrates as middleware and supports per-view overrides and nonce injection via `{% csp_nonce %}`. For Flask, `flask-talisman` sets CSP and all other security headers in one call. For other WSGI/ASGI frameworks, implement a middleware that generates `secrets.token_urlsafe(16)` per request, stores it in the request context, and writes the `Content-Security-Policy` header before yielding.

## Reference implementation
```python
# Django — settings.py
MIDDLEWARE = [
    "csp.middleware.CSPMiddleware",
    # ... other middleware
]

CSP_DEFAULT_SRC = ("'none'",)
CSP_SCRIPT_SRC = ("'self'", "'strict-dynamic'")
CSP_STYLE_SRC = ("'self'",)
CSP_IMG_SRC = ("'self'", "data:")
CSP_FONT_SRC = ("'self'",)
CSP_CONNECT_SRC = ("'self'",)
CSP_FORM_ACTION = ("'self'",)
CSP_FRAME_ANCESTORS = ("'none'",)
CSP_BASE_URI = ("'self'",)
CSP_UPGRADE_INSECURE_REQUESTS = True
CSP_INCLUDE_NONCE_IN = ["script-src", "style-src"]
# Report violations to an endpoint before enforcing
CSP_REPORT_ONLY = False  # True during rollout
CSP_REPORT_URI = "/csp-report/"
```

```python
# Django template — access nonce via template tag
# {% load csp %}
# <script nonce="{% csp_nonce %}">...</script>

# Flask — app factory
from flask_talisman import Talisman

def create_app():
    app = Flask(__name__)
    Talisman(
        app,
        content_security_policy={
            "default-src": "'none'",
            "script-src": ["'self'", "'strict-dynamic'"],
            "style-src": "'self'",
            "img-src": ["'self'", "data:"],
            "connect-src": "'self'",
            "form-action": "'self'",
            "frame-ancestors": "'none'",
            "base-uri": "'self'",
        },
        content_security_policy_nonce_in=["script-src"],
    )
    return app
```

## Language-specific gotchas
- `django-csp` generates the nonce via `os.urandom(16)` encoded as base64 — it is request-scoped and safe. Verify you are on version 3.8+ which supports `strict-dynamic`.
- `{{ csp_nonce }}` in Django templates outputs the raw nonce value; the library injects the `nonce-{value}` form into the header automatically.
- `CSP_REPORT_ONLY = True` switches the header to `Content-Security-Policy-Report-Only` globally. Use a separate settings file or environment flag for the rollout phase.
- FastAPI / Starlette: use a custom `BaseHTTPMiddleware` that calls `secrets.token_urlsafe(16)`, stores in `request.state.csp_nonce`, and appends the header in `dispatch()` after `call_next()`.
- Django's `{% csp_nonce %}` tag fails silently if `CSPMiddleware` is not installed — write a test that confirms the nonce appears in both the header and the rendered HTML.
- `flask-talisman` also sets HSTS, X-Content-Type-Options, and X-Frame-Options — review all defaults before enabling in production.

## Tests to write
- Django test client: response includes `Content-Security-Policy` header with `nonce-` value matching `{% csp_nonce %}` in rendered body.
- Two requests produce different nonces.
- `frame-ancestors 'none'` present in every HTML response.
- CSP report endpoint returns 204 and logs the violation body.
- Settings with `CSP_REPORT_ONLY = True` produces `Content-Security-Policy-Report-Only`, not the enforcing header.
