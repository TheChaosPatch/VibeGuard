---
schema_version: 1
archetype: http/content-security-policy
language: csharp
principles_file: _principles.md
libraries:
  preferred: NetEscapades.AspNetCore.SecurityHeaders
  acceptable:
    - Manual middleware (app.Use + IHttpContextAccessor)
  avoid:
    - name: NWebsec (legacy)
      reason: Unmaintained; does not support CSP Level 3 or nonce-based strict-dynamic.
minimum_versions:
  dotnet: "10.0"
---

# Content Security Policy — C#

## Library choice
`NetEscapades.AspNetCore.SecurityHeaders` provides a fluent builder API for all CSP directives, generates a cryptographically random nonce per request, and injects it into `IHttpContextAccessor` so Razor can access it via `@Context.GetNonce()`. For bare ASP.NET Core without the library, implement a middleware that writes `RandomNumberGenerator.GetBytes(16)` as a Base64 nonce into `HttpContext.Items`, then reads it when building the header. Do not set headers in individual controllers.

## Reference implementation
```csharp
// Program.cs
using NetEscapades.AspNetCore.SecurityHeaders;
using NetEscapades.AspNetCore.SecurityHeaders.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSecurityHeaderPolicies();

var app = builder.Build();

app.UseSecurityHeaders(policies =>
    policies.AddDefaultSecurityHeaders()
            .AddContentSecurityPolicy(csp =>
            {
                csp.AddDefaultSrc().None();
                csp.AddScriptSrc()
                   .WithNonce()
                   .AddStrictDynamic()
                   .UnsafeInline()   // fallback for CSP Level 2 (ignored when nonce present)
                   .OverHttps();     // https: fallback for very old browsers
                csp.AddStyleSrc().Self().WithNonce();
                csp.AddImgSrc().Self().Data();
                csp.AddFontSrc().Self();
                csp.AddConnectSrc().Self();
                csp.AddFormAction().Self();
                csp.AddFrameAncestors().None();
                csp.AddBaseUri().Self();
                csp.AddUpgradeInsecureRequests();
            })
);

app.MapRazorPages();
app.Run();
```

```razor
<%-- _Layout.cshtml — emit nonce on every inline script --%>
@inject IHttpContextAccessor HttpContextAccessor
@{
    var nonce = HttpContextAccessor.HttpContext?.GetNonce() ?? string.Empty;
}
<script nonce="@nonce">
    // inline bootstrap code
</script>
```

## Language-specific gotchas
- `WithNonce()` in `NetEscapades.AspNetCore.SecurityHeaders` generates a fresh `RandomNumberGenerator`-backed nonce per response — never reuse it across requests or cache the header.
- Tag Helpers that render `<script>` or `<link>` elements must propagate the nonce. The library provides `<script asp-add-nonce="true">` support; hand-rolled helpers need `HttpContext.GetNonce()`.
- `Content-Security-Policy-Report-Only` is a distinct header — use `AddContentSecurityPolicyReportOnly()` for the rollout phase. Both headers can coexist; use Report-Only for the next-stricter policy while enforcing the current one.
- Response caching (`[ResponseCache]` or output cache middleware) must be disabled or the `Vary` header configured to prevent serving a stale nonce from one user's response to another.
- Blazor Server's `<script>` tag injected by `_Host.cshtml` needs the nonce too; add it via `@Context.GetNonce()` in the render root.
- `'unsafe-inline'` in `script-src` is listed as a CSP Level 2 fallback; browsers that support `'nonce-*'` ignore `'unsafe-inline'` when a nonce is present — it is safe to include for compatibility but should not be used alone.

## Tests to write
- Middleware integration test: response headers contain `Content-Security-Policy` with a `nonce-` value that is base64 and at least 16 bytes.
- Two sequential requests produce different nonce values.
- Rendered HTML contains `<script nonce="X">` where `X` matches the nonce in the CSP header.
- `frame-ancestors 'none'` is present in the policy.
- `base-uri 'self'` and `form-action 'self'` are present in the policy.
- Report-Only header is absent in production configuration (enforcing mode).
