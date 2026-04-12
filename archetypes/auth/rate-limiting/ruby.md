---
schema_version: 1
archetype: auth/rate-limiting
language: ruby
principles_file: _principles.md
libraries:
  preferred: rack-attack
  acceptable:
    - redis-rb (for custom sliding window)
  avoid:
    - name: In-memory Hash counters
      reason: Not shared across Puma workers or Unicorn processes.
minimum_versions:
  ruby: "3.3"
---

# Rate Limiting and Brute Force Defense — Ruby

## Library choice
`rack-attack` is the standard Rack middleware for rate limiting and blocking in Ruby web applications. It integrates with Rails, Sinatra, and any Rack app, and uses Redis as its backing store. It supports throttling (soft limit, 429 response) and blocking (hard ban). Configure it once in an initializer; it applies across all requests before the framework router.

## Reference implementation
```ruby
# config/initializers/rack_attack.rb
class Rack::Attack
  # Redis-backed cache — shared across all Puma workers and dynos
  Rack::Attack.cache.store = ActiveSupport::Cache::RedisCacheStore.new(
    url: ENV.fetch('REDIS_URL')
  )

  # 1. IP-level outer gate: 100 login attempts per IP per 5 minutes
  throttle('login/ip', limit: 100, period: 5.minutes) do |req|
    req.ip if req.path == '/login' && req.post?
  end

  # 2. Account-level gate: 5 attempts per email per 5 minutes
  throttle('login/email', limit: 5, period: 5.minutes) do |req|
    if req.path == '/login' && req.post?
      email = req.params['email'].to_s.downcase.strip
      email unless email.empty?
    end
  end

  # 3. Password reset: 5 requests per email per 5 minutes
  throttle('password_reset/email', limit: 5, period: 5.minutes) do |req|
    if req.path == '/password/reset' && req.post?
      req.params['email'].to_s.downcase.strip
    end
  end

  # Return 429 with Retry-After
  self.throttled_responder = lambda do |env|
    match_data = env['rack.attack.match_data']
    now        = match_data[:epoch_time]
    period     = match_data[:period]
    retry_after = (period - (now % period)).ceil

    [429, { 'Content-Type' => 'application/json', 'Retry-After' => retry_after.to_s },
     [{ error: 'Too many requests' }.to_json]]
  end
end
```

## Language-specific gotchas
- `req.params['email']` parses the request body — `rack-attack` runs before Rails controllers, so Rack parses `application/x-www-form-urlencoded` and JSON body parameters differently. For JSON APIs, `JSON.parse(req.body.read)['email']` is needed; reset `req.body` with `req.body.rewind` after reading.
- `ActiveSupport::Cache::RedisCacheStore` is the Rails-idiomatic Redis client. It handles connection pooling, serialization, and reconnection. Do not use a raw `Redis` object with `cache.store=` unless you wrap it in a cache store adapter.
- `Rack::Attack` uses sliding windows internally when the period does not align with clock boundaries. The throttle block runs for every request — keep it fast (no database calls).
- Test `rack-attack` throttles with `Rack::Attack.enabled = true` in the test environment and `Rack::MockRequest` to simulate requests without starting a real server.
- Do not expose the `rack.attack.match_data` hash values in API responses — they reveal internal limit configuration.

## Tests to write
- 5 POST `/login` requests with the same email → 6th returns 429 with `Retry-After`.
- 6th request from a different IP but same email still returns 429.
- After the throttle period, the 6th request returns 401 (not 429).
- `throttled_responder` sets `Content-Type: application/json` and `Retry-After` header.
- Password reset endpoint is also throttled separately.
