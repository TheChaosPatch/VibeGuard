---
schema_version: 1
archetype: http/security-headers
language: csharp
principles_file: _principles.md
libraries:
  preferred: ASP.NET Core middleware (custom or NetEscapades.AspNetCore.SecurityHeaders)
  acceptable:
    - NWebsec
  avoid:
    - name: Setting headers in individual controllers
      reason: Decentralized header management guarantees gaps when new endpoints are added.
minimum_versions:
  dotnet: "10.0"
---

# HTTP Security Headers — C#

## Library choice
ASP.NET Core's middleware pipeline is the natural place for security headers. You can write a simple custom middleware (15 lines) or use `NetEscapades.AspNetCore.SecurityHeaders` for a fluent configuration API with CSP nonce support. `NWebsec` is an older alternative that still works. The key is that headers are set once in `Program.cs`, not in individual controllers. Also call `app.UseHsts()` (built-in) for Strict-Transport-Security, and suppress the `Server` header via Kestrel options.

## Reference implementation
```csharp
var builder = WebApplication.CreateBuilder(args);

// Suppress the Server header at the Kestrel level.
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

var app = builder.Build();

// Security headers middleware — runs on every response.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    headers["Content-Security-Policy"] = string.Join("; ",
        "default-src 'none'",
        "script-src 'self'",
        "style-src 'self'",
        "img-src 'self'",
        "connect-src 'self'",
        "font-src 'self'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'");

    // Remove headers that leak stack information.
    headers.Remove("X-Powered-By");
    await next(context);
});

// HSTS — built-in middleware, only for production HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
// ... remaining middleware
```

## Language-specific gotchas
- `app.UseHsts()` must come before `app.UseHttpsRedirection()` in the pipeline. HSTS tells the browser to upgrade future requests; the redirect handles the current one.
- Kestrel sets a `Server: Kestrel` header by default. `AddServerHeader = false` suppresses it. If you are behind IIS or nginx, also configure those to suppress their own `Server` headers.
- ASP.NET Core's default `app.UseHsts()` sets `max-age=2592000` (30 days). Override it with `builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; })` for production-grade HSTS.
- For CSP nonces (needed for inline scripts), generate a per-request nonce with `RandomNumberGenerator.GetHexString(32)`, add it to the CSP header as `script-src 'self' 'nonce-{value}'`, and pass it to Razor via `HttpContext.Items` or a Tag Helper.
- The `X-Frame-Options` header is ignored if CSP `frame-ancestors` is also set in modern browsers. Set both for backward compatibility, but `frame-ancestors` is the authoritative directive.
- `Content-Security-Policy-Report-Only` can coexist with `Content-Security-Policy`. Use Report-Only to test a stricter policy while enforcing the current one.

## Tests to write
- Every response (200, 404, 500) includes `X-Content-Type-Options: nosniff`.
- Every response includes `Content-Security-Policy` with `default-src 'none'`.
- HTTPS responses include `Strict-Transport-Security` with `max-age >= 31536000`.
- No response includes a `Server` or `X-Powered-By` header.
- `X-Frame-Options: DENY` is present on all responses.
