---
schema_version: 1
archetype: auth/rate-limiting
language: javascript
principles_file: _principles.md
libraries:
  preferred: express-rate-limit + rate-limit-redis
  acceptable:
    - "@fastify/rate-limit"
    - ioredis (for custom sliding window)
  avoid:
    - name: In-memory Map/object counters
      reason: Not shared across Node.js cluster workers or multiple instances.
minimum_versions:
  node: "22"
---

# Rate Limiting and Brute Force Defense — JavaScript

## Library choice
`express-rate-limit` is the standard rate limiting middleware for Express, and `rate-limit-redis` provides a Redis store so limits are enforced across all instances. `@fastify/rate-limit` is the equivalent for Fastify. For account-level (per-email) limiting that requires reading the request body, apply a custom Redis-backed middleware after the body parser.

## Reference implementation
```js
import rateLimit from 'express-rate-limit';
import RedisStore from 'rate-limit-redis';
import { createClient } from 'redis';

const redis = createClient({ url: process.env.REDIS_URL });
await redis.connect();

export const ipLimiter = rateLimit({
    windowMs: 5 * 60 * 1000, limit: 100,
    standardHeaders: 'draft-7', legacyHeaders: false,
    store: new RedisStore({ sendCommand: (...args) => redis.sendCommand(args) }),
});

const ACCOUNT_LIMIT = 5;
const WINDOW_SECONDS = 300;

export async function checkAccountLimit(email) {
    const key = `login:fail:${email.toLowerCase()}`;
    const count = await redis.get(key);
    if (count !== null && parseInt(count, 10) >= ACCOUNT_LIMIT) {
        const err = new Error('Too many attempts');
        err.status = 429;
        err.retryAfter = WINDOW_SECONDS;
        throw err;
    }
}

export async function recordFailure(email) {
    const key = `login:fail:${email.toLowerCase()}`;
    const count = await redis.incr(key);
    if (count === 1) await redis.expire(key, WINDOW_SECONDS);
}

export async function clearFailures(email) {
    await redis.del(`login:fail:${email.toLowerCase()}`);
}

app.post('/login', ipLimiter, async (req, res) => {
    await checkAccountLimit(req.body.email);
    const user = await users.findByEmail(req.body.email);
    if (!user || !await argon2.verify(user.passwordHash, req.body.password)) {
        await recordFailure(req.body.email);
        return res.status(401).json({ error: 'Invalid credentials' });
    }
    await clearFailures(req.body.email);
    res.json({ accessToken: await issueToken(user.id) });
});
```

## Language-specific gotchas
- `redis.incr(key)` is atomic. `redis.get` + `redis.set` is not — use `incr` for counters.
- Set the TTL only when `count === 1` (first failure). Resetting TTL on every `incr` extends the window with each attempt.
- `express-rate-limit` with `standardHeaders: 'draft-7'` sends `RateLimit-Policy`, `RateLimit`, and `RateLimit-Reset` headers following the IETF draft. `legacyHeaders: false` suppresses the older `X-RateLimit-*` headers.
- The Redis store's `sendCommand` pattern avoids a direct dependency on the internal client API and works with both `node-redis` v4 and `ioredis`.
- Behind a reverse proxy, set `app.set('trust proxy', 1)` in Express so `req.ip` reflects the `X-Forwarded-For` client IP, not the proxy IP. Without it, all requests appear to originate from the proxy — one limit for all clients.

## Tests to write
- 5 failed logins for the same email → 6th returns 429 with `Retry-After`.
- Successful login clears the per-account counter.
- 6th attempt from a different IP but same email still returns 429.
- Redis key TTL is set on first failure and not extended on subsequent failures.
- `clearFailures` makes the 6th attempt return 401 (not 429) again.
