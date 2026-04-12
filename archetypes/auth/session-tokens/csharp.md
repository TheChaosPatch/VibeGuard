---
schema_version: 1
archetype: auth/session-tokens
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.DataProtection
  acceptable:
    - Microsoft.AspNetCore.Session
    - StackExchange.Redis
  avoid:
    - name: Custom token generation with Guid.NewGuid()
      reason: UUIDs provide 122 bits of entropy, not 128, and some implementations are not CSPRNG-backed.
    - name: System.Random for token generation
      reason: Not cryptographically secure. Predictable output enables session hijacking.
minimum_versions:
  dotnet: "10.0"
---

# Session Token Management -- C#

## Library choice
ASP.NET Core's built-in authentication cookie middleware (`AddCookie`) handles token generation, cookie flags, server-side ticket storage, and sliding expiration out of the box. The token is a Data-Protection-encrypted authentication ticket -- opaque, tamper-proof, and revocable when backed by a server-side `ITicketStore`. For distributed deployments, implement `ITicketStore` over Redis or a database so that logout on one node invalidates the session cluster-wide. Avoid rolling your own token format.

## Reference implementation
```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ITicketStore, DistributedTicketStore>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.Name = "__Host-Session";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(30);   // idle timeout
        o.SlidingExpiration = true;
        o.SessionStore = builder.Services
            .BuildServiceProvider().GetRequiredService<ITicketStore>();
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var issued = ctx.Properties.IssuedUtc ?? DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - issued > TimeSpan.FromHours(8)) // absolute timeout
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync();
            }
        };
    });

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/login", async (HttpContext ctx, LoginRequest req) =>
{
    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, req.UserId) };
    var identity = new ClaimsIdentity(claims, "pwd");
    await ctx.SignInAsync(new ClaimsPrincipal(identity));
    return Results.Ok();
});
```

## Language-specific gotchas
- `__Host-` cookie prefix enforces `Secure`, path `/`, and no `Domain` attribute at the browser level -- an extra layer of defense even if the code misconfigures the flags.
- `SlidingExpiration = true` with `ExpireTimeSpan` gives you idle timeout. The absolute timeout must be enforced in `OnValidatePrincipal` by checking `IssuedUtc`, as shown -- there is no built-in "absolute lifetime" property on the cookie scheme.
- Without a custom `ITicketStore`, the entire claims principal is serialized into the cookie. That makes the cookie large and, more importantly, un-revocable: "logout" only clears the client-side cookie. With `ITicketStore`, the cookie contains only an opaque key and the server is the source of truth.
- Call `HttpContext.SignOutAsync()` on logout -- it both clears the cookie and removes the server-side ticket. Deleting the cookie manually does not touch the store.
- `RandomNumberGenerator.GetBytes(32)` is the correct CSPRNG call if you ever need to generate a raw token (e.g., for an API session). Never `Guid.NewGuid().ToString()`.

## Tests to write
- Round-trip: login, make an authenticated request, confirm 200. Logout, repeat the same request, confirm 401.
- Idle timeout: login, advance the test clock past `ExpireTimeSpan`, confirm the next request gets 401.
- Absolute timeout: login, advance the clock past 8 hours but keep making requests within the idle window, confirm rejection.
- Session fixation: capture the session cookie before login, login, confirm the post-login cookie value differs from the pre-login value.
- Cookie flags: inspect the `Set-Cookie` header and assert `HttpOnly`, `Secure`, `SameSite=Lax`, and `__Host-` prefix are present.
