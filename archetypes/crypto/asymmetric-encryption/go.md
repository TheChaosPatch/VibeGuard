---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: go
principles_file: _principles.md
libraries:
  preferred: crypto/ecdsa + crypto/ed25519 (stdlib)
  acceptable:
    - crypto/rsa (stdlib, OAEP only)
    - filippo.io/edwards25519
    - github.com/aws/aws-sdk-go-v2/service/kms
  avoid:
    - name: crypto/rsa with PKCS1v15 encryption
      reason: Bleichenbacher-vulnerable; use rsa.EncryptOAEP.
    - name: math/big for custom elliptic curve math
      reason: Timing side-channels; use stdlib or filippo.io/edwards25519.
minimum_versions:
  go: "1.22"
---

# Asymmetric Encryption and Signing — Go

## Library choice
Go's standard library covers Ed25519 (`crypto/ed25519`), ECDSA on NIST curves (`crypto/ecdsa`), and RSA (`crypto/rsa`). Ed25519 is the recommended default for new signing use cases: deterministic, constant-time, and immune to nonce-reuse. For RSA encryption you must call `rsa.EncryptOAEP` — never `rsa.EncryptPKCS1v15`. For KMS-backed signing (keys that never leave an HSM), use the AWS, GCP, or Azure SDK. `filippo.io/edwards25519` is the reference low-level Ed25519 implementation used by the stdlib itself — import it only if you need raw field arithmetic.

## Reference implementation
```go
package signing

import (
    "crypto"
    "crypto/ed25519"
    "crypto/rand"
    "crypto/rsa"
    "crypto/sha256"
    "fmt"
)

// GenerateEd25519Key returns a new Ed25519 key pair.
func GenerateEd25519Key() (ed25519.PublicKey, ed25519.PrivateKey, error) {
    pub, priv, err := ed25519.GenerateKey(rand.Reader)
    if err != nil {
        return nil, nil, fmt.Errorf("signing: generate Ed25519: %w", err)
    }
    return pub, priv, nil
}

// Sign signs data with an Ed25519 private key. Deterministic; no nonce.
func Sign(data []byte, priv ed25519.PrivateKey) []byte {
    return ed25519.Sign(priv, data)
}

// Verify returns true iff signature is a valid Ed25519 signature over data.
func Verify(data, sig []byte, pub ed25519.PublicKey) bool {
    return ed25519.Verify(pub, data, sig)
}

// EncryptDEK wraps a symmetric DEK with RSA-OAEP-SHA256.
// Use only for the DEK, never the full payload.
func EncryptDEK(dek []byte, pub *rsa.PublicKey) ([]byte, error) {
    return rsa.EncryptOAEP(sha256.New(), rand.Reader, pub, dek, nil)
}

// DecryptDEK unwraps an RSA-OAEP-SHA256-wrapped DEK.
func DecryptDEK(wrapped []byte, priv *rsa.PrivateKey) ([]byte, error) {
    return rsa.DecryptOAEP(sha256.New(), rand.Reader, priv, wrapped, nil)
}
```

## Language-specific gotchas
- `ed25519.Sign` in Go's stdlib already hashes internally with SHA-512 (as per RFC 8032). Do not pre-hash data before passing it; doing so produces a signature over `SHA512(data)` which will fail against any correct verifier.
- For ECDSA on P-256, use `crypto/ecdsa` with `ecdsa.SignASN1` / `ecdsa.VerifyASN1` (Go 1.15+). The older `ecdsa.Sign` / `ecdsa.Verify` return raw `(r, s)` big.Int pairs with no encoding — avoid them for wire protocols.
- `rsa.GenerateKey(rand.Reader, 2048)` is the minimum. Use 3072 for keys you plan to keep past 2030. The public exponent is always 65537 in Go's implementation.
- Never use `rsa.SignPKCS1v15` for new code. Use `rsa.SignPSS` with `rsa.PSSOptions{SaltLength: rsa.PSSSaltLengthEqualsHash}` if RSA signing is required by an existing protocol.
- The `crypto.Signer` interface (`Sign(rand io.Reader, digest []byte, opts crypto.SignerOpts) ([]byte, error)`) lets you swap an in-process key for a KMS-backed one transparently. Design signing functions to accept `crypto.Signer`, not `ed25519.PrivateKey` or `*rsa.PrivateKey`.
- `rand.Reader` must be passed to every operation that uses randomness (`GenerateKey`, `EncryptOAEP`, `DecryptOAEP`, `SignPSS`). Never substitute `nil` — the functions will panic.

## Tests to write
- Round-trip: sign a payload, verify with the returned public key, assert true.
- Wrong-key rejection: sign with key A, verify with key B's public key, assert false.
- Tampered payload: sign, change one byte of data, verify, assert false.
- RSA OAEP round-trip: generate 32-byte DEK, encrypt with public key, decrypt with private key, assert equal.
- Signer interface: wrap `ed25519.PrivateKey` as `crypto.Signer`, call `Sign`, verify the output with `ed25519.Verify`.
- RSA key size: generate RSA key, assert `priv.N.BitLen() >= 2048`.
