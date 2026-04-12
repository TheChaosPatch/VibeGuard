---
schema_version: 1
archetype: http/csrf
language: go
principles_file: _principles.md
libraries:
  preferred: gorilla/csrf
  acceptable:
    - justinas/nosurf
  avoid:
    - name: Custom token generation with math/rand
      reason: math/rand is not cryptographically secure; CSRF tokens must use crypto/rand.
minimum_versions:
  go: "1.22"
---

# Cross-Site Request Forgery Defense — Go

## Library choice
Go's stdlib `net/http` does not include CSRF middleware. `gorilla/csrf` is the de facto standard: it generates per-session tokens, validates them on unsafe methods, sets a `SameSite` cookie, and provides template helpers. `justinas/nosurf` is an acceptable alternative with a similar API. For APIs that use only bearer-token authentication (no cookies), CSRF protection is unnecessary — skip the middleware for those routes entirely. Never roll your own token scheme with `math/rand` — use `crypto/rand` through a proven library.

## Reference implementation
```go
package main

import (
	"net/http"

	"github.com/gorilla/csrf"
	"github.com/gorilla/mux"
)

func main() {
	// Key must be 32 bytes, loaded from a secret store — not hardcoded.
	csrfKey := loadSecretKey() // []byte, 32 bytes from env/vault

	csrfMiddleware := csrf.Protect(
		csrfKey,
		csrf.Secure(true),                    // requires HTTPS
		csrf.SameSite(csrf.SameSiteStrictMode),
		csrf.HttpOnly(true),
		csrf.Path("/"),
		csrf.CookieName("__Host-csrf"),
	)

	r := mux.NewRouter()
	r.HandleFunc("/form", showForm).Methods("GET")
	r.HandleFunc("/submit", handleSubmit).Methods("POST")

	// Apply CSRF to all routes that use cookie auth.
	http.ListenAndServeTLS(":443", "cert.pem", "key.pem", csrfMiddleware(r))
}

func showForm(w http.ResponseWriter, r *http.Request) {
	// Pass the token to the template for inclusion in the form.
	token := csrf.Token(r)
	// render template with token as a hidden field:
	// <input type="hidden" name="gorilla.csrf.Token" value="{{.Token}}">
	renderTemplate(w, "form.html", map[string]string{"Token": token})
}

func handleSubmit(w http.ResponseWriter, r *http.Request) {
	// If we reach here, the CSRF middleware already validated the token.
	w.WriteHeader(http.StatusOK)
}
```

## Language-specific gotchas
- `gorilla/csrf` validates on POST, PUT, PATCH, DELETE by default and skips GET, HEAD, OPTIONS. Do not add state-changing logic to GET handlers.
- The `csrf.Protect()` key must be exactly 32 bytes and must be the same across all instances of the application behind a load balancer. If each instance generates its own key, tokens from one instance will fail validation on another.
- `csrf.Secure(true)` sets the `Secure` flag on the cookie — it will not work over plain HTTP. In local development, use `csrf.Secure(false)` behind a build tag or environment check, never in production.
- For SPAs: `gorilla/csrf` can send the token in a non-HttpOnly cookie (set `csrf.HttpOnly(false)`) so JavaScript can read it and send it as the `X-CSRF-Token` header. The middleware checks the header automatically.
- `gorilla/csrf` stores the token in a cookie by default. If you need server-side storage (session-based), use `csrf.RequestHeader` and `csrf.FieldName` to customize, or switch to `nosurf` which supports both.
- Token-authenticated API routes (e.g., `/api/v1/*` with bearer tokens) should be mounted on a separate router that does not use the CSRF middleware. Mixing cookie-auth and token-auth routes under the same CSRF middleware creates confusing failures.

## Tests to write
- POST without a CSRF token returns 403.
- POST with a valid CSRF token (from a preceding GET) returns 200.
- POST with a tampered token returns 403.
- GET request is not blocked by the CSRF middleware.
- Token from one session is rejected when used with a different session cookie.
