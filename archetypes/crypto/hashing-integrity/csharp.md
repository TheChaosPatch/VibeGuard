---
schema_version: 1
archetype: crypto/hashing-integrity
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Security.Cryptography.HMACSHA256
  acceptable:
    - System.Security.Cryptography.HMACSHA512
    - System.Security.Cryptography.SHA256 (for keyless digest)
    - System.Security.Cryptography.SHA3_256 (.NET 8+)
  avoid:
    - name: System.Security.Cryptography.MD5
      reason: Collision attacks are practical; broken for integrity use cases.
    - name: System.Security.Cryptography.SHA1
      reason: SHAttered collision attack is demonstrated; deprecated for new use.
    - name: String.Equals or == for MAC comparison
      reason: Short-circuits on first mismatch — timing oracle for MAC forgery.
minimum_versions:
  dotnet: "10.0"
---

# Hashing and Data Integrity — C#

## Library choice
`System.Security.Cryptography.HMACSHA256` is the correct default for keyed integrity. It is allocation-friendly, available in all .NET runtimes, and backed by the OS crypto provider. For high-throughput scenarios, the static `HMACSHA256.HashData(key, data)` method (added in .NET 7) avoids instantiating and disposing the HMAC object per call. For keyless checksums, `SHA256.HashData(data)` is the equivalent one-liner. Constant-time comparison is `CryptographicOperations.FixedTimeEquals` — this is the only acceptable comparator for MAC values.

## Reference implementation
```csharp
using System.Security.Cryptography;
using System.Text;

public static class IntegrityService
{
    // HMAC: keyed — use when the tag must be unforgeable
    public static byte[] ComputeHmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
        => HMACSHA256.HashData(key, data);

    // Verify — constant-time to prevent timing oracle
    public static bool VerifyHmac(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> expectedTag)
    {
        var actualTag = HMACSHA256.HashData(key, data);
        return CryptographicOperations.FixedTimeEquals(actualTag, expectedTag);
    }

    // Keyless digest: content-addressed storage, download checksums
    public static byte[] Sha256Digest(ReadOnlySpan<byte> data)
        => SHA256.HashData(data);

    // Webhook helper — binds purpose string to prevent cross-context replay
    public static byte[] WebhookTag(ReadOnlySpan<byte> hmacKey, string payload)
    {
        var prefixed = Encoding.UTF8.GetBytes("webhook-v1:" + payload);
        return HMACSHA256.HashData(hmacKey, prefixed);
    }
}
```

## Language-specific gotchas
- `HMACSHA256.HashData(key, data)` is a static, allocation-minimised path added in .NET 7. Prefer it over `new HMACSHA256(key) { ... }` unless you need incremental hashing of a stream.
- `CryptographicOperations.FixedTimeEquals` requires both spans to be the same length to return true — it will return false (not throw) if lengths differ. This is correct behavior: a tag of the wrong length is always invalid.
- `HMACSHA256` implements `IDisposable`. If you use the instance form, wrap in `using`. The static `HashData` form manages its own lifetime internally.
- For streaming large files, use `IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key)` and call `AppendData` in chunks, then `GetHashAndReset`. This avoids loading the whole file into memory.
- `SHA256.HashData` was added in .NET 5. On .NET 6+, it dispatches to a hardware-accelerated path (AVX-512 on supported CPUs). There is no reason to use `SHA256.Create().ComputeHash()` for one-shot digests.
- HMAC key length: HMAC-SHA256 accepts any key length, but keys shorter than 32 bytes are padded with zeros and keys longer than 64 bytes are hashed first. Use exactly 32 bytes from a CSPRNG for the most predictable security margin.

## Tests to write
- Round-trip HMAC: compute tag, verify with same key and data, assert true.
- Wrong-key rejection: compute tag with key A, verify with key B, assert false.
- Tampered data: compute tag, mutate one byte of data, verify, assert false.
- Constant-time comparison: assert `CryptographicOperations.FixedTimeEquals` is used (code review / Roslyn analyzer rule), not `SequenceEqual` or `==`.
- Purpose binding: compute webhook tag and plain tag for same payload, assert they differ.
- Key source: assert HMAC key is at least 32 bytes and was not derived from a constant or hardcoded string.
