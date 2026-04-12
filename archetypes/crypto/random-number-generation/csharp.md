---
schema_version: 1
archetype: crypto/random-number-generation
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Security.Cryptography.RandomNumberGenerator
  avoid:
    - name: System.Random
      reason: Not cryptographically secure; output is predictable given the seed.
    - name: System.Security.Cryptography.RNGCryptoServiceProvider
      reason: Deprecated in .NET 6+; use the static RandomNumberGenerator methods instead.
minimum_versions:
  dotnet: "10.0"
---

# Cryptographic Random Number Generation -- C#

## Library choice
`System.Security.Cryptography.RandomNumberGenerator` is the only correct choice. Its static methods (`GetBytes`, `GetInt32`, `GetHexString`, `Fill`) draw from the OS CSPRNG with zero configuration. `System.Random` is a deterministic PRNG seeded from the system clock -- it is designed for simulations and shuffles, not security. `RNGCryptoServiceProvider` is the pre-.NET 6 spelling of the same primitive and is obsolete; the static API is both simpler and allocation-free.

## Reference implementation
```csharp
using System.Security.Cryptography;

public static class SecureTokens
{
    /// <summary>
    /// Generates a URL-safe Base64-encoded token with the specified
    /// number of random bytes (default 32 = 256 bits of entropy).
    /// </summary>
    public static string GenerateToken(int byteLength = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteLength, 16);
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Generates a uniform random integer in [0, exclusiveMax)
    /// without modulo bias.
    /// </summary>
    public static int GenerateUniformInt(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(exclusiveMax, 0);
        return RandomNumberGenerator.GetInt32(exclusiveMax);
    }

    /// <summary>
    /// Generates a hex-encoded random string (2 hex chars per byte).
    /// </summary>
    public static string GenerateHexToken(int byteLength = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(byteLength, 16);
        return RandomNumberGenerator.GetHexString(byteLength, lowercase: true);
    }
}
```

## Language-specific gotchas
- `RandomNumberGenerator.GetInt32(toExclusive)` handles rejection sampling internally. Never write `GetBytes(4) % N` by hand -- it has modulo bias and is harder to read.
- `RandomNumberGenerator.GetHexString` was added in .NET 8. Use it instead of `Convert.ToHexString(GetBytes(n))` -- the semantics are the same but the intent is clearer.
- `System.Random.Shared` (added in .NET 6) is thread-safe but still not cryptographic. Its convenience makes it tempting for token generation -- resist.
- The static methods on `RandomNumberGenerator` are thread-safe. There is no reason to create an instance or hold a lock.
- If you need a nonce for AES-GCM, call `RandomNumberGenerator.GetBytes(12)` directly in the encryption layer (see `crypto/symmetric-encryption`). Don't route it through a token-generation utility -- nonces have different length requirements and are not tokens.
- Never log or include the generated token in exception messages. Return it exactly once to the caller; after that, store only its hash if you need to look it up later.

## Tests to write
- Length correctness: `GenerateToken(32)` produces a Base64 string that decodes to exactly 32 bytes.
- Minimum length enforcement: `GenerateToken(8)` throws `ArgumentOutOfRangeException`.
- No collisions: generate 10,000 tokens, assert all distinct (smoke test against a broken source).
- Uniform distribution: call `GenerateUniformInt(6)` 60,000 times and assert each bucket is within 20% of 10,000 (chi-squared smoke test).
- URL safety: `GenerateToken` output contains no `+`, `/`, or `=` characters.
- Hex format: `GenerateHexToken(16)` returns a 32-character lowercase hex string matching `^[0-9a-f]{32}$`.
