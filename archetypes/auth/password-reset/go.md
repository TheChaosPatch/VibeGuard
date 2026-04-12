---
schema_version: 1
archetype: auth/password-reset
language: go
principles_file: _principles.md
libraries:
  preferred: crypto/rand (stdlib)
  acceptable:
    - Custom HMAC-signed token (no DB row)
  avoid:
    - name: math/rand
      reason: PRNG — not cryptographically secure.
    - name: google/uuid
      reason: UUID v4 entropy is adequate but crypto/rand is explicit and idiomatic for secrets.
minimum_versions:
  go: "1.22"
---

# Secure Password Reset — Go

## Library choice
Go's `crypto/rand` is the correct source for reset tokens — no third-party library is needed for token generation. Database persistence (PostgreSQL via `pgx`, SQLite via `modernc.org/sqlite`) stores the token hash. Alternatively, a signed token using `crypto/hmac` + `encoding/binary` for expiry avoids a database round-trip but cannot be single-use without a Redis denylist.

## Reference implementation
```go
package reset

import (
    "context"; "crypto/rand"; "crypto/sha256"
    "encoding/hex"; "errors"; "time"
)

const (
    tokenBytes = 32
    expiry     = 30 * time.Minute
)

func RequestReset(ctx context.Context, store Store, email string, send func(string, string)) error {
    user, err := store.GetUserByEmail(ctx, email)
    if errors.Is(err, ErrNotFound) { return nil }
    if err != nil { return err }
    _ = store.InvalidateTokens(ctx, user.ID)
    raw := make([]byte, tokenBytes)
    if _, err := rand.Read(raw); err != nil { return err }
    sum := sha256.Sum256(raw)
    hash := hex.EncodeToString(sum[:])
    if err := store.SaveToken(ctx, user.ID, hash, time.Now().UTC().Add(expiry)); err != nil { return err }
    send(user.Email, hex.EncodeToString(raw))
    return nil
}

func RedeemReset(ctx context.Context, store Store, tokenHex, newPwHash string) (bool, error) {
    raw, err := hex.DecodeString(tokenHex)
    if err != nil || len(raw) != tokenBytes { return false, nil }
    sum := sha256.Sum256(raw)
    hash := hex.EncodeToString(sum[:])
    rec, err := store.GetToken(ctx, hash)
    if errors.Is(err, ErrNotFound) || rec.Consumed || rec.ExpiresAt.Before(time.Now().UTC()) { return false, nil }
    if err != nil { return false, err }
    _ = store.ConsumeToken(ctx, hash)
    _ = store.UpdatePassword(ctx, rec.UserID, newPwHash)
    return true, store.InvalidateSessions(ctx, rec.UserID)
}
```

## Language-specific gotchas
- `rand.Read` in Go 1.20+ always succeeds on supported platforms (it panics rather than returning an error internally), but check the error anyway for forward compatibility.
- `sha256.Sum256` returns `[32]byte` — convert to slice with `sum[:]` before passing to `hex.EncodeToString`.
- `hex.DecodeString` returns an error for odd-length or non-hex input — always check it and reject with `false, nil`, not a 500.
- The `Store` interface keeps the service testable with a mock. Do not call the database package directly from the reset logic.
- Use `hmac.Equal` (not `==` or `bytes.Equal`) if you ever compare token bytes directly. For hash comparison via database lookup by hash value, the database comparison is fine.

## Tests to write
- `RequestReset` for an unknown email returns `nil` and calls `send` zero times.
- `RedeemReset` with a valid hex token returns `true, nil`.
- Second `RedeemReset` with the same token returns `false, nil`.
- `RedeemReset` with an expired `ExpiresAt` returns `false, nil`.
- `RedeemReset` with garbage input returns `false, nil` without error.
