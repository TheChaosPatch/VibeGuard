---
schema_version: 1
archetype: http/cors
language: go
principles_file: _principles.md
libraries:
  preferred: github.com/rs/cors
  acceptable:
    - Custom middleware on net/http
  avoid:
    - name: Setting Access-Control headers in individual handlers
      reason: Misses preflight, error responses, and new endpoints; guarantees inconsistency.
minimum_versions:
  go: "1.22"
---

# Cross-Origin Resource Sharing Configuration — Go

## Library choice
`github.com/rs/cors` is the de facto standard CORS middleware for Go. It handles preflight caching, origin allowlisting, credential support, and `Vary: Origin` correctly out of the box. It wraps any `http.Handler`. A custom middleware is acceptable for simple cases (one or two allowed origins, no credentials), but `rs/cors` handles the edge cases — multiple origins, preflight caching, `Vary` headers, and null origins — that custom implementations typically miss. Never set CORS headers in individual handlers; always use middleware that wraps the top-level mux.

## Reference implementation
```go
package main

import (
	"net/http"

	"github.com/rs/cors"
)

func main() {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /api/data", dataHandler)
	mux.HandleFunc("POST /api/data", createHandler)

	c := cors.New(cors.Options{
		AllowedOrigins: []string{
			"https://app.example.com",
			"https://staging.example.com",
		},
		AllowedMethods: []string{"GET", "POST", "PUT", "DELETE"},
		AllowedHeaders: []string{"Content-Type", "Authorization", "X-Request-ID"},
		AllowCredentials: true,
		MaxAge:           3600, // seconds — preflight cache
	})

	// Wrap the mux — every response gets CORS headers if the origin matches.
	// Preflight OPTIONS is handled entirely by the cors middleware.
	handler := c.Handler(mux)
	http.ListenAndServeTLS(":443", "cert.pem", "key.pem", handler)
}

func dataHandler(w http.ResponseWriter, r *http.Request) {
	// CORS headers are already set by the middleware.
	w.Header().Set("Content-Type", "application/json")
	w.Write([]byte(`{"status":"ok"}`))
}

func createHandler(w http.ResponseWriter, r *http.Request) {
	w.WriteHeader(http.StatusCreated)
}
```

## Language-specific gotchas
- `rs/cors` with `AllowedOrigins: []string{"*"}` and `AllowCredentials: true` logs a warning and disables credentials. This is the library protecting you — do not work around it by reflecting the origin manually with a custom `AllowOriginFunc`.
- `AllowOriginFunc` in `rs/cors` takes precedence over `AllowedOrigins`. If you use it for subdomain matching, anchor the check: parse the origin as a URL and compare host parts explicitly. A function like `strings.HasSuffix(origin, ".example.com")` matches `evil-example.com`.
- `rs/cors` adds `Vary: Origin` automatically when the response varies by origin. Verify this in tests — a CDN that strips `Vary` can cache one origin's response and serve it to another.
- Go's `net/http` default mux handles `OPTIONS` requests by returning 405 Method Not Allowed. The CORS middleware must intercept `OPTIONS` before the mux. `rs/cors` does this correctly when wrapping the mux; a custom middleware must explicitly check for `OPTIONS` and handle it.
- If your application has both public (no-auth) and private (cookie-auth) endpoints, use two separate CORS configurations: a permissive one (`AllowedOrigins: ["*"]`, no credentials) for public endpoints and a restrictive one (explicit origins, credentials) for private endpoints. Mount them on separate muxes or use `rs/cors`'s `AllowOriginRequestFunc` with path-based logic.
- Do not set CORS headers in both the Go application and a reverse proxy (nginx, Caddy). Duplicate headers cause browsers to reject the response. Choose one layer to own CORS.

## Tests to write
- Request with `Origin: https://app.example.com` receives `Access-Control-Allow-Origin: https://app.example.com`.
- Request with `Origin: https://evil.com` does not receive an `Access-Control-Allow-Origin` header.
- Preflight OPTIONS with an allowed origin returns 200 with `Access-Control-Allow-Methods` and `Access-Control-Max-Age`.
- `Vary: Origin` is present on all responses to cross-origin requests.
- Request without an `Origin` header does not include `Access-Control-Allow-Origin` in the response (no unnecessary headers).
