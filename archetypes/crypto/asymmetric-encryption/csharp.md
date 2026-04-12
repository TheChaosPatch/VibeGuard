---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Security.Cryptography (ECDsa, RSA, ECDiffieHellman)
  acceptable:
    - Microsoft.IdentityModel.Tokens (JWT signing)
    - Azure.Security.KeyVault.Keys.Cryptography
  avoid:
    - name: BouncyCastle (raw, without wrapper)
      reason: Safe but verbose; native .NET APIs are sufficient and less prone to misconfiguration.
    - name: RSA with PKCS1v1.5 padding for encryption
      reason: Vulnerable to Bleichenbacher adaptive chosen-ciphertext attacks; use OaepSHA256.
    - name: Custom RSA padding implementation
      reason: Any hand-rolled padding scheme will be wrong. Use the built-in padding modes.
minimum_versions:
  dotnet: "10.0"
---

# Asymmetric Encryption and Signing — C#

## Library choice
`System.Security.Cryptography` provides `ECDsa`, `RSA`, `ECDiffieHellman`, and `ECDsaCng`/`RSACng` (Windows CNG-backed). Prefer `ECDsa.Create(ECCurve.NamedCurves.nistP256)` for ECDSA or `ECDsa.Create("Ed25519")` for EdDSA (available on .NET 8+ via `ECDsa.Create` with the `"id-EdDSA"` OID on supported platforms). For JWT issuance, `Microsoft.IdentityModel.Tokens` wraps these cleanly. For KMS-backed keys, `Azure.Security.KeyVault.Keys.Cryptography.CryptographyClient` signs and verifies without the private key ever leaving the HSM.

## Reference implementation
```csharp
using System.Security.Cryptography;

// --- Key generation (do once; export private key to secrets store) ---
ECDsa GenerateSigningKey() =>
    ECDsa.Create(ECCurve.NamedCurves.nistP256);

// --- Signing ---
byte[] Sign(byte[] data, ECDsa privateKey)
{
    // SHA-256 digest + ECDSA signature over the raw bytes
    return privateKey.SignData(data, HashAlgorithmName.SHA256);
}

// --- Verification ---
bool Verify(byte[] data, byte[] signature, ECDsa publicKey)
{
    // Never catch CryptographicException here and return true.
    return publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256);
}

// --- RSA hybrid encryption (encrypt small DEK, not large payload) ---
byte[] RsaEncryptDek(byte[] dek, RSA recipientPublicKey) =>
    recipientPublicKey.Encrypt(dek, RSAEncryptionPadding.OaepSHA256);

byte[] RsaDecryptDek(byte[] wrappedDek, RSA recipientPrivateKey) =>
    recipientPrivateKey.Decrypt(wrappedDek, RSAEncryptionPadding.OaepSHA256);

// --- Export public key for distribution ---
string ExportPublicKeyPem(ECDsa key) =>
    key.ExportSubjectPublicKeyInfoPem();
```

## Language-specific gotchas
- `ECDsa` and `RSA` are `IDisposable`. Use `using` or store as a singleton in a `Lazy<T>` — repeated creation is expensive, especially on Windows CNG.
- `SignData` hashes internally; do not pre-hash. If you call `SignHash(SHA256.HashData(data), ...)`, you're hashing twice and the signature is over `Hash(Hash(data))`. Use `SignData` with the algorithm name, or `SignHash` with exactly one hash of the data.
- `ECDsa.Create("id-EdDSA")` requires the platform to support EdDSA (Linux OpenSSL 1.1.1+, macOS 13+). Test on CI with the actual target OS; Windows CNG does not support Ed25519 as of .NET 10.
- For JWT signing, pin the algorithm at validation time: `new TokenValidationParameters { ValidAlgorithms = new[] { "ES256" } }`. Never leave `ValidAlgorithms` null — that permits `alg: none`.
- `RSA.Create(2048)` is the minimum; prefer `RSA.Create(3072)` for keys you expect to use past 2030.
- When loading a key from PEM, use `ImportFromPem` (not `FromXmlString`). XML key format is Windows-only and non-standard.

## Tests to write
- Round-trip sign/verify: sign a known payload, verify with the corresponding public key, assert true.
- Wrong-key rejection: sign with key A, verify with key B, assert false (not exception).
- Tampered payload: sign, mutate one byte of the data, verify, assert false.
- Algorithm pinning: assert that JWT validation rejects a token with `alg: none` or `alg: HS256` when an ECDSA key is configured.
- Key size: generate RSA key, assert `KeySize >= 2048`.
- OaepSHA256 padding: encrypt a 32-byte DEK with the public key, decrypt with private key, assert equality; then assert that `Pkcs1` padding throws or is rejected by policy.
