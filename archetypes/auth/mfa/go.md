---
schema_version: 1
archetype: auth/mfa
language: go
principles_file: _principles.md
libraries:
  preferred: github.com/pquerna/otp
  acceptable:
    - github.com/go-webauthn/webauthn
  avoid:
    - name: Custom HMAC-based OTP with crypto/hmac
      reason: Time-step calculation, dynamic truncation, and endianness bugs are the norm in hand-rolled implementations.
    - name: math/rand for backup code generation
      reason: Not cryptographically secure. Use crypto/rand exclusively.
minimum_versions:
  go: "1.22"
---

# Multi-Factor Authentication -- Go

## Library choice
`github.com/pquerna/otp` is the standard Go library for TOTP (RFC 6238) and HOTP (RFC 4226). It handles time-step calculation, dynamic truncation, and base32 encoding correctly. For WebAuthn/FIDO2, `github.com/go-webauthn/webauthn` covers registration and authentication ceremonies. Both are well-maintained and focused. Do not build TOTP from `crypto/hmac` + `encoding/binary` -- the truncation logic and counter endianness are where hand-rolled implementations break.

## Reference implementation
```go
package mfa

import (
	"crypto/rand"; "crypto/sha256"; "encoding/hex"
	"fmt"; "math/big"; "time"
	"github.com/pquerna/otp"
	"github.com/pquerna/otp/totp"
)

func Enroll(email, issuer string) (string, string, error) {
	key, err := totp.Generate(totp.GenerateOpts{
		Issuer: issuer, AccountName: email, SecretSize: 20,
		Algorithm: otp.AlgorithmSHA1, Digits: otp.DigitsSix, Period: 30,
	})
	if err != nil { return "", "", fmt.Errorf("totp generate: %w", err) }
	return key.Secret(), key.URL(), nil
}

func Verify(secret, code string) bool {
	valid, _ := totp.ValidateCustom(code, secret, time.Now().UTC(),
		totp.ValidateOpts{Period: 30, Skew: 1, Digits: otp.DigitsSix, Algorithm: otp.AlgorithmSHA1})
	return valid
}

const backupAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789" // no 0/O/1/I/L

func GenerateBackupCodes(count, length int) ([]string, []string, error) {
	max := big.NewInt(int64(len(backupAlphabet)))
	plain, hashes := make([]string, 0, count), make([]string, 0, count)
	for range count {
		buf := make([]byte, length)
		for i := range buf {
			n, err := rand.Int(rand.Reader, max)
			if err != nil { return nil, nil, fmt.Errorf("crypto/rand: %w", err) }
			buf[i] = backupAlphabet[n.Int64()]
		}
		code := string(buf)
		h := sha256.Sum256([]byte(code))
		plain = append(plain, code)
		hashes = append(hashes, hex.EncodeToString(h[:]))
	}
	return plain, hashes, nil
}
```

## Language-specific gotchas
- `totp.ValidateOpts{Skew: 1}` accepts the current window and one on each side. The `Skew` field in `pquerna/otp` is the number of periods in each direction, so `Skew: 1` means +/-1 window (roughly 90 seconds). Do not increase it.
- `crypto/rand.Int` is the correct way to generate a uniform random index from `crypto/rand`. Do not use `math/rand` or `math/rand/v2` for anything security-relevant.
- When redeeming backup codes, normalize input (`strings.ToUpper(strings.TrimSpace(code))`), hash with SHA-256, and compare with `subtle.ConstantTimeCompare`. Both slices are hex-encoded SHA-256 (64 bytes), so length is guaranteed equal -- add a guard if the format changes.
- Store the TOTP secret encrypted at rest. If you use a relational database, encrypt the column with an application-level key (envelope encryption). A leaked secrets table is a full MFA bypass.
- The backup code alphabet excludes `0/O`, `1/I/L` to minimize transcription errors.
- `totp.Generate` uses `crypto/rand` internally for secret generation. Do not override the random source.

## Tests to write
- Round-trip: enroll, generate a code with `totp.GenerateCode(secret, time.Now())`, verify it returns true.
- Window boundary: generate a code, advance time by 31 seconds, verify it still passes.
- Expired code: advance time by 61 seconds, verify rejection.
- Wrong code: verify `"000000"` against a random secret returns false.
- Backup code round-trip: generate codes, redeem one, confirm success. Redeem the same code again, confirm failure.
- Backup code case insensitivity: redeem a lowercase version of a generated code, confirm success.
- Concurrency: call `Verify` from multiple goroutines with the race detector enabled to confirm no data races.
