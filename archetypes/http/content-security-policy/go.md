---
schema_version: 1
archetype: http/content-security-policy
language: go
principles_file: _principles.md
libraries:
  preferred: unrolled/secure
  acceptable:
    - Manual middleware with crypto/rand
  avoid:
    - name: justinas/nosurf CSP helpers
      reason: nosurf is a CSRF library; it has no CSP nonce support.
minimum_versions:
  go: "1.22"
---

# Content Security Policy — Go

## Library choice
`github.com/unrolled/secure` is a production-grade middleware library that sets all security headers including CSP. For nonce generation, pair it with a thin custom middleware that calls `crypto/rand.Read` for a per-request nonce and stores it in the request context. `unrolled/secure` accepts a `ContentSecurityPolicy` string; build that string with the nonce interpolated from context. For full-featured CSP with structured builders, write the header manually — the policy is a string and Go's standard library provides everything needed.

## Reference implementation
```go
package middleware

import (
    "context"
    "crypto/rand"
    "encoding/base64"
    "fmt"
    "net/http"
)

type contextKey struct{}
var nonceKey = contextKey{}

func NonceMiddleware(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        b := make([]byte, 16)
        if _, err := rand.Read(b); err != nil {
            http.Error(w, "internal error", http.StatusInternalServerError)
            return
        }
        nonce := base64.StdEncoding.EncodeToString(b)
        next.ServeHTTP(w, r.WithContext(context.WithValue(r.Context(), nonceKey, nonce)))
    })
}

func NonceFromContext(ctx context.Context) string {
    if v, ok := ctx.Value(nonceKey).(string); ok { return v }
    return ""
}

func CSPMiddleware(next http.Handler) http.Handler {
    return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
        nonce := NonceFromContext(r.Context())
        policy := fmt.Sprintf(
            "default-src 'none'; script-src 'nonce-%s' 'strict-dynamic'; "+
                "style-src 'nonce-%s' 'self'; img-src 'self' data:; connect-src 'self'; "+
                "font-src 'self'; form-action 'self'; frame-ancestors 'none'; "+
                "base-uri 'self'; upgrade-insecure-requests", nonce, nonce)
        w.Header().Set("Content-Security-Policy", policy)
        next.ServeHTTP(w, r)
    })
}
```

## Language-specific gotchas
- `crypto/rand.Read` never returns an error on Linux (getrandom syscall); on Windows it can in theory. Check the error anyway — if it fails, abort the request rather than serve a response without a nonce.
- `html/template` auto-escapes template variables, which means a nonce value `abc123` rendered as `nonce="{{.Nonce}}"` is safe. Do not use `template.HTML` to bypass escaping for the nonce attribute.
- `unrolled/secure` sets the CSP as a static string. Since the nonce changes per request, you cannot use `secure.New()` with a static `ContentSecurityPolicy` string containing a nonce — build the header in a wrapping middleware that runs after `NonceMiddleware`.
- HTTP/2 `Push` (server push) is removed in Go 1.20+; no special handling needed. HTTP/2 is enabled by default in `net/http` when TLS is configured.
- Context key collisions: use an unexported struct type as the key (as shown above), not a string — string keys from different packages can collide.
- Template rendering: pass the nonce into the template data struct and emit `<script nonce="{{ .Nonce }}">`. Never emit `nonce="{{ .Nonce | safeAttr }}"` — Nonce is plain text, no special marking needed.

## Tests to write
- `NonceMiddleware` sets a value in context that `NonceFromContext` retrieves.
- Two calls produce different nonce values (probabilistic; collision probability is negligible at 128 bits).
- `CSPMiddleware` sets the `Content-Security-Policy` header containing the nonce from context.
- Integration test: HTTP GET on a page endpoint returns a header where the `nonce-` value matches the `nonce="..."` attribute in the rendered `<script>` tag.
- `frame-ancestors 'none'` is present in every response header.
