---
schema_version: 1
archetype: auth/rate-limiting
language: go
principles_file: _principles.md
libraries:
  preferred: golang.org/x/time/rate
  acceptable:
    - github.com/ulule/limiter/v3
  avoid:
    - name: sync.Map with integer counters
      reason: In-process only — bypassed by multiple goroutine workers or horizontal scaling.
minimum_versions:
  go: "1.22"
---

# Rate Limiting and Brute Force Defense — Go

## Library choice
`golang.org/x/time/rate` provides a token bucket limiter and is the idiomatic Go choice for single-instance rate limiting. For distributed enforcement across multiple instances, use Redis with `github.com/go-redis/redis/v9` and the `INCR`+`EXPIRE` pattern, or `github.com/ulule/limiter/v3` which wraps Redis with sliding window support out of the box.

## Reference implementation
```go
package middleware

import (
    "context"
    "fmt"
    "net/http"
    "strconv"
    "time"

    "github.com/redis/go-redis/v9"
)

const accountLimit = 5
const windowSeconds = 300

type AccountLimiter struct{ rdb *redis.Client }

func NewAccountLimiter(rdb *redis.Client) *AccountLimiter { return &AccountLimiter{rdb: rdb} }

func (l *AccountLimiter) Allow(ctx context.Context, email string) (bool, error) {
    key := fmt.Sprintf("login:fail:%s", email)
    val, err := l.rdb.Get(ctx, key).Int()
    if err == redis.Nil { return true, nil }
    if err != nil { return true, err }
    return val < accountLimit, nil
}

func (l *AccountLimiter) RecordFailure(ctx context.Context, email string) error {
    key := fmt.Sprintf("login:fail:%s", email)
    pipe := l.rdb.TxPipeline()
    pipe.Incr(ctx, key)
    pipe.Expire(ctx, key, windowSeconds*time.Second)
    _, err := pipe.Exec(ctx)
    return err
}

func (l *AccountLimiter) Clear(ctx context.Context, email string) error {
    return l.rdb.Del(ctx, fmt.Sprintf("login:fail:%s", email)).Err()
}

func RetryAfterHeader(w http.ResponseWriter) {
    w.Header().Set("Retry-After", strconv.Itoa(windowSeconds))
    w.WriteHeader(http.StatusTooManyRequests)
}
```

## Language-specific gotchas
- Use `TxPipeline` (or `MULTI/EXEC`) to make `INCR` + `EXPIRE` atomic. Without a pipeline, a crash between the two commands leaves a key that never expires.
- `redis.Nil` is the error returned when a key does not exist — it is not a real error. Check for it explicitly before treating a Redis error as a failure.
- Failing open on Redis errors (`return true, err`) is safer for availability than failing closed (blocking all logins when Redis is down), but requires alerting on the error so the Redis outage is not invisible.
- `golang.org/x/time/rate` is in-process only. Use it only for local rate limiting (e.g., outbound request throttling). Authentication endpoint limiting requires Redis.
- When reading the client IP, trust `X-Real-IP` or `X-Forwarded-For` only from a configured trusted proxy IP range — never from arbitrary clients.

## Tests to write
- `Allow` returns `true` for a fresh key (no prior failures).
- `Allow` returns `false` after `accountLimit` calls to `RecordFailure`.
- `RecordFailure` key expires after `windowSeconds` — `Allow` returns `true` again.
- `Clear` removes the counter — `Allow` returns `true` immediately.
- Redis `Nil` error in `Allow` returns `true, nil` (fail-open, not an error).
