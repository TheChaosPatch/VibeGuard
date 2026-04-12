---
schema_version: 1
archetype: http/cors
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.Cors
  acceptable:
    - Custom middleware (for advanced origin matching)
  avoid:
    - name: Setting Access-Control headers manually in controllers
      reason: Decentralized CORS handling is inconsistent and misses preflight, error responses, and new endpoints.
minimum_versions:
  dotnet: "10.0"
---

# Cross-Origin Resource Sharing Configuration — C#

## Library choice
ASP.NET Core's built-in CORS middleware (`Microsoft.AspNetCore.Cors`) is the answer. It supports named policies, per-endpoint overrides via `[EnableCors]` / `[DisableCors]`, preflight caching, and credentialed requests. Configure it in `Program.cs` with explicit origin allowlists — never use `.AllowAnyOrigin()` with `.AllowCredentials()`. For complex origin matching (e.g., subdomains of a base domain), use `.SetIsOriginAllowed()` with a carefully anchored check, not a loose substring match.

## Reference implementation
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    // Named policy for the SPA frontend.
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(
                "https://app.example.com",
                "https://staging.example.com")
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Content-Type", "Authorization", "X-Request-ID")
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
    });

    // Restrictive policy for public, read-only endpoints.
    options.AddPolicy("PublicReadOnly", policy =>
    {
        policy.AllowAnyOrigin()
            .WithMethods("GET", "HEAD")
            .WithHeaders("Content-Type")
            .SetPreflightMaxAge(TimeSpan.FromHours(1));
        // No AllowCredentials — wildcard origin is safe here.
    });
});

var app = builder.Build();

// Apply the default CORS policy globally.
app.UseCors("Frontend");

// Per-endpoint override for public APIs:
// app.MapGet("/api/public/status", () => "ok").RequireCors("PublicReadOnly");
```

## Language-specific gotchas
- `AllowAnyOrigin()` and `AllowCredentials()` together throw an `InvalidOperationException` at startup in ASP.NET Core. This is the framework protecting you — do not work around it by reflecting the origin manually.
- `app.UseCors()` must be placed after `app.UseRouting()` and before `app.UseAuthorization()`. Incorrect ordering causes CORS headers to be missing on certain responses.
- `SetIsOriginAllowed(origin => origin.EndsWith(".example.com"))` is tempting for subdomain matching but dangerous — `evil-example.com` also ends with `.example.com`. Use `Uri` parsing and compare the registered domain: `new Uri(origin).Host == "example.com" || new Uri(origin).Host.EndsWith(".example.com", StringComparison.Ordinal)` with explicit scheme and port checks.
- ASP.NET Core's CORS middleware automatically adds `Vary: Origin` when the policy is origin-specific. Verify this with integration tests — a CDN without `Vary: Origin` can cache one origin's response and serve it to another.
- Preflight responses (OPTIONS) are handled entirely by the middleware. If you have a custom OPTIONS handler, it may conflict with the CORS middleware and produce missing or duplicate headers.
- `[DisableCors]` on an endpoint removes CORS headers entirely — the browser blocks cross-origin access. Use this for admin or internal endpoints.

## Tests to write
- Request from an allowed origin includes `Access-Control-Allow-Origin` matching that origin.
- Request from an unlisted origin does not include `Access-Control-Allow-Origin`.
- Preflight OPTIONS request returns the correct `Access-Control-Allow-Methods` and `Access-Control-Max-Age`.
- `Vary: Origin` is present on responses to cross-origin requests.
- Credentialed request from an allowed origin succeeds; credentialed request from an unlisted origin is blocked.
