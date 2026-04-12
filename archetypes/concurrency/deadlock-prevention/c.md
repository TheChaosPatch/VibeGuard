---
schema_version: 1
archetype: concurrency/deadlock-prevention
language: c
principles_file: _principles.md
libraries:
  preferred: POSIX pthread (pthread_mutex_timedlock)
  acceptable:
    - C11 mtx_t (threads.h)
  avoid:
    - name: pthread_mutex_lock without pthread_mutex_timedlock alternative
      reason: pthread_mutex_lock blocks indefinitely; use pthread_mutex_timedlock with a deadline to detect and recover from deadlocks.
minimum_versions:
  c: "C11"
---

# Deadlock Prevention — C

## Library choice
POSIX pthreads (`pthread_mutex_t`) are the standard mutual exclusion primitive on Linux/macOS. `pthread_mutex_timedlock` acquires the mutex or returns `ETIMEDOUT` after a deadline — use this in production code to avoid permanent blocking. C11's `mtx_t` with `mtx_timedlock` is the portable standard alternative.

## Reference implementation
```c
#include <pthread.h>
#include <time.h>
#include <errno.h>
#include <stdint.h>
#include <stdio.h>

/* Rank 1 < Rank 2 — always acquire lock_a before lock_b. */
static pthread_mutex_t lock_a = PTHREAD_MUTEX_INITIALIZER; /* rank 1 */
static pthread_mutex_t lock_b = PTHREAD_MUTEX_INITIALIZER; /* rank 2 */

static int try_lock(pthread_mutex_t *mu, long timeout_ms) {
    struct timespec ts;
    clock_gettime(CLOCK_REALTIME, &ts);
    ts.tv_sec  += timeout_ms / 1000;
    ts.tv_nsec += (timeout_ms % 1000) * 1000000L;
    if (ts.tv_nsec >= 1000000000L) {
        ts.tv_sec++;
        ts.tv_nsec -= 1000000000L;
    }
    int rc = pthread_mutex_timedlock(mu, &ts);
    if (rc == ETIMEDOUT) return -1;
    return rc;
}

int transfer(void) {
    /* Acquire in rank order. */
    if (try_lock(&lock_a, 5000) != 0) {
        fprintf(stderr, "timeout acquiring lock_a\n");
        return -1;
    }
    if (try_lock(&lock_b, 5000) != 0) {
        pthread_mutex_unlock(&lock_a);
        fprintf(stderr, "timeout acquiring lock_b\n");
        return -1;
    }

    /* Critical section — no I/O, no external calls. */

    pthread_mutex_unlock(&lock_b);
    pthread_mutex_unlock(&lock_a);
    return 0;
}
```

## Language-specific gotchas
- `pthread_mutex_lock` blocks indefinitely. On Linux, `pthread_mutex_timedlock` requires `_POSIX_C_SOURCE >= 199309L` or `_XOPEN_SOURCE >= 600`. Set the feature-test macro in the compilation flags.
- Mutexes must be destroyed with `pthread_mutex_destroy` before the memory is freed. Failing to do so on heap-allocated mutexes leaks OS resources.
- Error checking: every `pthread_*` function returns an error code. Ignoring return values hides bugs — check and log or abort on unexpected errors.
- `PTHREAD_MUTEX_ERRORCHECK` type mutex (`pthread_mutexattr_settype`) returns `EDEADLK` instead of deadlocking when the same thread attempts to re-acquire. Enable it in debug builds.
- Releasing a mutex from a thread that does not own it is undefined behaviour with default `PTHREAD_MUTEX_DEFAULT`. Use `PTHREAD_MUTEX_ERRORCHECK` to catch this in testing.
- Signal handlers must not acquire mutexes that are also acquired by the main thread — async-signal-safety constraints make this a deadlock source.

## Tests to write
- Two threads call `transfer()` simultaneously; assert both return 0 and complete within 2 seconds.
- Lock `lock_b` in a helper thread, then call `transfer()` from the main thread; assert `transfer()` returns -1 within ~5 seconds (timeout from `lock_b`).
- Compile with `PTHREAD_MUTEX_ERRORCHECK` and attempt to re-acquire `lock_a` from the same thread; assert `EDEADLK` is returned.
- Valgrind Helgrind or ThreadSanitizer: run the concurrent test under TSAN and assert no data race reports.
