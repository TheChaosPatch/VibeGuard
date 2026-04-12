---
schema_version: 1
archetype: concurrency/resource-exhaustion
language: typescript
principles_file: _principles.md
libraries:
  preferred: p-limit
  acceptable:
    - bottleneck
  avoid:
    - name: Promise.all with unbounded array
      reason: Unbounded Promise.all exhausts connection pools and file descriptors; use p-limit to cap concurrency.
minimum_versions:
  node: "22"
  typescript: "5.7"
---

# Resource Exhaustion Prevention — TypeScript

## Library choice
`p-limit` with TypeScript generics provides a typed concurrency limiter. The type parameter flows through the wrapped function, preserving the return type. For complex rate-limiting scenarios, `bottleneck` has TypeScript declarations.

## Reference implementation
```typescript
import pLimit, { LimitFunction } from "p-limit";

const DB_CONCURRENCY = 20;
const QUEUE_CAPACITY = 200;

// Typed limiter — callers receive the correct return type.
const dbLimit: LimitFunction = pLimit(DB_CONCURRENCY);

export async function queryDb<T>(fn: () => Promise<T>): Promise<T> {
    return dbLimit(fn);
}

export async function processBatch<T>(
    items: readonly T[],
    processItem: (item: T) => Promise<void>,
    concurrency = DB_CONCURRENCY,
): Promise<void> {
    const limit = pLimit(concurrency);
    await Promise.all(items.map((item) => limit(() => processItem(item))));
}

// Simple in-memory bounded queue with backpressure.
export class BoundedQueue<T> {
    private readonly items: T[] = [];
    private readonly capacity: number;

    constructor(capacity = QUEUE_CAPACITY) {
        this.capacity = capacity;
    }

    enqueue(item: T): void {
        if (this.items.length >= this.capacity) {
            throw new Error(`Queue at capacity (${this.capacity}). Backpressure applied.`);
        }
        this.items.push(item);
    }

    dequeue(): T | undefined {
        return this.items.shift();
    }

    get size(): number { return this.items.length; }
}
```

## Language-specific gotchas
- TypeScript's `readonly` on the `items` parameter of `processBatch` prevents mutation of the input array, making the function safe to call with frozen arrays.
- `pLimit(0)` throws at runtime even though `number` is accepted by the type. Validate configuration values at startup.
- The `LimitFunction` type from `p-limit` is a generic function type. If you wrap it in a class or service, preserve the generics — avoid casting to `any`.
- `Promise.allSettled` with `p-limit` is useful when you want all items processed even if some fail: replace `Promise.all` with `Promise.allSettled` and inspect the `PromiseSettledResult` array.
- For Node.js `worker_threads`, define a typed `WorkerMessage` interface and validate messages in the worker with a type guard — worker threads communicate via structured clone, not type-safe channels.

## Tests to write
- `processBatch` with 100 items: count concurrent executions with an atomic-style counter; assert peak concurrency never exceeds the limit.
- `BoundedQueue.enqueue` beyond capacity throws `Error` with the backpressure message.
- TypeScript compile check: `queryDb<string>` with a `() => Promise<number>` callback is a type error.
- `pLimit(0)` throws — catch and assert in a unit test to document the runtime constraint.
