---
schema_version: 1
archetype: errors/error-handling
language: go
principles_file: _principles.md
libraries:
  preferred: standard library errors + fmt.Errorf wrapping
  acceptable:
    - github.com/pkg/errors (legacy codebases)
  avoid:
    - name: panic for ordinary errors
      reason: Panics are for truly unrecoverable invariants, not failed I/O.
minimum_versions:
  go: "1.22"
---

# Error Handling — Go

## Library choice
The standard library's `errors` package plus `fmt.Errorf` with `%w` covers almost every case. Sentinel errors (`var ErrNotFound = errors.New(...)`) and typed errors (implementing `error`) together let callers decide what to handle.

## Reference implementation
```go
package orders

import (
    "context"
    "errors"
    "fmt"
    "log/slog"
)

var ErrAlreadySubmitted = errors.New("order already submitted")

type Repository interface {
    Get(ctx context.Context, id string) (*Order, error)
    Save(ctx context.Context, o *Order) error
}

type Service struct {
    repo   Repository
    logger *slog.Logger
}

func (s *Service) Submit(ctx context.Context, req SubmitRequest) (*Order, error) {
    existing, err := s.repo.Get(ctx, req.OrderID)
    if err != nil && !errors.Is(err, ErrNotFound) {
        return nil, fmt.Errorf("orders: lookup %s: %w", req.OrderID, err)
    }
    if existing != nil {
        return nil, ErrAlreadySubmitted
    }
    order, err := NewOrder(req)
    if err != nil {
        return nil, fmt.Errorf("orders: build %s: %w", req.OrderID, err)
    }
    if err := s.repo.Save(ctx, order); err != nil {
        s.logger.WarnContext(ctx, "save failed",
            slog.String("order_id", req.OrderID),
            slog.String("error", err.Error()))
        return nil, fmt.Errorf("orders: save %s: %w", req.OrderID, err)
    }
    return order, nil
}
```

## Language-specific gotchas
- `%w` in `fmt.Errorf` wraps so callers can use `errors.Is` / `errors.As`. `%v` or `%s` drops the chain — don't.
- Always handle the error right after the call. Accumulating checked errors in a list and dealing with them later loses context.
- `panic` is for programmer errors, not user errors. A failed DB call is not a panic.
- `defer` for cleanup, but watch closure capture: `defer f(err)` captures `err` at defer time, not at call time.
- Log errors at the boundary that decides to convert them into user-visible responses, not at every wrap. One error, one log line.

## Tests to write
- Happy path returns the order and no error.
- Duplicate submit returns `ErrAlreadySubmitted` — use `errors.Is` in the test.
- Repo `Get` returning a generic error is wrapped with context (check `err.Error()` contains the order ID).
- Repo `Save` failing logs a warning with structured fields.
