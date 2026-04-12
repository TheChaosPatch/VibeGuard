---
schema_version: 1
archetype: auth/password-reset
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.Identity (built-in token provider)
  acceptable:
    - Custom CSPRNG token + EF Core token store
  avoid:
    - name: System.Guid.NewGuid()
      reason: Version 4 UUID has 122 bits of entropy but is not from CSPRNG in all runtimes; prefer RandomNumberGenerator.
minimum_versions:
  dotnet: "10.0"
---

# Secure Password Reset — C#

## Library choice
ASP.NET Core Identity's `UserManager<T>.GeneratePasswordResetTokenAsync` is the built-in solution — it produces a data-protection token that embeds a timestamp, is single-use, and is tied to the user's current security stamp. For applications not using Identity, use `RandomNumberGenerator.GetBytes(32)` to generate the raw token and store its SHA-256 hash in the database via EF Core.

## Reference implementation
```csharp
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

public sealed class PasswordResetService(AppDbContext db, IPasswordHasher hasher, IEmailSender email)
{
    private const int TokenBytes = 32;
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(30);

    public async Task RequestResetAsync(string emailAddress)
    {
        // Uniform response: always complete without revealing whether the email exists
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == emailAddress);
        if (user is null) return;

        var rawToken = RandomNumberGenerator.GetBytes(TokenBytes);
        var tokenHash = Convert.ToHexString(SHA256.HashData(rawToken));

        await db.PasswordResetTokens.AddAsync(new PasswordResetToken
        {
            UserId    = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.Add(Expiry),
        });
        await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && !t.Consumed && t.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Consumed, true));

        await db.SaveChangesAsync();
        var link = $"https://app.example.com/reset?token={Convert.ToHexString(rawToken)}";
        await email.SendResetLinkAsync(user.Email, link);
    }

    public async Task<bool> RedeemAsync(string rawToken, string newPassword)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Convert.FromHexString(rawToken)));
        var record = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.Consumed && t.ExpiresAt > DateTime.UtcNow);

        if (record is null) return false;

        record.Consumed = true;
        record.User.PasswordHash = hasher.Hash(newPassword);
        record.User.SecurityStamp = Guid.NewGuid().ToString(); // invalidates all sessions
        await db.SaveChangesAsync();
        return true;
    }
}
```

## Language-specific gotchas
- `SHA256.HashData` (static, .NET 7+) is non-allocating and safe to call from any thread. Do not use `SHA256.Create()` per request — use the static method.
- When using ASP.NET Core Identity, `GeneratePasswordResetTokenAsync` already hashes and embeds a security stamp. Do not hash the token again before storing — Identity's `ResetPasswordAsync` expects the raw DataProtection token.
- `Convert.FromHexString` throws `FormatException` on invalid input. Wrap token parameter parsing in a try-catch in the controller and return 400, not 500.
- The `ExecuteUpdateAsync` call to invalidate old tokens must run before `AddAsync` in the same transaction, or use a database-level unique constraint on `(UserId, Consumed=false)`.
- Emit a structured log event on both request and redemption, including `userId` but never the raw token.

## Tests to write
- `RequestResetAsync` for an unknown email returns without throwing and sends no email.
- A second `RequestResetAsync` for the same user invalidates the first token.
- `RedeemAsync` with a valid token returns `true` and marks the token as consumed.
- `RedeemAsync` called a second time with the same token returns `false`.
- `RedeemAsync` with an expired token returns `false`.
