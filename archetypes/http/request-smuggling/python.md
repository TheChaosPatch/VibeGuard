---
schema_version: 1
archetype: http/request-smuggling
language: python
principles_file: _principles.md
libraries:
  preferred: uvicorn + starlette / gunicorn + uvicorn workers (ASGI)
  acceptable:
    - gunicorn (WSGI, HTTP/1.1 only)
  avoid:
    - name: Werkzeug development server
      reason: Single-threaded, not for production; does not implement keep-alive hardening.
    - name: http.server (stdlib)
      reason: Toy server; no keep-alive hardening, not for production.
minimum_versions:
  python: "3.10"
  uvicorn: "0.30"
---

# HTTP Request Smuggling — Python

## Library choice
`uvicorn` with `httptools` HTTP/1.1 parser rejects ambiguous `Content-Length` / `Transfer-Encoding` combinations by default. For full HTTP/2 end-to-end, run `uvicorn` with `--http h2c` or behind a TLS-terminating proxy (nginx, Caddy, AWS ALB) configured to speak HTTP/2 to the backend. ASGI middleware provides the application-layer validation layer. Gunicorn is acceptable as a process manager with uvicorn workers.

## Reference implementation
```python
# middleware/smuggling_guard.py
from __future__ import annotations
from starlette.types import ASGIApp, Receive, Scope, Send
from starlette.responses import PlainTextResponse


class RequestSmugglingGuard:
    """Reject HTTP/1.1 requests with both Content-Length and Transfer-Encoding."""

    def __init__(self, app: ASGIApp) -> None:
        self.app = app

    async def __call__(
        self, scope: Scope, receive: Receive, send: Send
    ) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        headers = {
            k.lower(): v
            for k, v in scope.get("headers", [])
        }
        has_cl = b"content-length" in headers
        has_te = b"transfer-encoding" in headers

        if has_cl and has_te:
            response = PlainTextResponse(
                "Ambiguous request length headers.",
                status_code=400,
            )
            await response(scope, receive, send)
            return

        te_value = headers.get(b"transfer-encoding", b"").lower()
        if has_te and te_value not in (b"chunked", b""):
            response = PlainTextResponse(
                "Non-standard Transfer-Encoding rejected.",
                status_code=400,
            )
            await response(scope, receive, send)
            return

        await self.app(scope, receive, send)
```

```python
# app.py
from starlette.applications import Starlette
from starlette.routing import Route
from starlette.responses import PlainTextResponse
from middleware.smuggling_guard import RequestSmugglingGuard

def homepage(request):
    return PlainTextResponse("ok")

app = Starlette(routes=[Route("/", homepage)])
app = RequestSmugglingGuard(app)  # outermost middleware
```

## Language-specific gotchas
- `uvicorn` with `--limit-max-requests N` closes connections after N requests, limiting the shared-connection window that smuggling exploits.
- Django / Flask behind gunicorn: gunicorn's `--worker-connections` and `--keep-alive` settings control connection reuse. Set `--keep-alive 0` to disable HTTP keep-alive in high-risk deployments.
- `httptools` (uvicorn's HTTP parser) will raise a parse error and close the connection on `Transfer-Encoding: chunked` with an invalid chunk size — this is the correct behavior and should not be suppressed.
- The scope `headers` list in ASGI is a list of `(bytes, bytes)` tuples, not a dict — duplicate header names (two `Content-Length` lines) appear as two tuples. Build the validation against the raw list, not a dict that silently drops duplicates.
- Reverse proxy configuration: ensure nginx uses `proxy_pass` with HTTP/1.1 and `proxy_set_header Connection ""` to disable pipelining to uvicorn, or configure upstream HTTP/2 with `grpc_pass`.
- `aiohttp.web` server: `aiohttp` 3.9+ rejects conflicting CL/TE by default. Verify the version and do not downgrade.

## Tests to write
- ASGI test client: send request with both `content-length` and `transfer-encoding` headers — expect 400.
- Send request with `Transfer-Encoding: xchunked` — expect 400.
- Normal chunked request (no `Content-Length`) — expect 200.
- Normal `Content-Length` request (no `Transfer-Encoding`) — expect 200.
- Verify duplicate `Content-Length` headers in the raw ASGI scope both appear in the validation logic.
