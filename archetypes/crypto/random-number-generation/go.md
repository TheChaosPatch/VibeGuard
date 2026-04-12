---
schema_version: 1
archetype: crypto/random-number-generation
language: go
principles_file: _principles.md
libraries:
  preferred: crypto/rand
  acceptable:
    - crypto/rand + encoding/base64
    - crypto/rand + encoding/hex
  avoid:
    - name: math/rand
      reason: Deterministic PRNG seeded from a predictable source; not for security.
    - name: math/rand/v2 (non-crypto)
      reason: Improved API but still not cryptographic when used with a non-crypto source.
minimum_versions:
  go: "1.22"
---

# Cryptographic Random Number Generation -- Go

## Library choice
`crypto/rand` is the only correct source. `rand.Read` fills a byte slice from the OS CSPRNG (`getrandom` on Linux, `CryptGenRandom` on Windows, `/dev/urandom` on others). `rand.Int` produces a uniform big.Int in a range via rejection sampling. `math/rand` -- even with `math/rand/v2` -- is a deterministic PRNG that is seeded automatically from a fast source since Go 1.20, but its output is still predictable and reproducible by design. Any token, key, or nonce generated with `math/rand` is compromised.

## Reference implementation
```go
package securerand

import (
	"crypto/rand"
	"encoding/base64"
	"encoding/hex"
	"errors"
	"fmt"
	"math/big"
)

func GenerateToken(byteLen int) (string, error) {
	if byteLen < 16 {
		return "", errors.New("securerand: minimum 16 bytes (128 bits) required")
	}
	buf := make([]byte, byteLen)
	if _, err := rand.Read(buf); err != nil {
		return "", fmt.Errorf("securerand: read entropy: %w", err)
	}
	return base64.RawURLEncoding.EncodeToString(buf), nil
}

func GenerateHexToken(byteLen int) (string, error) {
	if byteLen < 16 {
		return "", errors.New("securerand: minimum 16 bytes (128 bits) required")
	}
	buf := make([]byte, byteLen)
	_, err := rand.Read(buf)
	if err != nil {
		return "", fmt.Errorf("securerand: read entropy: %w", err)
	}
	return hex.EncodeToString(buf), nil
}

func UniformInt(max int) (int, error) {
	if max <= 0 {
		return 0, errors.New("securerand: max must be positive")
	}
	n, err := rand.Int(rand.Reader, big.NewInt(int64(max)))
	if err != nil {
		return 0, fmt.Errorf("securerand: uniform int: %w", err)
	}
	return int(n.Int64()), nil
}
```

## Language-specific gotchas
- `crypto/rand.Read` returns an error. On mainstream OSes this practically never fails, but you must still check it -- a silent short read produces a token padded with zero bytes.
- `rand.Int(rand.Reader, max)` performs rejection sampling. Never write `binary.BigEndian.Uint64(buf) % n` -- it has modulo bias for most values of `n`.
- Since Go 1.22, `math/rand/v2` can accept a `rand.Source` backed by `crypto/rand`, but using `crypto/rand` directly is simpler and avoids the import ambiguity. Do not import both `math/rand` and `crypto/rand` in the same file without aliasing -- the compiler allows it, but reviewers will trip over it.
- `base64.RawURLEncoding` omits padding (`=`) and uses `-` and `_` instead of `+` and `/`. This is the correct encoding for tokens in URLs and headers.
- Go does not have a `secrets` equivalent at the stdlib level. The pattern above *is* the idiomatic equivalent. Resist the urge to build a framework around it -- three standalone functions are sufficient.
- Never log the return value. If you need to reference a token in logs, log a truncated SHA-256 prefix (first 8 hex chars) of the token, not the token itself.

## Tests to write
- Length correctness: `GenerateToken(32)` base64-decodes to exactly 32 bytes.
- Minimum length enforcement: `GenerateToken(8)` returns a non-nil error.
- No collisions: generate 10,000 tokens, assert all distinct.
- Uniform distribution: call `UniformInt(6)` 60,000 times and assert each bucket is within 20% of expected.
- URL safety: `GenerateToken` output matches `^[A-Za-z0-9_-]+$` (no `+`, `/`, or `=`).
- Hex format: `GenerateHexToken(16)` returns a 32-character string matching `^[0-9a-f]{32}$`.
- Error on zero/negative: `UniformInt(0)` and `UniformInt(-1)` return errors.
