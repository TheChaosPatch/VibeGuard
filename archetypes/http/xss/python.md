---
schema_version: 1
archetype: http/xss
language: python
principles_file: _principles.md
libraries:
  preferred: Jinja2 (autoescape=True)
  acceptable:
    - Django templates (auto-escaped by default)
    - bleach or nh3 (for HTML sanitization)
    - markupsafe
  avoid:
    - name: Mako (default settings)
      reason: Does not auto-escape by default; requires explicit configuration.
    - name: string concatenation / f-strings for HTML
      reason: Bypasses all escaping; the canonical one-line XSS.
minimum_versions:
  python: "3.10"
---

# Cross-Site Scripting Defense — Python

## Library choice
Jinja2 with `autoescape=True` (or `select_autoescape()` for file-extension-based rules) is the stock answer. Django templates auto-escape by default and are equally safe. For rich-text input that must accept a subset of HTML, use `nh3` (Rust-backed, fast, maintained) or `bleach` (older but well-known) with an explicit allowlist of tags and attributes. `markupsafe.Markup` is the underlying type that Jinja2 uses to distinguish "already safe" from "needs escaping" — understand it, because misuse of `Markup()` on user input is the most common Jinja2 XSS.

## Reference implementation
```python
from __future__ import annotations
import nh3
from markupsafe import Markup, escape

# Context-specific encoding for non-template use cases.
def encode_for_html(untrusted: str) -> str:
    """Encode for HTML body context."""
    return str(escape(untrusted))

# Sanitizer for rich-text fields (WYSIWYG, markdown-rendered HTML).
ALLOWED_TAGS = {"p", "br", "strong", "em", "ul", "ol", "li", "a"}
ALLOWED_ATTRIBUTES: dict[str, set[str]] = {"a": {"href"}}

def sanitize_rich_text(untrusted_html: str) -> str:
    return nh3.clean(
        untrusted_html,
        tags=ALLOWED_TAGS,
        attributes=ALLOWED_ATTRIBUTES,
        url_schemes={"https"},
        link_rel="noopener noreferrer",
    )

# Jinja2 environment — autoescape is the load-bearing setting.
from jinja2 import Environment, PackageLoader, select_autoescape

env = Environment(
    loader=PackageLoader("myapp", "templates"),
    autoescape=select_autoescape(["html", "xml"]),
)

# In a Flask/Django view, data flows as plain text:
# return render_template("profile.html", display_name=user.display_name)
# The template {{ display_name }} auto-escapes. Never use | safe on
# user data without sanitizing first.
```

## Language-specific gotchas
- `Markup(user_input)` wraps the string and tells Jinja2 "this is already safe — do not escape." Calling it on unsanitized input is an XSS. Only call `Markup()` on output you produced or sanitized.
- The `| safe` filter in Jinja2 and `{% autoescape off %}` in Django disable escaping for that expression. Grep for both — every occurrence must justify why the input is trusted or pre-sanitized.
- `render_template_string(user_input)` is not just XSS — it is server-side template injection (SSTI). Never pass user data as the template itself; only pass it as context variables.
- JSON embedded in a `<script>` tag needs the `</` sequence escaped. Use `tojson` filter in Jinja2 (`{{ data | tojson }}`), which handles this. Do not call `json.dumps()` and inject via `| safe`.
- `bleach.clean()` with an empty `tags` list strips all HTML but can still be bypassed by unusual encodings. `nh3` is preferred for new projects due to its Rust parser and stricter defaults.
- Flask's `jsonify()` sets `Content-Type: application/json` correctly, but an endpoint returning HTML via `make_response()` must encode manually — there is no auto-escape outside the template engine.

## Tests to write
- `encode_for_html("<script>alert(1)</script>")` returns a string with `&lt;script&gt;`.
- `sanitize_rich_text("<img onerror=alert(1)>")` returns an empty string or strips the tag entirely.
- `sanitize_rich_text('<a href="javascript:alert(1)">x</a>')` strips the `href` (only `https` allowed).
- Jinja2 template rendering `{{ "<script>" }}` with autoescape on produces `&lt;script&gt;` in the output.
- `Markup(escape(user_input))` round-trips correctly without double-encoding.
