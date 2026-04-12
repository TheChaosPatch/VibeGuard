---
schema_version: 1
archetype: auth/mfa
language: csharp
principles_file: _principles.md
libraries:
  preferred: Otp.NET
  acceptable:
    - Fido2.Models
  avoid:
    - name: Custom HMAC-based OTP implementation
      reason: Off-by-one errors in time-step calculation and missing constant-time comparison are the norm.
    - name: System.Random for backup code generation
      reason: Not cryptographically secure. Backup codes must come from CSPRNG.
minimum_versions:
  dotnet: "10.0"
---

# Multi-Factor Authentication -- C#

## Library choice
`Otp.NET` implements RFC 6238 TOTP and RFC 4226 HOTP correctly, with configurable time steps and hash algorithms. For WebAuthn/FIDO2, `Fido2.Models` (the FIDO2 .NET library) handles attestation and assertion ceremonies. Both are focused libraries with no unnecessary dependencies. Do not hand-roll HMAC-SHA1 time-step math -- the edge cases (clock skew, truncation, endianness) are where bugs hide.

## Reference implementation
```csharp
using OtpNet;
using System.Security.Cryptography;
using System.Text;

public sealed class TotpService
{
    private const int SecretSize = 20; // 160-bit per RFC 6238
    private const int Window = 1;      // +/- 1 step (90s validity)

    public (string Secret, string Uri) Enroll(string email, string issuer)
    {
        var secret = RandomNumberGenerator.GetBytes(SecretSize);
        var b32 = Base32Encoding.ToString(secret);
        var uri = $"otpauth://totp/{issuer}:{email}?secret={b32}&issuer={issuer}&digits=6&period=30";
        return (b32, uri);
    }

    public bool Verify(string base32Secret, string code)
    {
        var key = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(key, step: 30, totpSize: 6);
        return totp.VerifyTotp(code, out _, new VerificationWindow(Window, Window));
    }
}

public static class BackupCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I/L

    public static (List<string> Plain, List<string> Hashes) Generate(int count = 10, int len = 8)
    {
        var plain = new List<string>(count);
        var hashes = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            Span<char> buf = stackalloc char[len];
            for (var j = 0; j < len; j++)
                buf[j] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            var code = new string(buf);
            plain.Add(code);
            hashes.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))));
        }
        return (plain, hashes);
    }
}
```

## Language-specific gotchas
- `VerificationWindow(previous: 1, future: 1)` is the correct skew tolerance. The default in some TOTP libraries is 0 (current window only), which causes usability failures at window boundaries. Wider than 1 degrades security.
- `RandomNumberGenerator.GetInt32` (introduced in .NET 6) provides uniform cryptographic random integers. Never `Random.Shared.Next` for backup codes.
- Store the TOTP secret encrypted at rest (use Data Protection API or an envelope encryption key). If the database is compromised, plaintext secrets let the attacker generate valid codes.
- The alphabet deliberately excludes ambiguous characters (`0`, `O`, `1`, `I`, `L`) to reduce transcription errors when users type codes from a printed sheet.
- When redeeming backup codes, normalize input (`ToUpperInvariant().Trim()`) and compare hashes with `CryptographicOperations.FixedTimeEquals`. Mark consumed codes in the store immediately.

## Tests to write
- Round-trip: enroll, generate a TOTP code from the returned secret using the same library, verify it succeeds.
- Window boundary: generate a code, advance the clock by 31 seconds, verify it still succeeds (within the +1 window).
- Expired code: advance the clock by 61 seconds past generation, verify rejection.
- Rate limiting: submit 5 wrong codes, confirm the 6th attempt is rejected regardless of correctness (integration test against your rate-limit middleware).
- Backup code round-trip: generate codes, redeem one, confirm it works. Redeem the same code again, confirm rejection.
- Backup code exhaustion: redeem all codes, confirm the next attempt fails and the user is directed to recovery.
