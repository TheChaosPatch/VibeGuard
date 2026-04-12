---
schema_version: 1
archetype: auth/rate-limiting
language: python
principles_file: _principles.md
libraries:
  preferred: slowapi
  acceptable:
    - limits (underlying library used by slowapi)
    - redis-py (for custom sliding window)
  avoid:
    - name: In-process dict counters
      reason: State is not shared across workers/processes — limits are bypassed by Gunicorn multi-worker setups.
minimum_versions:
  python: "3.10"
---

# Rate Limiting and Brute Force Defense — Python

## Library choice
`slowapi` wraps the `limits` library and integrates with FastAPI and Starlette as middleware. It supports sliding windows, fixed windows, and token buckets, backed by Redis. For Flask, `flask-limiter` (which also uses `limits`) is the idiomatic choice. For custom per-account logic not covered by decorator-based rate limiting, use `redis-py` with `INCR` + `EXPIRE` for atomic counters.

## Reference implementation
```python
import redis.asyncio as redis
from fastapi import FastAPI, HTTPException, Request
from slowapi import Limiter
from slowapi.util import get_remote_address

# IP-level outer gate via slowapi
limiter = Limiter(key_func=get_remote_address, storage_uri="redis://localhost:6379")
app = FastAPI()
app.state.limiter = limiter

REDIS_CLIENT = redis.from_url("redis://localhost:6379", decode_responses=True)
ACCOUNT_LIMIT = 5
WINDOW_SECONDS = 300  # 5 minutes

async def check_account_limit(email: str) -> None:
    key   = f"login:fail:{email.lower()}"
    count = await REDIS_CLIENT.get(key)
    if count and int(count) >= ACCOUNT_LIMIT:
        raise HTTPException(status_code=429, headers={"Retry-After": str(WINDOW_SECONDS)})

async def record_failure(email: str) -> None:
    key   = f"login:fail:{email.lower()}"
    count = await REDIS_CLIENT.incr(key)
    if count == 1:
        await REDIS_CLIENT.expire(key, WINDOW_SECONDS)

async def clear_failures(email: str) -> None:
    await REDIS_CLIENT.delete(f"login:fail:{email.lower()}")

@app.post("/login")
@limiter.limit("30/5minutes")  # IP-level outer gate
async def login(request: Request, body: LoginRequest, user_repo, hasher, token_svc):
    await check_account_limit(body.email)

    user = await user_repo.get_by_email(body.email)
    if user is None or not hasher.verify(body.password, user.password_hash):
        await record_failure(body.email)
        raise HTTPException(status_code=401)

    await clear_failures(body.email)
    return {"access_token": await token_svc.issue(user.id)}
```

## Language-specific gotchas
- `REDIS_CLIENT.incr(key)` is atomic — it returns the new value after increment. Using `GET` + `SET` is a TOCTOU race; always use `INCR` for counter increments.
- Set the TTL (`EXPIRE`) only on the first increment (`count == 1`). Resetting the TTL on every failure extends the window with each attempt — a sliding window that grows. Use a separate TTL strategy if you want strict sliding window semantics.
- `slowapi` reads the IP from `get_remote_address`, which uses `request.client.host`. Behind a proxy, configure `TrustedHostMiddleware` or use `X-Forwarded-For` with a trusted proxy allowlist.
- FastAPI's background task system (`BackgroundTasks`) should not be used for rate-limit writes — they run after the response is sent, creating a window where a second request sees the old counter.
- Return `Retry-After` in seconds as an integer string in the header. RFC 7231 defines both date and delta-seconds formats; clients universally support delta-seconds.

## Tests to write
- 5 failed logins for the same email → 6th returns 429 with `Retry-After` header.
- Successful login clears the per-account counter.
- 6th attempt from a new IP but same email still returns 429.
- Redis key expires after `WINDOW_SECONDS` — counter resets.
- `INCR` is atomic — concurrent requests do not skip the limit.
