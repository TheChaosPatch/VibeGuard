---
schema_version: 1
archetype: crypto/key-management
language: csharp
principles_file: _principles.md
libraries:
  preferred: Azure.Security.KeyVault.Keys
  acceptable:
    - AWS.EncryptionSDK
    - System.Security.Cryptography.RandomNumberGenerator (for local DEK generation)
  avoid:
    - name: Hardcoded byte arrays in source
      reason: Cannot be rotated, audited, or revoked.
    - name: System.Security.Cryptography.ProtectedData (DPAPI)
      reason: Machine-scoped, Windows-only, no rotation story, no audit trail.
minimum_versions:
  dotnet: "10.0"
---

# Cryptographic Key Management -- C#

## Library choice
For production key management, use a cloud KMS SDK: `Azure.Security.KeyVault.Keys` (Azure), `Amazon.KeyManagementService` (AWS), or `Google.Cloud.Kms.V1` (GCP). These keep the KEK in hardware, provide audit logging, and enforce IAM policies. For local DEK generation, `RandomNumberGenerator.GetBytes(32)` produces 256-bit keys from the OS CSPRNG. `ProtectedData` (DPAPI) is a legacy Windows-only mechanism with no cross-platform support, no rotation, and no centralized audit -- avoid it for new systems.

## Reference implementation
```csharp
using System.Runtime.InteropServices;
using System.Security.Cryptography;

public interface IKeyProvider : IAsyncDisposable
{
    Task<KeyMaterial> GetCurrentKeyAsync(CancellationToken ct = default);
    Task<KeyMaterial> GetKeyByVersionAsync(int version, CancellationToken ct = default);
}

// Holds DEK bytes in pinned memory and zeroes them on disposal.
public sealed class KeyMaterial : IDisposable
{
    private readonly GCHandle _pin;
    private readonly byte[] _key;
    private bool _disposed;

    public int Version { get; }
    public ReadOnlySpan<byte> Key => _disposed
        ? throw new ObjectDisposedException(nameof(KeyMaterial))
        : _key;

    public KeyMaterial(byte[] keyBytes, int version)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(keyBytes.Length, 32);
        _key = keyBytes;
        _pin = GCHandle.Alloc(_key, GCHandleType.Pinned);
        Version = version;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_key);
        _pin.Free();
    }
}

// Generates a 256-bit DEK from the OS CSPRNG. Caller owns disposal.
public static class DekGenerator
{
    public static KeyMaterial Generate(int version) =>
        new(RandomNumberGenerator.GetBytes(32), version);
}
```

## Language-specific gotchas
- `CryptographicOperations.ZeroMemory` is the correct zeroing API in .NET. `Array.Clear` works but is not guaranteed to survive JIT optimizations that detect "dead stores." `ZeroMemory` is marked as having side effects and will not be elided.
- `GCHandle.Alloc(buf, GCHandleType.Pinned)` prevents the GC from relocating the buffer, which would leave a copy of the key bytes in the old heap location. Always pin key material for its entire lifetime.
- Do not use `string` to hold key material. Strings are immutable, interned, and cannot be zeroed. Use `byte[]` (pinned) or `Span<byte>` on the stack for very short-lived operations.
- When using Azure Key Vault, call `CryptographyClient.UnwrapKeyAsync` to unwrap the DEK at runtime. The KEK never leaves the vault. Cache the unwrapped DEK in a `KeyMaterial` instance for the duration of the key version's active lifetime -- do not call `UnwrapKey` on every request.
- `IAsyncDisposable` on `IKeyProvider` exists because the provider may need to flush cached keys and zero their memory asynchronously. Implement `DisposeAsync` to iterate all cached `KeyMaterial` instances and dispose each one.
- Never log `KeyMaterial.Key`, its Base64 encoding, or any derivative. Log the `Version` property when tracing which key version was used for an operation.

## Tests to write
- Key length enforcement: constructing `KeyMaterial` with a 16-byte array throws `ArgumentOutOfRangeException`.
- Zeroing on disposal: create `KeyMaterial`, copy the key bytes, dispose, assert the backing array is all zeros.
- Disposed access: after disposal, accessing `Key` throws `ObjectDisposedException`.
- DEK generation: call `DekGenerator.Generate` twice, assert the two keys are not equal (256-bit collision probability is effectively zero).
- Version round-trip: `GetCurrentKeyAsync` and `GetKeyByVersionAsync(current)` return the same key bytes.
- No key in logs: serialize a `KeyMaterial` instance with `JsonSerializer.Serialize` and assert the output does not contain the key bytes (regression test against accidental serialization).
