---
schema_version: 1
archetype: auth/jwt-handling
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.Authentication.JwtBearer
  acceptable:
    - System.IdentityModel.Tokens.Jwt
  avoid:
    - name: Manual base64 decode of JWT payload
      reason: Reads claims without verifying the signature — not authentication.
minimum_versions:
  dotnet: "10.0"
---

# JWT Handling — C#

## Library choice
`Microsoft.AspNetCore.Authentication.JwtBearer` is the first-party ASP.NET Core middleware for validating JWTs on incoming requests. `System.IdentityModel.Tokens.Jwt` (`JwtSecurityTokenHandler` / `JsonWebTokenHandler`) is the underlying handler used for issuance and for non-ASP.NET contexts. Both are Microsoft-maintained and integrate with the standard `IOptions` system. Do not use third-party JWT libraries when these cover the use case.

## Reference implementation
```csharp
// Issuance — TokenService.cs
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Security.Cryptography;

public sealed class TokenService(IOptions<JwtOptions> opts)
{
    private readonly JwtOptions _opts = opts.Value;

    public string Issue(string subject, IEnumerable<Claim> claims)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(_opts.PrivateKeyPem);
        var key = new RsaSecurityKey(rsa) { KeyId = _opts.KeyId };
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims.Append(new Claim(JwtRegisteredClaimNames.Sub, subject))),
            Expires = DateTime.UtcNow.AddMinutes(15),
            Issuer = _opts.Issuer,
            Audience = _opts.Audience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

// Validation — Program.cs (ASP.NET Core)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKeyResolver = (_, _, kid, _) => ResolveKey(kid),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
```

## Language-specific gotchas
- `JsonWebTokenHandler` (from `Microsoft.IdentityModel.JsonWebTokens`) is the modern replacement for `JwtSecurityTokenHandler`. Use it for new code — it returns `TokenValidationResult` rather than throwing exceptions.
- Set `ClockSkew` to 30 seconds or less. The default is 5 minutes, which extends effective token lifetime silently.
- `ValidAlgorithms` must be an explicit allowlist. Omitting it allows the handler to accept any algorithm present in the token header.
- Resolve the signing key by `kid` from a cached JWKS document rather than embedding the key in config — this makes rotation possible without a redeploy.
- Do not log the raw `Authorization` header. Log the validated `sub` claim only.

## Tests to write
- Valid token with correct `iss`, `aud`, `exp` → middleware sets `HttpContext.User` correctly.
- Expired token → `401` with `WWW-Authenticate: Bearer error="invalid_token"`.
- Token signed with the wrong key → `401`.
- Token with `alg: none` in the header → `401` (rejected by allowlist).
- Token with an unknown `kid` → key resolver returns nothing → `401`.
