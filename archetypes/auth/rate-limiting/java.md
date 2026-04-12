---
schema_version: 1
archetype: auth/rate-limiting
language: java
principles_file: _principles.md
libraries:
  preferred: bucket4j (with Redis backend)
  acceptable:
    - resilience4j-ratelimiter
    - Spring Boot + spring-boot-starter-data-redis (custom INCR)
  avoid:
    - name: Guava RateLimiter
      reason: In-process token bucket only — not distributed across JVM instances.
minimum_versions:
  java: "21"
---

# Rate Limiting and Brute Force Defense — Java

## Library choice
`bucket4j` implements token bucket and sliding window algorithms with first-class Redis integration via Lettuce or Redisson, making it distributed by default. `resilience4j-ratelimiter` is an alternative that integrates well with Spring Boot Actuator metrics. For account-level limiting that requires per-email counters, use Spring Data Redis with `RedisTemplate.opsForValue().increment()` wrapped in a custom service.

## Reference implementation
```java
@Service
@RequiredArgsConstructor
public class LoginRateLimiter {

    private static final int    ACCOUNT_LIMIT  = 5;
    private static final long   WINDOW_SECONDS = 300L;
    private static final String KEY_PREFIX     = "login:fail:";

    private final StringRedisTemplate redis;

    public void checkAccountLimit(String email) {
        String key   = KEY_PREFIX + email.toLowerCase();
        String count = redis.opsForValue().get(key);
        if (count != null && Integer.parseInt(count) >= ACCOUNT_LIMIT) {
            throw new TooManyAttemptsException(WINDOW_SECONDS);
        }
    }

    public void recordFailure(String email) {
        String key   = KEY_PREFIX + email.toLowerCase();
        Long   count = redis.opsForValue().increment(key);
        if (Long.valueOf(1).equals(count)) {
            redis.expire(key, Duration.ofSeconds(WINDOW_SECONDS));
        }
    }

    public void clearFailures(String email) {
        redis.delete(KEY_PREFIX + email.toLowerCase());
    }
}

// LoginController.java
@PostMapping("/login")
public ResponseEntity<?> login(@RequestBody @Valid LoginRequest req) {
    rateLimiter.checkAccountLimit(req.email());
    var user = users.findByEmail(req.email())
        .orElseThrow(() -> { rateLimiter.recordFailure(req.email()); return new UnauthorizedException(); });
    if (!encoder.matches(req.password(), user.getPasswordHash())) {
        rateLimiter.recordFailure(req.email());
        return ResponseEntity.status(401).build();
    }
    rateLimiter.clearFailures(req.email());
    return ResponseEntity.ok(tokens.issue(user));
}
```

## Language-specific gotchas
- `StringRedisTemplate.opsForValue().increment(key)` executes Redis `INCR` — it is atomic. The `expire` call must happen in the same Redis transaction or conditionally on count == 1, as shown. Use a Lua script or `MULTI/EXEC` for strict atomicity across `INCR` and `EXPIRE`.
- `TooManyAttemptsException` should extend `RuntimeException` and be mapped to `429` via a `@ExceptionHandler` in `@ControllerAdvice`. Set `Retry-After` in the response header there.
- `StringRedisTemplate` uses Java `String` serialization — the key and value are stored as UTF-8 strings. Do not mix with a `RedisTemplate<String, Object>` that uses Java serialization for the value, or you will get deserialization errors.
- Behind a Spring Cloud Gateway or nginx reverse proxy, configure `server.forward-headers-strategy=framework` in `application.properties` so Spring sees the real client IP in `X-Forwarded-For`.
- `bucket4j` with the Redis backend uses a compare-and-swap loop for atomic counter updates — it is safe under concurrent load without explicit `MULTI/EXEC`. Use it when you need strict token bucket semantics rather than the simpler `INCR` approach.

## Tests to write
- `checkAccountLimit` succeeds for an email with no prior failures.
- After 5 `recordFailure` calls, `checkAccountLimit` throws `TooManyAttemptsException`.
- `clearFailures` allows `checkAccountLimit` to succeed again.
- `recordFailure` sets a TTL on the first call and does not reset it on subsequent calls.
- `POST /login` with a valid credential returns 200 and clears the counter.
