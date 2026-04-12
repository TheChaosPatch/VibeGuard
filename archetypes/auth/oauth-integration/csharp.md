---
schema_version: 1
archetype: auth/oauth-integration
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.Authentication.OpenIdConnect
  acceptable:
    - Microsoft.Identity.Web
  avoid:
    - name: Manual HTTP calls to the token endpoint
      reason: Mishandling PKCE, state, nonce, or token validation is near-certain without a vetted library.
    - name: Storing tokens in browser-accessible cookies without encryption
      reason: Token theft via XSS. Use Data Protection or server-side storage.
minimum_versions:
  dotnet: "10.0"
---

# OAuth 2.0 / OIDC Client Integration -- C#

## Library choice
`Microsoft.AspNetCore.Authentication.OpenIdConnect` is the framework-integrated OIDC client. It handles the authorization code flow with PKCE, state parameter, nonce, ID token validation, and token refresh. `Microsoft.Identity.Web` builds on top of it for Entra ID (Azure AD) with opinionated defaults for scope management and token caching. Both are maintained by Microsoft and participate in the ASP.NET Core authentication pipeline. Never hand-roll the redirect/callback/token-exchange cycle.

## Reference implementation
```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(o =>
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.Name = "__Host-Auth";
    })
    .AddOpenIdConnect(o =>
    {
        o.Authority = builder.Configuration["Oidc:Authority"];
        o.ClientId = builder.Configuration["Oidc:ClientId"];
        o.ClientSecret = builder.Configuration["Oidc:ClientSecret"];
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.UsePkce = true;
        o.SaveTokens = true;
        o.Scope.Clear(); o.Scope.Add("openid"); o.Scope.Add("email");
        o.MapInboundClaims = false;
        o.Events.OnRedirectToIdentityProvider = ctx =>
        {
            ctx.ProtocolMessage.RedirectUri =
                $"{ctx.Request.Scheme}://{ctx.Request.Host}/signin-oidc";
            return Task.CompletedTask;
        };
    });

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/me", (HttpContext ctx) =>
    Results.Ok(new { sub = ctx.User.FindFirst("sub")?.Value })).RequireAuthorization();
app.Run();
```

## Language-specific gotchas
- `UsePkce = true` is the critical line. It is `true` by default since .NET 7, but set it explicitly so the intent is visible in review and survives copy-paste to older targets.
- `SaveTokens = true` stores the access, refresh, and ID tokens in the authentication ticket (cookie). If you use a server-side `ITicketStore`, the tokens stay on the server. Without `ITicketStore`, they travel in the encrypted cookie on every request -- large and harder to revoke.
- `Scope.Clear()` before adding scopes prevents inheriting defaults that request more than you need. The middleware adds `openid` and `profile` by default; explicitly controlling scope is the principle of least privilege.
- `MapInboundClaims = false` preserves the provider's original claim names (`sub`, `email`) instead of mapping them to verbose WS-Federation URIs. This avoids a class of bugs where `ClaimTypes.NameIdentifier` does not match `sub`.
- The `OnRedirectToIdentityProvider` event hardens the redirect URI to exactly one value. Never allow the callback URL to be influenced by a user-supplied parameter -- that is an open redirect leading to token theft.
- To refresh tokens, call `HttpContext.GetTokenAsync("refresh_token")` and use the OIDC library's token refresh mechanism. Never build your own token endpoint HTTP call.

## Tests to write
- PKCE enforcement: intercept the authorization redirect and confirm `code_challenge` and `code_challenge_method=S256` are present in the query string.
- State parameter: intercept the redirect, confirm `state` is present. Replay the callback with a modified `state` value and confirm rejection.
- Redirect URI: confirm the authorization URL contains the exact registered callback URI with no user-controllable suffix.
- ID token validation: supply a callback with a forged ID token (wrong issuer, wrong audience, expired) and confirm each is rejected with 401.
- Scope minimality: intercept the redirect and confirm only `openid` and `email` appear in the `scope` parameter.
- Token storage: after login, confirm no tokens appear in browser-accessible storage (no `localStorage`, no non-HttpOnly cookies).
