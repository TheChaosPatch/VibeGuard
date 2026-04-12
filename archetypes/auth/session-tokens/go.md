---
schema_version: 1
archetype: auth/session-tokens
language: go
principles_file: _principles.md
libraries:
  preferred: github.com/gorilla/sessions
  acceptable:
    - github.com/alexedwards/scs/v2
  avoid:
    - name: math/rand for token generation
      reason: Not cryptographically secure. Use crypto/rand exclusively.
    - name: net/http cookie helpers without explicit flag setting
      reason: Defaults omit HttpOnly, Secure, and SameSite. Every flag must be set explicitly.
minimum_versions:
  go: "1.22"
---

# Session Token Management -- Go

## Library choice
`gorilla/sessions` provides a clean interface for cookie-backed and server-side sessions with pluggable backends (filesystem, Redis, database). `alexedwards/scs` is an excellent alternative with built-in middleware and a simpler API for server-side stores. Both handle cookie serialization and flag management correctly. The standard library's `net/http` has cookie support but no session abstraction -- you would need to build token generation, storage, expiry, and rotation yourself, which is where bugs hide.

## Reference implementation
```go
package session

import (
	"crypto/rand"
	"encoding/base64"
	"net/http"
	"sync"
	"time"
)

const (
	tokenBytes = 32; idleTimeout = 30 * time.Minute
	maxAge = 8 * time.Hour; cookieName = "__Host-Session"
)

type Session struct{ UserID string; CreatedAt, LastActive time.Time }
type Store struct{ mu sync.RWMutex; m map[string]*Session }
func NewStore() *Store { return &Store{m: make(map[string]*Session)} }

func (s *Store) Create(w http.ResponseWriter, userID string) {
	buf := make([]byte, tokenBytes)
	if _, err := rand.Read(buf); err != nil { panic("crypto/rand: " + err.Error()) }
	id, now := base64.URLEncoding.EncodeToString(buf), time.Now().UTC()
	s.mu.Lock(); s.m[id] = &Session{userID, now, now}; s.mu.Unlock()
	http.SetCookie(w, &http.Cookie{
		Name: cookieName, Value: id, Path: "/",
		HttpOnly: true, Secure: true, SameSite: http.SameSiteLaxMode,
		MaxAge: int(maxAge.Seconds()),
	})
}

func (s *Store) Validate(r *http.Request) *Session {
	c, err := r.Cookie(cookieName)
	if err != nil { return nil }
	s.mu.RLock(); sess, ok := s.m[c.Value]; s.mu.RUnlock()
	if !ok { return nil }
	now := time.Now().UTC()
	if now.Sub(sess.LastActive) > idleTimeout || now.Sub(sess.CreatedAt) > maxAge {
		s.Destroy(c.Value); return nil
	}
	s.mu.Lock(); sess.LastActive = now; s.mu.Unlock()
	return sess
}

func (s *Store) Destroy(id string) { s.mu.Lock(); delete(s.m, id); s.mu.Unlock() }
```

## Language-specific gotchas
- `crypto/rand.Read` is the only acceptable source of session token bytes. `math/rand` is seeded and predictable -- importing it anywhere near token generation is a defect.
- The `__Host-` cookie prefix requires `Secure`, `Path=/`, and no `Domain` attribute. Go's `http.Cookie` does not enforce this -- you must set the fields correctly and test the output.
- `sync.RWMutex` on the in-memory store is mandatory. HTTP handlers run on separate goroutines -- an unprotected map is a data race. In production, replace the in-memory map with Redis or a database and remove the mutex.
- `panic` on `crypto/rand.Read` failure is deliberate. If the OS entropy pool is exhausted, the system is in an unrecoverable state and issuing weak tokens is worse than crashing.
- `gorilla/sessions` stores session data in encrypted cookies by default (using `securecookie`). To get server-side storage, use a store backend like `redistore` or `pgstore`. Without a server-side store, you cannot revoke sessions.

## Tests to write
- Round-trip: create a session, validate with the returned cookie, confirm the `UserID` matches.
- Idle timeout: create a session, advance the clock past 30 minutes, confirm `Validate` returns nil.
- Absolute timeout: keep a session active with periodic validation, advance past 8 hours total, confirm rejection.
- Destroy: create a session, destroy it, confirm `Validate` returns nil.
- Token entropy: create 1000 sessions, confirm all IDs are unique and at least 43 characters long.
- Cookie flags: inspect the `Set-Cookie` header and assert `HttpOnly`, `Secure`, `SameSite=Lax`, and `__Host-` prefix.
- Concurrent access: create, validate, and destroy sessions from multiple goroutines to exercise the mutex under the race detector.
