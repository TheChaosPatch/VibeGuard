---
schema_version: 1
archetype: concurrency/deadlock-prevention
language: go
principles_file: _principles.md
libraries:
  preferred: sync (stdlib)
  acceptable:
    - golang.org/x/sync/semaphore
  avoid:
    - name: sync.Mutex without timeout wrapper
      reason: sync.Mutex.Lock() blocks forever; wrap with a channel-based timeout to detect deadlocks in production.
minimum_versions:
  go: "1.23"
---

# Deadlock Prevention — Go

## Library choice
`sync.Mutex` and `sync.RWMutex` from the stdlib are the primary primitives. Go does not provide a built-in try-lock with timeout on `sync.Mutex` (added as `TryLock()` in Go 1.18, but without a duration). For timeout-based acquisition, use a `golang.org/x/sync/semaphore` (weighted semaphore that supports `TryAcquire`) or a goroutine + channel pattern. For database deadlocks, detect the PostgreSQL error code `40P01` and retry.

## Reference implementation
```go
package transfer

import (
    "context"
    "errors"
    "sync"
    "time"
)

var ErrLockTimeout = errors.New("could not acquire lock within timeout")

func tryLock(ctx context.Context, mu *sync.Mutex, timeout time.Duration) (unlock func(), err error) {
    done := make(chan struct{})
    go func() { mu.Lock(); close(done) }()
    select {
    case <-done:
        return mu.Unlock, nil
    case <-time.After(timeout):
        go func() { <-done; mu.Unlock() }()
        return nil, ErrLockTimeout
    case <-ctx.Done():
        go func() { <-done; mu.Unlock() }()
        return nil, ctx.Err()
    }
}

var (
    muA sync.Mutex
    muB sync.Mutex
)

func Transfer(ctx context.Context) error {
    unlockA, err := tryLock(ctx, &muA, 5*time.Second)
    if err != nil { return err }
    defer unlockA()
    unlockB, err := tryLock(ctx, &muB, 5*time.Second)
    if err != nil { return err }
    defer unlockB()
    return doWork(ctx)
}

func doWork(_ context.Context) error { return nil }
```

## Language-specific gotchas
- `sync.Mutex` is not re-entrant. Calling `mu.Lock()` twice from the same goroutine deadlocks immediately. Unlike Java's `synchronized`, there is no re-entrancy — design around it.
- `sync.Mutex.TryLock()` (Go 1.18+) returns false if the lock is held but does not accept a timeout. Use the goroutine+channel pattern above for deadline-based acquisition.
- Copying a `sync.Mutex` after first use causes a data race — the copy shares no state with the original. Pass `*sync.Mutex` (pointer), not `sync.Mutex` by value. `go vet` catches this.
- `defer mu.Unlock()` releases the lock when the enclosing function returns, not when the `defer` statement is reached. In a loop, this means all unlocks happen at function return — hold the lock for the entire loop duration unintentionally. Use an explicit unlock inside the loop body.
- `sync.RWMutex.RLock()` allows multiple concurrent readers. Acquiring a write lock while readers are active blocks until all read locks are released. This is not a deadlock, but many simultaneous readers can starve a writer indefinitely.

## Tests to write
- `Transfer(ctx)` from two goroutines simultaneously; assert both return nil within 2 seconds.
- Acquire `muB` then call `Transfer`; assert `ErrLockTimeout` is returned within ~5 seconds (demonstrates rank inversion is caught by timeout).
- `tryLock` with a cancelled context; assert `context.Canceled` is returned.
- `go vet ./...` — assert no `sync.Mutex` copy warnings.
