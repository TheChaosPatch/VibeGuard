---
schema_version: 1
archetype: auth/api-endpoint-authentication
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.Authentication.JwtBearer
  acceptable:
    - Microsoft.AspNetCore.Authentication.OpenIdConnect
    - Microsoft.Identity.Web
  avoid:
    - name: Custom middleware that parses JWTs by hand
      reason: Signature verification, clock-skew, and key-rotation bugs are too easy to introduce.
    - name: Per-endpoint [Authorize] as the only defense
      reason: A forgotten attribute is an unauthenticated endpoint in production.
minimum_versions:
  dotnet: "10.0"
---

# API Endpoint Authentication — C#

## Library choice
`Microsoft.AspNetCore.Authentication.JwtBearer` is the stock answer and it composes with the ASP.NET Core authorization pipeline the way everything else in the framework expects. Use `AddAuthentication().AddJwtBearer(...)` to register the scheme, then configure the authorization policy so the *default* (fallback) policy requires an authenticated user. That single line is what flips the codebase from opt-in to opt-out. `Microsoft.Identity.Web` is the right upgrade if you're on Entra ID and want token acquisition helpers; `OpenIdConnect` is the right choice for interactive sign-in flows. All three share the same `FallbackPolicy` hook, which is the mechanism that matters.

## Reference implementation
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.Authority = builder.Configuration["Auth:Authority"];
        o.Audience = builder.Configuration["Auth:Audience"];
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

// Authenticated by default — no attribute required.
app.MapGet("/orders/{id:guid}", (Guid id, ClaimsPrincipal user) =>
    Results.Ok(new { id, caller = user.FindFirstValue(ClaimTypes.NameIdentifier) }));

// Explicit opt-out, on the documented public allowlist.
app.MapGet("/health", () => Results.Ok("ok")).AllowAnonymous();

app.Run();
```

## Language-specific gotchas
- `SetFallbackPolicy` is the line that makes authentication the default. Without it, endpoints with no `[Authorize]` attribute are *anonymous*, which is the opposite of what you want. Grep for it in review — it should appear exactly once.
- `[AllowAnonymous]` is the only sanctioned opt-out. Keep the set of `.AllowAnonymous()` calls on a single `PublicEndpoints` static class (or an extension method) so a reviewer can audit the whole allowlist in one file.
- `ClockSkew = TimeSpan.FromSeconds(30)` is deliberate. The default is five minutes, which is surprisingly generous for short-lived access tokens. Tighten it.
- Never read `HttpContext.Request.Headers["Authorization"]` yourself to "check if the user sent a token." The middleware has already done this and has populated `HttpContext.User`. Read the claim.
- If you're behind a load balancer that terminates TLS and forwards an `X-Forwarded-*` header, configure `ForwardedHeadersOptions` with `KnownProxies` or `KnownNetworks` so the header is only honored from the actual load balancer. Otherwise a caller can spoof it.
- `RequireAuthorization()` on a route group is how you layer *additional* policies (roles, scopes) on top of the fallback. Use it for authorization, not authentication — the fallback already ensures the user is authenticated.

## Tests to write
- Unauthenticated request to an authenticated endpoint returns 401 with an empty body — not 404, not 500, not a handler-generated response.
- Request with an expired JWT returns 401 — add a test that signs a token with `NotBefore`/`Expires` in the past and confirms the middleware rejects it before the handler runs.
- Request with a JWT signed by the wrong key returns 401 — swap the signing key between issuer and verifier and confirm rejection.
- Request with a valid JWT but wrong `aud` returns 401.
- `AllowAnonymous` allowlist test: enumerate every endpoint with `.AllowAnonymous()` and assert the set matches a hardcoded expected list, so adding a new public endpoint requires updating the test (forcing review).
- Handler never runs on a 401 — add a handler that throws if invoked, point an unauthenticated request at it, confirm 401 and no exception.
