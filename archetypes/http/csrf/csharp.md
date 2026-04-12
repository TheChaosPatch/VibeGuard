---
schema_version: 1
archetype: http/csrf
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.Antiforgery
  acceptable:
    - IAntiforgery (manual validation for APIs)
  avoid:
    - name: Custom token generation
      reason: The built-in system handles token lifecycle, cookie binding, and validation; reimplementing it introduces subtle timing and entropy bugs.
minimum_versions:
  dotnet: "10.0"
---

# Cross-Site Request Forgery Defense — C#

## Library choice
ASP.NET Core's built-in antiforgery system (`Microsoft.AspNetCore.Antiforgery`) is the answer. For Razor Pages and MVC, it works out of the box: `@Html.AntiForgeryToken()` in the form and `[ValidateAntiForgeryToken]` on the action (or the global `AutoValidateAntiforgeryToken` filter). For Minimal APIs consumed by SPAs with cookie auth, inject `IAntiforgery` and validate the token from a custom header. For APIs authenticated purely by bearer tokens, exclude them from CSRF protection entirely — they are not vulnerable.

## Reference implementation
```csharp
using Microsoft.AspNetCore.Antiforgery;

// Program.cs — configure antiforgery and cookie policy.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "__Host-XSRF";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
});

var app = builder.Build();

// Middleware: validate antiforgery on all state-changing requests
// that use cookie auth. Skip safe methods and token-auth endpoints.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method)
        || HttpMethods.IsHead(context.Request.Method)
        || HttpMethods.IsOptions(context.Request.Method))
    {
        await next(context);
        return;
    }
    // Skip endpoints that use bearer-token auth (not CSRF-vulnerable).
    var endpoint = context.GetEndpoint();
    if (endpoint?.Metadata.GetMetadata<IgnoreAntiforgeryTokenAttribute>() is not null)
    {
        await next(context);
        return;
    }
    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
    if (!await antiforgery.IsRequestValidAsync(context))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    await next(context);
});
```

## Language-specific gotchas
- `[AutoValidateAntiforgeryToken]` as a global filter covers all MVC/Razor POST actions by default. Prefer this over decorating each action individually — missing one action is how CSRF bugs happen.
- The `__Host-` cookie prefix enforces `Secure`, `Path=/`, and no `Domain` attribute. Use it for the antiforgery cookie to prevent subdomain attacks.
- `SameSite=Strict` on the antiforgery cookie is safe because the cookie is not the session cookie — it is only the CSRF token store. The session cookie can remain `Lax` for usability.
- For SPAs: send the antiforgery token via a non-HttpOnly cookie or a dedicated endpoint, and have the SPA read it and include it in the `X-XSRF-TOKEN` header on state-changing requests.
- Blazor Server uses SignalR (WebSocket), which is not CSRF-vulnerable for form posts. But Blazor endpoints that accept standard HTTP POST still need antiforgery — `EditForm` includes it by default.
- `IAntiforgery.GetAndStoreTokens()` generates and sets the cookie in one call. Call it on GET requests that serve forms, so the token is ready before the POST.

## Tests to write
- POST without a CSRF token returns 400.
- POST with a valid CSRF token returns the expected success response.
- POST with a tampered or expired CSRF token returns 400.
- GET request is not blocked by the antiforgery middleware.
- An endpoint decorated with `[IgnoreAntiforgeryToken]` accepts POST without a token.
