---
schema_version: 1
archetype: concurrency/deadlock-prevention
language: rust
principles_file: _principles.md
libraries:
  preferred: std::sync (Mutex, RwLock)
  acceptable:
    - tokio::sync::Mutex (async)
    - parking_lot::Mutex
  avoid:
    - name: std::sync::Mutex::lock() without poisoning check
      reason: A panicking thread leaves the Mutex poisoned; callers must handle PoisonError explicitly or the guard is silently dropped.
minimum_versions:
  rust: "1.85"
---

# Deadlock Prevention — Rust

## Library choice
`std::sync::Mutex<T>` wraps the protected data, making it impossible to access the data without holding the lock — a compile-time enforcement of mutual exclusion. `parking_lot::Mutex` from the `parking_lot` crate is faster and adds `try_lock_for(Duration)` for timeout-based acquisition. For async code, `tokio::sync::Mutex` is a non-blocking async mutex.

## Reference implementation
```rust
use std::sync::{Arc, Mutex};
use std::time::Duration;
use parking_lot::Mutex as ParkingMutex;

// Rank 1 < Rank 2 — always acquire lock_a before lock_b.
pub struct TransferService {
    lock_a: Arc<ParkingMutex<AccountState>>, // rank 1
    lock_b: Arc<ParkingMutex<AccountState>>, // rank 2
}

pub struct AccountState {
    pub balance: i64,
}

impl TransferService {
    pub fn transfer(&self, amount: i64) -> Result<(), &'static str> {
        // Acquire in rank order with timeout.
        let mut guard_a = self
            .lock_a
            .try_lock_for(Duration::from_secs(5))
            .ok_or("Could not acquire lock A within timeout")?;

        let mut guard_b = self
            .lock_b
            .try_lock_for(Duration::from_secs(5))
            .ok_or("Could not acquire lock B within timeout")?;

        // Guards drop (releasing locks) at end of scope — no explicit unlock.
        guard_a.balance -= amount;
        guard_b.balance += amount;
        Ok(())
    }
}
```

## Language-specific gotchas
- `std::sync::Mutex::lock()` blocks the calling thread indefinitely. `parking_lot::Mutex::try_lock_for(Duration)` adds a timeout — prefer it in production. `std::sync::Mutex` does not have a timeout variant.
- Rust's ownership model prevents data races at compile time, but deadlocks are still possible — the borrow checker does not reason about lock ordering. Lock order is an architectural convention, not a language guarantee.
- `std::sync::Mutex` becomes poisoned when a thread panics while holding the lock. `lock().unwrap()` propagates the `PoisonError`; use `lock().unwrap_or_else(|e| e.into_inner())` to recover the guard if the data is still valid.
- `Arc<Mutex<T>>` clones the `Arc`, not the `Mutex`. Both clones point to the same underlying lock. This is the correct pattern for sharing state between threads.
- `tokio::sync::Mutex` is async — `.lock().await` suspends without blocking a thread. Do not use `std::sync::Mutex` inside an async context (it blocks the async executor thread).
- Holding two `MutexGuard`s simultaneously in Rust is safe from the borrow checker's perspective — ordering is your responsibility.

## Tests to write
- `transfer(100)` from two threads simultaneously; assert both return `Ok(())` and balances are consistent.
- Hold `lock_a` from one thread while `transfer` is called from another; assert `Err("Could not acquire lock A within timeout")` is returned within ~5 seconds.
- Poisoned mutex: panic in a closure that holds `lock_a`; assert subsequent `try_lock_for` on the same mutex still succeeds by unwrapping the `PoisonError`.
- Verify lock ordering with `clippy::mutex_atomic` and `clippy::await_holding_lock` lint annotations in the test configuration.
