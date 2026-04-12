---
schema_version: 1
archetype: concurrency/race-conditions
language: go
principles_file: _principles.md
libraries:
  preferred: database/sql + GORM (row-level locking)
  acceptable:
    - github.com/go-redsync/redsync (distributed locks)
    - sqlx
  avoid:
    - name: sync.Mutex for cross-instance coordination
      reason: Only protects the current process. Other instances hold their own mutex.
    - name: Check-then-act without a transaction
      reason: Classic TOCTOU. The gap between check and act is the vulnerability.
minimum_versions:
  go: "1.22"
---

# Race Condition Defense — Go

## Library choice
Go's `database/sql` and GORM both support transactions with `SELECT ... FOR UPDATE` for pessimistic locking. GORM's `Clauses(clause.Locking{Strength: "UPDATE"})` is the idiomatic wrapper. For optimistic concurrency, add a `version` column and include it in the `WHERE` clause of updates. For distributed coordination, `go-redsync/redsync` implements RedLock over Redis. Go's `sync.Mutex` and channels protect in-process state and are excellent for that purpose, but they do not coordinate across multiple instances of a service.

## Reference implementation
```go
package payments

import (
	"context"
	"errors"
	"fmt"
	"gorm.io/gorm"
	"gorm.io/gorm/clause"
)

var ErrInsufficientFunds = errors.New("insufficient funds")

type Account struct{ ID string; Balance, Version int }

type Payment struct{ ID, AccountID, IdempotencyKey string; Amount int }

func Debit(ctx context.Context, db *gorm.DB, acctID string, amount int, key string) error {
	return db.WithContext(ctx).Transaction(func(tx *gorm.DB) error {
		var p Payment
		if tx.Where("idempotency_key = ?", key).First(&p).Error == nil {
			return nil // already processed — unique index is the real guard
		}
		var acct Account
		if err := tx.Clauses(clause.Locking{Strength: "UPDATE"}).
			Where("id = ?", acctID).First(&acct).Error; err != nil {
			return fmt.Errorf("payments: lock %s: %w", acctID, err)
		}
		if acct.Balance < amount {
			return ErrInsufficientFunds
		}
		acct.Balance -= amount
		acct.Version++
		if err := tx.Save(&acct).Error; err != nil {
			return fmt.Errorf("payments: save: %w", err)
		}
		return tx.Create(&Payment{
			ID: newID(), AccountID: acctID, Amount: amount, IdempotencyKey: key,
		}).Error
	})
}
```

## Language-specific gotchas
- `sync.Mutex` protects goroutines within a single process. When running multiple replicas behind a load balancer, each replica has its own mutex. Use database locks or a distributed lock for cross-instance invariants.
- GORM's `clause.Locking{Strength: "UPDATE"}` adds `FOR UPDATE` to the query. The lock is held until the enclosing transaction commits or rolls back. Keep transactions short — do not perform HTTP calls or expensive computation inside them.
- Go's `-race` detector (`go test -race ./...`) finds data races on in-memory state but not database-level race conditions. Test database races by running concurrent goroutines that hit the same row and asserting the invariant holds.
- Channels serialize access to shared state within a process and are idiomatic Go. For single-instance services, a channel-based worker pattern can replace a mutex. For multi-instance services, channels are invisible to other instances.
- Idempotency keys enforced by a GORM unique index produce a database error on duplicate insert. Handle this error by returning success (the operation already happened), not by logging and retrying.
- `gorm.DB.Transaction(func(tx *gorm.DB) error)` automatically commits on nil return and rolls back on error. Do not call `tx.Commit()` manually inside the callback — it causes a double-commit.
- Optimistic concurrency in GORM: `tx.Model(&acct).Where("version = ?", oldVersion).Updates(...)`. If the returned `RowsAffected` is zero, another writer won. Retry with bounded attempts.

## Tests to write
- Double-debit: launch 50 goroutines debiting the same account with different keys — assert the final balance is correct and no overdraft occurred.
- Idempotency: call `Debit` twice with the same key — assert the balance is debited once.
- `FOR UPDATE` serialization: two goroutines lock the same row — assert both complete without deadlock and the balance is consistent.
- Race detector: `go test -race ./...` passes with no data race warnings on all in-memory shared state.
