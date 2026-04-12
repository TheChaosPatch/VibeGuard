---
schema_version: 1
archetype: crypto/key-management
language: go
principles_file: _principles.md
libraries:
  preferred: crypto/rand (DEK generation) + cloud KMS SDK (KEK operations)
  acceptable:
    - cloud.google.com/go/kms
    - github.com/aws/aws-sdk-go-v2/service/kms
    - github.com/Azure/azure-sdk-for-go/sdk/security/keyvault/azkeys
  avoid:
    - name: Hardcoded byte slices or string constants
      reason: Cannot be rotated, audited, or revoked.
    - name: math/rand for key generation
      reason: Not a CSPRNG; output is predictable.
minimum_versions:
  go: "1.22"
---

# Cryptographic Key Management -- Go

## Library choice
Go has no built-in KMS abstraction, so key management is split between `crypto/rand` for local DEK generation and a cloud KMS SDK for KEK operations. For AWS, use `github.com/aws/aws-sdk-go-v2/service/kms`; for GCP, `cloud.google.com/go/kms`; for Azure, `azkeys`. The pattern is the same everywhere: the KMS holds the KEK and performs `Encrypt`/`Decrypt` (wrapping/unwrapping) of the DEK; the application holds the plaintext DEK in memory only for the duration of its use, then zeroes the slice. Go's `clear` builtin (added in 1.21) zeroes a slice in place and is the correct tool for key destruction.

## Reference implementation
```go
package keyring

import (
	"crypto/rand"
	"errors"
	"fmt"
	"io"
)

// KeyMaterial holds DEK bytes. Call Destroy when done.
type KeyMaterial struct {
	Version int
	key     []byte
}

func (km *KeyMaterial) Key() ([]byte, error) {
	if len(km.key) == 0 {
		return nil, errors.New("keyring: key material destroyed")
	}
	return km.key, nil
}

// Destroy zeroes key bytes via clear() and nils the slice.
func (km *KeyMaterial) Destroy() {
	clear(km.key)
	km.key = nil
}

// GenerateDEK creates a 256-bit DEK from crypto/rand.
func GenerateDEK(version int) (*KeyMaterial, error) {
	key := make([]byte, 32)
	if _, err := io.ReadFull(rand.Reader, key); err != nil {
		return nil, fmt.Errorf("keyring: generate DEK: %w", err)
	}
	return &KeyMaterial{Version: version, key: key}, nil
}
```

## Language-specific gotchas
- `clear(slice)` zeroes all elements of the slice in place. It was added in Go 1.21. For older Go, use a manual loop: `for i := range key { key[i] = 0 }`. Setting the slice to `nil` without zeroing first leaves key bytes in the heap until the GC collects and the OS reclaims the page.
- Go's garbage collector may copy heap objects during compaction. There is no `GCHandle.Alloc(Pinned)` equivalent. For defense in depth, keep keys in slices (not strings -- strings are immutable and cannot be zeroed), minimize key lifetime, and zero immediately after use. For truly zero-copy key handling, consider `mlock`/`mprotect` via `syscall`, but this is rarely justified outside HSM-adjacent code.
- `io.ReadFull(rand.Reader, key)` is preferred over `rand.Read(key)` because `ReadFull` guarantees exactly `len(key)` bytes or an error. `rand.Read` on most platforms also guarantees this, but `ReadFull` makes the contract explicit.
- A key-version provider (map of version to `*KeyMaterial`) should be protected by `sync.RWMutex` -- registration is rare (write lock), lookup is frequent (read lock). Do not hold a reference to `KeyMaterial.key` across goroutines after `Destroy`.
- When using AWS KMS `GenerateDataKey`, the response includes `Plaintext` (the unwrapped DEK) and `CiphertextBlob` (the wrapped DEK). Store only `CiphertextBlob`. After constructing a `KeyMaterial` from `Plaintext`, zero the SDK response field: `clear(resp.Plaintext)`.
- Do not use `fmt.Sprintf` or `slog.Any` with a `KeyMaterial` value. The `key` field is unexported, so `%v` will not print it, but a custom `String()` method or JSON marshaling could leak it. Add a `String()` method that returns only the version.

## Tests to write
- Key length: `GenerateDEK` returns 32-byte key.
- Zeroing: create a `KeyMaterial`, copy the key, call `Destroy`, assert every byte in the original backing array is zero.
- Destroyed access: after `Destroy`, `Key()` returns an error.
- Two DEKs differ: generate two DEKs, assert keys are not equal.
- Version tagging: `GenerateDEK(5)` returns a `KeyMaterial` with `Version == 5`.
- No key in fmt output: `fmt.Sprintf("%v", km)` does not contain the key bytes (unexported field).
