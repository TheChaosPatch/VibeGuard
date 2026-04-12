---
schema_version: 1
archetype: http/security-headers
language: go
principles_file: _principles.md
libraries:
  preferred: Custom middleware on net/http
  acceptable:
    - github.com/unrolled/secure
  avoid:
    - name: Setting headers in individual handlers
      reason: Decentralized header management guarantees gaps when new endpoints are added.
minimum_versions:
  go: "1.22"
---

# HTTP Security Headers — Go

## Library choice
Go's stdlib `net/http` makes writing a security-headers middleware trivial — it is a function that wraps an `http.Handler`, sets headers on the `ResponseWriter`, and calls the next handler. For a more feature-complete solution with HSTS, CSP, and SSL redirect built in, use `github.com/unrolled/secure`. The stdlib approach is preferred for applications that want minimal dependencies — the middleware is 20 lines and has zero external dependencies. Either way, the middleware wraps the top-level handler so every response inherits the policy by default.

## Reference implementation
```go
package middleware

import "net/http"

// SecurityHeaders wraps a handler with a standard set of security headers.
func SecurityHeaders(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		h := w.Header()
		h.Set("X-Content-Type-Options", "nosniff")
		h.Set("X-Frame-Options", "DENY")
		h.Set("Referrer-Policy", "strict-origin-when-cross-origin")
		h.Set("Permissions-Policy",
			"camera=(), microphone=(), geolocation=(), payment=()")
		h.Set("Content-Security-Policy",
			"default-src 'none'; "+
				"script-src 'self'; "+
				"style-src 'self'; "+
				"img-src 'self'; "+
				"connect-src 'self'; "+
				"font-src 'self'; "+
				"base-uri 'self'; "+
				"form-action 'self'; "+
				"frame-ancestors 'none'")
		h.Set("Strict-Transport-Security",
			"max-age=31536000; includeSubDomains")
		// Do not set a Server header — Go's net/http does not add one by default.
		next.ServeHTTP(w, r)
	})
}

// Usage in main:
// mux := http.NewServeMux()
// mux.HandleFunc("GET /", indexHandler)
// http.ListenAndServeTLS(":443", "cert.pem", "key.pem",
//     middleware.SecurityHeaders(mux))
```

## Language-specific gotchas
- Go's `net/http` does not add a `Server` header by default — do not add one. If you use a framework like Gin or Echo, check whether it adds one and suppress it.
- `w.Header().Set()` must be called **before** `w.WriteHeader()` or `w.Write()`. Once the first byte is written, headers are sent and further `Set` calls are silently ignored. Place the security-headers middleware early in the chain.
- HSTS should only be set when the server is actually serving over TLS. If a plain-HTTP redirect server also runs (port 80 to 443), do not set HSTS on the redirect response — set it on the TLS response.
- For CSP nonces, generate a per-request nonce with `crypto/rand`, store it in the request context, include it in the CSP header as `script-src 'self' 'nonce-<value>'`, and pass it to `html/template` for inline script tags.
- `github.com/unrolled/secure` panics if misconfigured (e.g., conflicting SSL options). Test your configuration at startup, not at request time.
- Middleware ordering matters: place `SecurityHeaders` outside (before) any compression middleware. If compression runs first and writes headers, the security-headers middleware's `Set` calls may be too late.

## Tests to write
- Every response from the wrapped handler includes `X-Content-Type-Options: nosniff`.
- `Content-Security-Policy` header is present and includes `default-src 'none'`.
- `Strict-Transport-Security` header is present with `max-age=31536000`.
- `X-Frame-Options: DENY` is present.
- No `Server` header is present in the response.
