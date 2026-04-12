---
schema_version: 1
archetype: crypto/hashing-integrity
language: go
principles_file: _principles.md
libraries:
  preferred: crypto/hmac + crypto/sha256 (stdlib)
  acceptable:
    - crypto/sha512 (stdlib)
    - golang.org/x/crypto/sha3
  avoid:
    - name: crypto/md5
      reason: Collision attacks are practical; do not use for integrity.
    - name: crypto/sha1
      reason: SHAttered collision demonstrated; deprecated for new use.
    - name: bytes.Equal for MAC comparison
      reason: Not constant-time — use hmac.Equal.
minimum_versions:
  go: "1.22"
---

# Hashing and Data Integrity — Go

## Library choice
Go's `crypto/hmac` combined with `crypto/sha256` covers all HMAC use cases with zero external dependencies. `hmac.Equal` is the constant-time comparator — it is the only acceptable way to compare MACs in Go. For keyless digests, `sha256.Sum256(data)` returns a `[32]byte` in one call. For streaming large inputs, use `sha256.New()` and call `Write` in chunks. `golang.org/x/crypto/sha3` provides SHA3-256/512 and SHAKE if you need them. Do not use `crypto/md5` or `crypto/sha1` for any new integrity work.

## Reference implementation
```go
package integrity

import (
    "crypto/hmac"
    "crypto/sha256"
    "fmt"
)

var webhookPrefix = []byte("webhook-v1:")

// ComputeHMAC returns an HMAC-SHA256 tag.
// key must be at least 32 random bytes.
func ComputeHMAC(key, data []byte) []byte {
    mac := hmac.New(sha256.New, key)
    mac.Write(data)
    return mac.Sum(nil)
}

// VerifyHMAC compares tags in constant time. Returns false on any mismatch.
func VerifyHMAC(key, data, expectedTag []byte) bool {
    actual := ComputeHMAC(key, data)
    return hmac.Equal(actual, expectedTag)
}

// SHA256Digest returns the SHA-256 digest of data (keyless, integrity only).
func SHA256Digest(data []byte) [32]byte {
    return sha256.Sum256(data)
}

// WebhookTag binds a purpose prefix to the HMAC to prevent cross-context replay.
func WebhookTag(key, payload []byte) []byte {
    prefixed := append(webhookPrefix, payload...)
    return ComputeHMAC(key, prefixed)
}

// StreamDigest computes SHA-256 over arbitrarily large input via io.Writer.
func StreamDigest(writeChunks func(w interface{ Write([]byte) (int, error) }) error) ([]byte, error) {
    h := sha256.New()
    if err := writeChunks(h); err != nil {
        return nil, fmt.Errorf("integrity: stream digest: %w", err)
    }
    return h.Sum(nil), nil
}
```

## Language-specific gotchas
- `hmac.Equal(a, b)` is constant-time regardless of length. `bytes.Equal(a, b)` is not — it is explicitly documented as not constant-time. Never use `bytes.Equal` or `==` on MAC values.
- `hmac.New` takes a hash constructor (`sha256.New`), not a hash instance. Passing `sha256.New()` (the result) is a type mismatch and will not compile. Pass the function reference `sha256.New`.
- `sha256.Sum256(data)` returns `[32]byte` (a value type), not `[]byte`. Convert with `sum[:]` when you need a slice. This distinction matters at API boundaries that accept `[]byte`.
- `mac.Write` on an `hmac.Hash` never returns an error (it satisfies `io.Writer` but the underlying hash never fails). You can safely ignore the error from `Write`; but writing to the hash after `Sum` without calling `Reset` will append to the previous state.
- `append(webhookPrefix, payload...)` mutates `webhookPrefix` if its capacity exceeds its length. Use a copy or `bytes.Join` to avoid this: `bytes.Join([][]byte{webhookPrefix, payload}, nil)`.
- For key lengths: Go's HMAC accepts any key length. Shorter than the block size (64 bytes for SHA-256) is padded; longer is hashed first. Use exactly 32 bytes from `crypto/rand` for a clean 256-bit key.

## Tests to write
- Round-trip: compute HMAC, verify with same key and data, assert true.
- Wrong-key rejection: compute with key A, verify with key B, assert false.
- Tampered data: compute, change one byte of data, verify, assert false.
- `hmac.Equal` vs `bytes.Equal`: assert that `hmac.Equal` is used in `VerifyHMAC` (code review; no runtime test needed).
- Purpose binding: assert `WebhookTag(k, data)` differs from `ComputeHMAC(k, data)`.
- `Sum256` to slice: assert `sha256.Sum256(data)[:]` equals the output of `sha256.New()` with the same data written.
