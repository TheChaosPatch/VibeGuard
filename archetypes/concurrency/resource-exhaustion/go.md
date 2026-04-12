---
schema_version: 1
archetype: concurrency/resource-exhaustion
language: go
principles_file: _principles.md
libraries:
  preferred: golang.org/x/sync/semaphore
  acceptable:
    - golang.org/x/sync/errgroup
    - net/http (MaxConnsPerHost)
  avoid:
    - name: unbounded goroutine spawning per request
      reason: Goroutines are cheap but not free; spawning one per request with no bound exhausts memory and scheduler resources under load.
minimum_versions:
  go: "1.23"
---

# Resource Exhaustion Prevention — Go

## Library choice
`golang.org/x/sync/semaphore` provides a weighted semaphore with `Acquire(ctx, n)` that accepts a context for cancellation and timeout. Buffered channels act as counting semaphores for simple cases. `golang.org/x/sync/errgroup` with `SetLimit(n)` bounds the number of concurrent goroutines in a group. `net/http.Transport` has `MaxConnsPerHost` and `MaxIdleConnsPerHost` to limit outbound HTTP concurrency.

## Reference implementation
```go
package concurrency

import (
    "context"
    "fmt"

    "golang.org/x/sync/semaphore"
    "golang.org/x/sync/errgroup"
)

const maxConcurrentDB = 20

var dbSem = semaphore.NewWeighted(maxConcurrentDB)

// QueryDB gates concurrent access to the database to maxConcurrentDB.
func QueryDB(ctx context.Context, fn func(context.Context) error) error {
    ctx, cancel := context.WithTimeout(ctx, 5*/* seconds */ 1e9)
    defer cancel()
    if err := dbSem.Acquire(ctx, 1); err != nil {
        return fmt.Errorf("database concurrency limit reached: %w", err)
    }
    defer dbSem.Release(1)
    return fn(ctx)
}

// ProcessBatch processes items with at most maxConcurrentDB concurrent goroutines.
func ProcessBatch(ctx context.Context, items []string, process func(context.Context, string) error) error {
    g, ctx := errgroup.WithContext(ctx)
    g.SetLimit(maxConcurrentDB) // goroutine pool bound

    for _, item := range items {
        item := item
        g.Go(func() error {
            return process(ctx, item)
        })
    }
    return g.Wait()
}
```

## Language-specific gotchas
- `go func() { ... }()` inside a loop with no bound is an unbounded goroutine pool. A request burst spawns thousands of goroutines, each consuming ~8 KB of stack minimum. Use `errgroup.SetLimit` or a worker pool pattern.
- `semaphore.Acquire(ctx, 1)` returns an error if the context is cancelled or times out. Always check the error — failure to acquire must not proceed into the guarded section.
- Buffered channels as semaphores (`ch <- struct{}{}` to acquire, `<-ch` to release) work but do not support context cancellation. Use `golang.org/x/sync/semaphore` for cancellable acquisition.
- `net/http.DefaultTransport` has `MaxIdleConnsPerHost = 2` by default — too low for high-throughput services. Create a custom `Transport` with `MaxConnsPerHost` and `MaxIdleConnsPerHost` tuned to the target service.
- `errgroup.SetLimit(-1)` removes the limit entirely. Ensure the limit value is always a positive integer from configuration, not computed in a way that could produce -1 or 0.

## Tests to write
- `QueryDB` with `maxConcurrentDB` goroutines holding the semaphore; assert the next call returns `context.DeadlineExceeded`.
- `ProcessBatch` with 100 items and `SetLimit(5)`; assert at most 5 goroutines are active simultaneously (use an atomic counter).
- `errgroup.SetLimit(0)` panics — assert the panic in a recover test.
- `net/http.Transport` `MaxConnsPerHost` set to 5; make 10 simultaneous requests; assert the 6th–10th wait for a connection slot.
