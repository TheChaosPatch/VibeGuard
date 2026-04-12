---
schema_version: 1
archetype: logging/audit-trail
language: go
principles_file: _principles.md
libraries:
  preferred: database/sql (append-only table)
  acceptable:
    - GORM
    - log/slog (dedicated audit logger)
  avoid:
    - name: log.Println for audit events
      reason: No structure, no sink control, no durability guarantee. Audit events are not debug logs.
    - name: fmt.Fprintf to a file
      reason: No rotation control, no tamper evidence, no structured fields.
minimum_versions:
  go: "1.22"
---

# Security Audit Trail — Go

## Library choice
Audit events go through a dedicated `AuditWriter` interface that inserts structured events into an append-only database table. The database connection uses credentials with INSERT-only grants. For tamper evidence, each event includes an HMAC chain linking it to the previous event. `log/slog` with a dedicated handler can serve as a secondary audit output but must not be the primary store — slog is designed for application logging with best-effort delivery, not for durable audit records. GORM is acceptable as the database layer for the audit writer.

## Reference implementation
```go
package audit

import (
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"database/sql"
	"encoding/hex"
	"fmt"
	"time"
)

type Event struct {
	ID, ActorID, Action, CorrelationID, Outcome string
	Timestamp                                    time.Time
	TargetID, Detail, PreviousHash, Hash         string
}

type Writer struct {
	db       *sql.DB
	hmacKey  []byte
	prevHash string
}

func NewWriter(db *sql.DB, key []byte) *Writer {
	return &Writer{db: db, hmacKey: key, prevHash: "0000000000000000"}
}

func (w *Writer) Write(ctx context.Context, ev Event) error {
	ev.Timestamp, ev.PreviousHash = time.Now().UTC(), w.prevHash
	mac := hmac.New(sha256.New, w.hmacKey)
	fmt.Fprintf(mac, "%s%s|%s|%s|%s", w.prevHash, ev.ID, ev.ActorID, ev.Action, ev.Outcome)
	ev.Hash = hex.EncodeToString(mac.Sum(nil))
	const q = `INSERT INTO audit_events
		(id,actor_id,action,timestamp,correlation_id,outcome,target_id,detail,previous_hash,hash)
		VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)`
	if _, err := w.db.ExecContext(ctx, q, ev.ID, ev.ActorID, ev.Action, ev.Timestamp,
		ev.CorrelationID, ev.Outcome, ev.TargetID, ev.Detail, ev.PreviousHash, ev.Hash); err != nil {
		return fmt.Errorf("audit: insert: %w", err)
	}
	w.prevHash = ev.Hash
	return nil
}
```

## Language-specific gotchas
- `time.Now()` uses the system clock, which must be NTP-synchronized. Go does not warn you if the clock is skewed. In containerized environments, ensure NTP is configured in the host or use a cloud provider's time-sync service.
- The audit database connection should use a separate `sql.DB` instance with credentials that have INSERT-only grants. Sharing the application's `sql.DB` means the application can `DELETE FROM audit_events`.
- `ExecContext` returns an error if the INSERT fails. Do not ignore it — if the audit write fails, the calling function must also fail. Return the error up the call chain.
- The `Writer` struct holds `previousHash` in memory. In a multi-instance deployment, each instance maintains its own chain. For global chain integrity, read the last hash from the database on startup and use a database-level sequence or advisory lock to serialize inserts across instances.
- `log/slog` is excellent for application logging but does not guarantee delivery. If the process crashes between the slog call and the flush, the audit event is lost. The database INSERT is the durable record.
- Do not log PII in the `Detail` field. Use `ActorID` and `TargetID` as opaque identifiers that can be resolved through the identity service at query time.
- The HMAC key must come from a secrets manager or environment variable injected by the deployment platform, not from a config file in the repository. If the key is in version control, anyone with repo access can forge audit events.
- Context propagation: the `CorrelationID` should be extracted from the incoming request context (via middleware) and passed through to the audit writer. Without it, correlating events across services requires timestamp-based guessing.

## Tests to write
- Hash chain: write two events — assert the second event's `PreviousHash` equals the first event's `Hash`.
- Tamper detection: modify a stored event's `Detail` column, recompute the HMAC — assert it does not match the stored `Hash`.
- Insert-only: attempt a `DELETE` on the audit table with the audit writer's database user — assert a permission error.
- Error propagation: mock the database to return an error on `ExecContext` — assert `Write` returns a non-nil error (audit writes are not fire-and-forget).
- Concurrent writes: launch 20 goroutines writing audit events — assert all inserts succeed and the chain is consistent (in single-instance mode).
