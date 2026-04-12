---
schema_version: 1
archetype: http/request-smuggling
language: go
principles_file: _principles.md
libraries:
  preferred: net/http (stdlib) — Kestrel-equivalent; correct by default
  acceptable:
    - fasthttp (with explicit TE/CL validation)
  avoid:
    - name: fasthttp (default settings)
      reason: fasthttp's HTTP/1.1 parser does not reject CL+TE conflicts by default; requires explicit configuration.
minimum_versions:
  go: "1.22"
---

# HTTP Request Smuggling — Go

## Library choice
Go's `net/http` server parses HTTP/1.1 correctly: it rejects requests with both `Content-Length` and `Transfer-Encoding` per RFC 9112, and HTTP/2 support is built-in via `golang.org/x/net/http2` (included in the standard library). The primary mitigation is ensuring the proxy-to-server leg speaks HTTP/2 where possible, and adding a middleware layer to validate and reject ambiguous HTTP/1.1 headers as defense-in-depth. `fasthttp` skips some validation for performance — do not use it in a smuggling-sensitive deployment without explicit hardening.

## Reference implementation
```go
package middleware

import (
    "net/http"
    "strings"
)

// AntiSmugglingMiddleware rejects requests with conflicting length headers
// or non-standard Transfer-Encoding values (defense-in-depth; net/http also
// rejects many of these, but explicit middleware is auditable).
func AntiSmugglingMiddleware(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        hasCL := r.Header.Get("Content-Length") != ""
        hasTEHeader := len(r.Header["Transfer-Encoding"]) > 0

        if hasCL && hasTEHeader {
            http.Error(w, "ambiguous request length", http.StatusBadRequest)
            return
        }

        for _, te := range r.Header["Transfer-Encoding"] {
            normalized := strings.ToLower(strings.TrimSpace(te))
            if normalized != "chunked" && normalized != "identity" {
                http.Error(w,
                    "non-standard Transfer-Encoding rejected",
                    http.StatusBadRequest)
                return
            }
        }

        next.ServeHTTP(w, r)
    })
}
```

```go
// main.go
package main

import (
    "net/http"
    "myapp/middleware"
)

func main() {
    mux := http.NewServeMux()
    mux.HandleFunc("/health", func(w http.ResponseWriter, _ *http.Request) {
        w.WriteHeader(http.StatusOK)
    })

    srv := &http.Server{
        Addr:    ":8080",
        Handler: middleware.AntiSmugglingMiddleware(mux),
        // MaxHeaderBytes limits header-section size; reduces preamble window.
        MaxHeaderBytes: 1 << 15, // 32 KB
    }
    if err := srv.ListenAndServeTLS("cert.pem", "key.pem"); err != nil {
        panic(err)
    }
}
```

## Language-specific gotchas
- `net/http` normalizes header keys via `textproto.CanonicalMIMEHeaderKey`, so `transfer-encoding` and `Transfer-Encoding` are the same key. Obfuscated variants with unusual casing are normalized before your handler sees them — but check `r.Header["Transfer-Encoding"]` (canonical map key) to catch multi-value entries.
- `r.ContentLength` is `-1` when the body is chunked and no `Content-Length` is present. Treat `-1` as unknown, not zero.
- HTTP/2 via `h2c` (cleartext HTTP/2): use `golang.org/x/net/http2/h2c.NewHandler(mux, &http2.Server{})` for internal services that do not terminate TLS — this eliminates the TE/CL ambiguity entirely on that leg.
- `fasthttp` performance optimizations skip RFC-mandated checks. If `fasthttp` is required, add an explicit `if bytes.Contains(ctx.Request.Header.RawHeaders(), []byte("Content-Length")) && bytes.Contains(...)` check in a `fasthttp.RequestHandler` wrapper.
- Go's `http.Server` does not limit the number of pipelined requests per connection by default. Set `ReadHeaderTimeout` and `ReadTimeout` to close idle connections and reduce the shared-connection window.
- Reverse proxy: `httputil.ReverseProxy` uses `net/http` under the hood and inherits its correct parsing. Set `Transport.ForceAttemptHTTP2 = true` to prefer HTTP/2 for the upstream leg.

## Tests to write
- `httptest.NewRecorder`: send request with both `Content-Length` and `Transfer-Encoding` headers — expect 400.
- Send request with `Transfer-Encoding: xchunked` — expect 400.
- Normal `POST` with `Content-Length` only — expect forwarding to next handler.
- Normal chunked `POST` — expect forwarding.
- Verify `MaxHeaderBytes` is enforced: send a request with headers exceeding 32 KB — expect 431.
