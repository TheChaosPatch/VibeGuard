---
schema_version: 1
archetype: memory/buffer-overflow
language: go
principles_file: _principles.md
libraries:
  preferred: standard library slices with explicit length checks
  avoid:
    - name: unsafe.Pointer arithmetic
      reason: Bypasses Go's bounds checking entirely.
    - name: reflect.SliceHeader / reflect.StringHeader
      reason: Deprecated in Go 1.20; use unsafe.Slice and unsafe.String instead, with bounds checks.
minimum_versions:
  go: "1.22"
---

# Buffer Overflow Defense — Go

## Library choice
Go slices are bounds-checked at runtime by default, so "buffer overflow" in Go usually means a slice-bounds panic that crashes the process. The defense is checking `len()` before indexing, not catching panics with `recover`. For the rare case where you need `unsafe.Pointer` (CGo interop, zero-copy serialization), the same C-level discipline applies: validate every offset and length before dereferencing.

## Reference implementation
```go
package codec

import (
	"encoding/binary"
	"errors"
	"fmt"
)

var (
	ErrBufferTooShort = errors.New("buffer too short")
	ErrMessageTooLarge = errors.New("message exceeds max size")
)

const MaxMessageSize = 1 << 20 // 1 MiB

// ReadPrefixedMessage reads a 4-byte little-endian length prefix followed
// by that many bytes of payload. It never panics on short input.
func ReadPrefixedMessage(buf []byte) ([]byte, []byte, error) {
	if len(buf) < 4 {
		return nil, buf, fmt.Errorf("need 4-byte header: %w", ErrBufferTooShort)
	}
	msgLen := binary.LittleEndian.Uint32(buf[:4])
	if msgLen > MaxMessageSize {
		return nil, buf, fmt.Errorf("length %d: %w", msgLen, ErrMessageTooLarge)
	}
	end := 4 + int(msgLen)
	if end < 4 { // integer overflow check
		return nil, buf, fmt.Errorf("length overflow: %w", ErrBufferTooShort)
	}
	if len(buf) < end {
		return nil, buf, fmt.Errorf(
			"need %d bytes, have %d: %w", end, len(buf), ErrBufferTooShort)
	}
	return buf[4:end], buf[end:], nil
}

// SafeIndex returns the element at idx or an error, never a panic.
func SafeIndex[T any](s []T, idx int) (T, error) {
	if idx < 0 || idx >= len(s) {
		var zero T
		return zero, fmt.Errorf(
			"index %d out of range [0, %d): %w", idx, len(s), ErrBufferTooShort)
	}
	return s[idx], nil
}
```

## Language-specific gotchas
- A slice-bounds panic is Go's equivalent of a buffer overflow. It is memory-safe (no corruption) but it terminates the goroutine or crashes the process. Check `len()` explicitly before indexing with values derived from untrusted input.
- Do NOT wrap handler code in `recover` to "handle" bounds panics. A panic means your preconditions were wrong. Fix the check, do not hide the bug.
- `unsafe.Pointer` arithmetic in Go has the same risks as C pointer math. If you must use it, keep the unsafe block in one function, validate all offsets at the top, and return a safe slice.
- `unsafe.Slice(ptr, length)` (Go 1.17+) creates a slice from a pointer. If `length` is attacker-controlled, you get arbitrary memory reads. Always validate it against the known allocation size.
- Integer conversion from `uint32` to `int` is safe on 64-bit but can overflow on 32-bit. The `end < 4` check in the example catches this for `int` sizes where `4 + int(msgLen)` wraps.
- Go's `-race` detector finds data races, not bounds errors, but run it anyway: concurrent slice writes without synchronization are a corruption vector that looks like overflow in the crash dump.

## Tests to write
- `ReadPrefixedMessage` with a valid buffer returns the payload and remaining slice with correct lengths.
- Buffer shorter than 4 bytes returns `ErrBufferTooShort`, not a panic.
- Length prefix claiming more bytes than available returns `ErrBufferTooShort`.
- Length prefix of `MaxMessageSize + 1` returns `ErrMessageTooLarge`.
- `SafeIndex` with negative index, index at `len`, and index at `len-1` behave correctly.
- Run the test suite with `-race` and verify zero findings.
