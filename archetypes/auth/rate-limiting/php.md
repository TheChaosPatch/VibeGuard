---
schema_version: 1
archetype: auth/rate-limiting
language: php
principles_file: _principles.md
libraries:
  preferred: predis/predis or phpredis (for custom Redis counters)
  acceptable:
    - symfony/rate-limiter
    - Laravel RateLimiter (built-in)
  avoid:
    - name: APCu counters
      reason: Per-process cache — not shared across PHP-FPM workers or multiple web servers.
minimum_versions:
  php: "8.3"
---

# Rate Limiting and Brute Force Defense — PHP

## Library choice
Laravel's built-in `RateLimiter` facade (backed by the cache store, typically Redis) is the first choice for Laravel applications — it implements sliding windows and is configured in `RouteServiceProvider`. Symfony's `symfony/rate-limiter` with the `SlidingWindowLimiter` is the idiomatic choice for Symfony apps. For frameworks without built-in rate limiting, use `predis/predis` with Redis `INCR`+`EXPIRE` directly.

## Reference implementation
```php
<?php
declare(strict_types=1);

// Laravel approach — AppServiceProvider or RouteServiceProvider
use Illuminate\Cache\RateLimiting\Limit;
use Illuminate\Support\Facades\RateLimiter;

RateLimiter::for('login', function (Request $request) {
    // IP-level outer gate
    return [
        Limit::perMinutes(5, 100)->by($request->ip()),
        // Account-level gate — key on email from request body
        Limit::perMinutes(5, 5)->by(strtolower($request->input('email', ''))),
    ];
});

// Route registration
Route::post('/login', LoginController::class)->middleware('throttle:login');

// Custom Redis INCR approach (framework-agnostic)
class AccountRateLimiter
{
    private const LIMIT          = 5;
    private const WINDOW_SECONDS = 300;

    public function __construct(private readonly \Redis $redis) {}

    public function isAllowed(string $email): bool
    {
        $key   = 'login:fail:' . strtolower($email);
        $count = $this->redis->get($key);
        return $count === false || (int) $count < self::LIMIT;
    }

    public function recordFailure(string $email): void
    {
        $key   = 'login:fail:' . strtolower($email);
        $count = $this->redis->incr($key);
        if ($count === 1) {
            $this->redis->expire($key, self::WINDOW_SECONDS);
        }
    }

    public function clear(string $email): void
    {
        $this->redis->del('login:fail:' . strtolower($email));
    }
}
```

## Language-specific gotchas
- `$this->redis->get($key)` returns `false` when the key does not exist — not `null` or `0`. Always check `=== false` before casting to int.
- `phpredis` extension's `incr()` returns an integer directly. `predis` returns a `Predis\Response\Status` — cast to int explicitly.
- Laravel's `throttle` middleware reads rate limit results before the controller runs, so the request body is parsed. The `by(email)` key works because Laravel parses JSON and form bodies before middleware executes in the HTTP kernel.
- PHP-FPM spawns multiple worker processes that do not share APCu state. Redis (or Memcached) is mandatory for distributed rate limiting.
- Do not include the account's rate limit count in error responses — it reveals how close to the limit an attacker is. Return a generic `429 Too Many Requests` with `Retry-After`.

## Tests to write
- 5 failed attempts for the same email → 6th `isAllowed` returns `false`.
- `clear` resets the counter so `isAllowed` returns `true` again.
- `recordFailure` sets TTL on first call only — `ttl(key)` decreases on subsequent calls rather than resetting.
- Redis `get` returning `false` (key absent) → `isAllowed` returns `true`.
- Laravel middleware: 6th `POST /login` with the same email returns 429 with `Retry-After`.
