---
schema_version: 1
archetype: concurrency/resource-exhaustion
language: javascript
principles_file: _principles.md
libraries:
  preferred: p-limit
  acceptable:
    - bottleneck
    - async (async.queue)
  avoid:
    - name: Promise.all with unbounded array
      reason: Promise.all starts all promises simultaneously; with no concurrency limit, a large array exhausts the connection pool or file descriptors.
minimum_versions:
  node: "22"
---

# Resource Exhaustion Prevention — JavaScript

## Library choice
`p-limit` is a lightweight concurrency limiter that wraps async functions and enforces a maximum number of concurrent executions. `bottleneck` provides more advanced rate limiting and reservoir patterns. `async.queue` from the `async` library is a producer-consumer queue with a worker concurrency bound.

## Reference implementation
```javascript
import pLimit from "p-limit";

const DB_CONCURRENCY = 20;
const limit = pLimit(DB_CONCURRENCY);

// Each call is queued; at most DB_CONCURRENCY run simultaneously.
export async function queryDb(fn) {
    return limit(fn);
}

// Safe batch processing — no Promise.all over unbounded array.
export async function processBatch(items, processItem) {
    const limiter = pLimit(DB_CONCURRENCY);
    const results = await Promise.all(
        items.map((item) => limiter(() => processItem(item)))
    );
    return results;
}

// Bounded queue pattern using async.queue.
import { queue } from "async";

const workQueue = queue(async (task) => {
    await processTask(task);
}, DB_CONCURRENCY); // worker concurrency = 20

workQueue.error((err, task) => {
    console.error(`Task failed: ${err.message}`, task);
});

export function enqueue(task) {
    if (workQueue.length() >= 200) {
        throw new Error("Work queue at capacity — backpressure applied.");
    }
    workQueue.push(task);
}

async function processTask(task) {}
```

## Language-specific gotchas
- `Promise.all(items.map(processItem))` starts all promises at once. With 1000 items and a database connection pool of 20, 980 promises are waiting for connections — holding event loop resources and potentially timing out.
- `p-limit` returns a wrapped function. Calling `limit(fn)` does not start `fn` if the concurrency cap is reached — it queues it. The returned promise resolves when `fn` completes.
- Node.js is single-threaded for JavaScript execution; CPU-bound tasks should be offloaded to `worker_threads` with a bounded `WorkerPool` pattern. An unbounded worker count exhausts OS threads.
- `async.queue` does not have a built-in capacity limit for the queue itself. Check `workQueue.length()` before pushing and reject when over a threshold.
- `bottleneck.schedule` returns a promise that resolves after the limiter runs the function. `bottleneck` supports `maxConcurrent` and `minTime` (rate limiting). Use `highWater` option to bound the queue.

## Tests to write
- `processBatch` with 100 items: assert at most `DB_CONCURRENCY` concurrent executions using a counter inside `processItem`.
- `enqueue` 201 tasks: assert the 201st throws `Error` with the backpressure message.
- `p-limit` active count: after pushing `DB_CONCURRENCY` tasks, assert `limit.activeCount === DB_CONCURRENCY` and `limit.pendingCount > 0`.
- Worker threads: spawn a bounded pool and submit CPU-bound tasks; assert the pool rejects when at max capacity.
