---
schema_version: 1
archetype: auth/password-hashing
language: csharp
principles_file: _principles.md
libraries:
  preferred: Konscious.Security.Cryptography.Argon2
  acceptable:
    - BCrypt.Net-Next
  avoid:
    - name: System.Security.Cryptography.Rfc2898DeriveBytes
      reason: PBKDF2 only — acceptable for FIPS, not preferred for greenfield.
    - name: System.Security.Cryptography.SHA256
      reason: Fast hash, not a password hash. Never use for credentials.
minimum_versions:
  dotnet: "10.0"
---

# Password Hashing — C#

## Library choice
`Konscious.Security.Cryptography.Argon2` gives you Argon2id with tunable memory, iteration, and parallelism parameters. It is community-maintained but widely audited. `BCrypt.Net-Next` is acceptable if you have an existing bcrypt database to interoperate with.

## Reference implementation
```csharp
using Konscious.Security.Cryptography;
using System.Security.Cryptography;
using System.Text;

public sealed class Argon2PasswordHasher
{
    private const int DegreeOfParallelism = 4;
    private const int MemorySize = 65_536; // 64 MiB
    private const int Iterations = 3;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Compute(password, salt);
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={DegreeOfParallelism}$" +
               $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        var salt = Convert.FromBase64String(parts[^2]);
        var expected = Convert.FromBase64String(parts[^1]);
        var actual = Compute(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Compute(string password, byte[] salt)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySize,
            Iterations = Iterations
        };
        return argon.GetBytes(HashSize);
    }
}
```

## Language-specific gotchas
- Use `CryptographicOperations.FixedTimeEquals` for hash comparison — `SequenceEqual` leaks timing information.
- `RandomNumberGenerator.GetBytes` is the right source of salt randomness. Never `System.Random`.
- Store the encoded string (including algorithm + parameters) as the column value, not the raw hash bytes. That's what makes rehash-on-login possible when you upgrade parameters.
- Wrap this in a `sealed` class behind an `IPasswordHasher` interface so routes never take a dependency on the concrete library.

## Tests to write
- Round-trip: `Verify(password, Hash(password))` is true for a handful of inputs including unicode and long strings.
- Negative: wrong password returns false, and the verify is constant-time in shape (don't early-return).
- Parameter drift: an encoded hash with old parameters still verifies, but the service also signals a rehash is needed.
- Salt uniqueness: hashing the same password twice yields distinct encoded strings.
