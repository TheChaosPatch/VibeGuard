---
schema_version: 1
archetype: auth/rate-limiting
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.RateLimiting (built-in)
  acceptable:
    - AspNetCoreRateLimit (stefanprodan)
  avoid:
    - name: In-memory per-node counters without IDistributedCache
      reason: Each pod has independent state — limits are bypassed by sending to different pods.
minimum_versions:
  dotnet: "10.0"
---

# Rate Limiting and Brute Force Defense — C#

## Library choice
ASP.NET Core 7+ includes `Microsoft.AspNetCore.RateLimiting` in the framework — use it as the primary middleware. Back per-account counters with `IDistributedCache` (backed by Redis via `StackExchange.Redis`) so limits are enforced across all nodes. `AspNetCoreRateLimit` by stefanprodan predates the built-in middleware and adds IP-based and client-based policies; it remains useful for advanced scenarios.

## Reference implementation
```csharp
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

builder.Services.AddStackExchangeRedisCache(o =>
    o.Configuration = builder.Configuration["Redis:ConnectionString"]);

builder.Services.AddRateLimiter(opts =>
{
    opts.AddPolicy("login", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(5), SegmentsPerWindow = 5,
            PermitLimit = 20, QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0,
        });
    });
    opts.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.StatusCode = 429;
        ctx.HttpContext.Response.Headers.RetryAfter =
            ((int)(ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra) ? ra.TotalSeconds : 60)).ToString();
        await ctx.HttpContext.Response.WriteAsync("Too many requests.", ct);
    };
});

public async Task<IActionResult> Login([FromBody] LoginRequest req, [FromServices] IDistributedCache cache)
{
    var cacheKey = $"login:fail:{req.Email.ToLowerInvariant()}";
    var failCount = int.TryParse(await cache.GetStringAsync(cacheKey), out var n) ? n : 0;
    if (failCount >= 5)
        return StatusCode(429, new { retryAfter = 300 });
    var user = await _users.FindByEmailAsync(req.Email);
    if (user is null || !_hasher.Verify(req.Password, user.PasswordHash))
    {
        await cache.SetStringAsync(cacheKey, (failCount + 1).ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        return Unauthorized();
    }
    await cache.RemoveAsync(cacheKey);
    return Ok(await _tokens.IssueAsync(user));
}
```

## Language-specific gotchas
- ASP.NET Core's built-in rate limiter keys on values available in `HttpContext` at the middleware level — the account email is only in the request body, which isn't parsed yet. Layer middleware (IP) + handler-level (account) as shown.
- `IDistributedCache` with Redis provides atomic increment via `StackExchange.Redis` `IDatabase.StringIncrementAsync` — prefer this over get-parse-set for race safety.
- The `Retry-After` header value must be in seconds (integer). `MetadataName.RetryAfter` returns a `TimeSpan` — convert it.
- When deployed behind a reverse proxy (nginx, Azure Front Door), `RemoteIpAddress` will be the proxy IP. Configure `ForwardedHeadersOptions` to trust the proxy and use the `X-Forwarded-For` value.
- Do not reset the failure counter immediately on a successful login from the same IP — an attacker can interleave successful logins from known-good accounts to reset the counter and continue stuffing.

## Tests to write
- 5 failed login attempts for the same email → 6th returns 429.
- Successful login resets the per-account counter.
- 6th attempt from a different IP but same email still returns 429 (account-keyed gate).
- `Retry-After` header is present and non-zero on 429 responses.
- Counter in distributed cache expires after the configured window.
