---
schema_version: 1
archetype: auth/rate-limiting
language: typescript
principles_file: _principles.md
libraries:
  preferred: express-rate-limit + rate-limit-redis
  acceptable:
    - "@fastify/rate-limit"
    - ioredis (for custom sliding window)
  avoid:
    - name: In-memory Map counters without distributed backing
      reason: Not shared across cluster workers or horizontal replicas.
minimum_versions:
  node: "22"
  typescript: "5.4"
---

# Rate Limiting and Brute Force Defense — TypeScript

## Library choice
Same stack as JavaScript: `express-rate-limit` + `rate-limit-redis` for IP-level limiting; custom Redis `INCR` for account-level limiting. TypeScript adds typed error classes and strongly-typed middleware signatures, making the rate limit surface auditable.

## Reference implementation
```typescript
import rateLimit from 'express-rate-limit';
import RedisStore from 'rate-limit-redis';
import { createClient, type RedisClientType } from 'redis';
import type { Request, Response, NextFunction } from 'express';

const redis: RedisClientType = createClient({ url: process.env.REDIS_URL! });
await redis.connect();

export class RateLimitError extends Error {
    readonly status = 429;
    constructor(readonly retryAfter: number) {
        super('Too many attempts');
    }
}

const ACCOUNT_LIMIT  = 5 as const;
const WINDOW_SECONDS = 300 as const;

export async function checkAccountLimit(email: string): Promise<void> {
    const key   = `login:fail:${email.toLowerCase()}`;
    const count = await redis.get(key);
    if (count !== null && parseInt(count, 10) >= ACCOUNT_LIMIT) {
        throw new RateLimitError(WINDOW_SECONDS);
    }
}

export async function recordFailure(email: string): Promise<void> {
    const key   = `login:fail:${email.toLowerCase()}`;
    const count = await redis.incr(key);
    if (count === 1) await redis.expire(key, WINDOW_SECONDS);
}

export async function clearFailures(email: string): Promise<void> {
    await redis.del(`login:fail:${email.toLowerCase()}`);
}

// Typed Express error handler for RateLimitError
export function rateLimitErrorHandler(
    err: unknown, _req: Request, res: Response, next: NextFunction
): void {
    if (err instanceof RateLimitError) {
        res.setHeader('Retry-After', String(err.retryAfter));
        res.status(429).json({ error: 'Too many attempts' });
        return;
    }
    next(err);
}
```

## Language-specific gotchas
- `RateLimitError` as a typed class lets TypeScript's `instanceof` narrow in error handlers, avoiding `(err as any).status` casts that bypass type safety.
- `process.env.REDIS_URL!` non-null assertion is a compile-time convenience but throws at runtime if the variable is missing. Use a config validation library (Zod, `envalid`) to validate env at startup.
- `as const` on `ACCOUNT_LIMIT` and `WINDOW_SECONDS` narrows them to literal types, which helps in comparison contexts and avoids accidental mutation.
- TypeScript does not add runtime guarantees — the Redis `INCR` atomicity requirement, proxy header trust, and TTL-on-first-increment logic are the same as in JavaScript.
- In NestJS, use `@nestjs/throttler` with a Redis storage adapter instead of raw `express-rate-limit`. It integrates with the NestJS DI container and supports guard-level configuration per endpoint.

## Tests to write
- `checkAccountLimit` resolves for a fresh key (no prior failures).
- `checkAccountLimit` throws `RateLimitError` after 5 `recordFailure` calls.
- `clearFailures` makes `checkAccountLimit` resolve again immediately.
- `rateLimitErrorHandler` sets `Retry-After` header and returns 429.
- Non-`RateLimitError` passes through to `next(err)` unchanged.
