---
schema_version: 1
archetype: io/file-upload
language: go
principles_file: _principles.md
libraries:
  preferred: net/http (multipart handling) + net/http.DetectContentType
  acceptable:
    - github.com/h2non/filetype (extended magic-byte detection)
    - golang.org/x/image (decode/re-encode for image sanitization)
  avoid:
    - name: mime.TypeByExtension
      reason: Determines type from the file extension, which is caller-controlled and trivially spoofed.
    - name: Trusting the Content-Type header from multipart.FileHeader
      reason: Caller-supplied string with no server-side validation of actual content.
minimum_versions:
  go: "1.22"
---

# Secure File Upload Handling -- Go

## Library choice
Go's standard library handles multipart parsing via `net/http`'s `Request.ParseMultipartForm` and `Request.FormFile`. For content-type detection, `http.DetectContentType` reads the first 512 bytes and applies the MIME sniffing algorithm -- this is a real content inspection, not an extension lookup. For broader format support beyond the standard library's built-in signatures, `github.com/h2non/filetype` provides extensive magic-byte matching. `mime.TypeByExtension` guesses from the extension and must never be used as a security control.

## Reference implementation
```go
package upload

import (
	"crypto/rand"
	"encoding/hex"
	"io"
	"net/http"
	"os"
	"path/filepath"
	"strings"
)

var allowed = map[string]string{"image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp"}

// SaveUpload validates content by magic bytes and stores under a random name.
func SaveUpload(root string, src io.ReadSeeker) (string, error) {
	hdr := make([]byte, 512)
	n, _ := io.ReadAtLeast(src, hdr, 1)
	ext, ok := allowed[http.DetectContentType(hdr[:n])]
	if !ok {
		return "", ErrTypeNotAllowed
	}
	_, _ = src.Seek(0, io.SeekStart)
	id := make([]byte, 16)
	_, _ = rand.Read(id)
	name := hex.EncodeToString(id) + ext
	dest := filepath.Join(root, name)
	if !strings.HasPrefix(dest, root+string(filepath.Separator)) {
		return "", ErrPathEscape
	}
	out, err := os.OpenFile(dest, os.O_CREATE|os.O_WRONLY|os.O_EXCL, 0o640)
	if err != nil {
		return "", err
	}
	defer out.Close()
	_, err = io.Copy(out, src)
	if err != nil {
		os.Remove(dest)
		return "", err
	}
	return name, nil
}
```

## Language-specific gotchas
- `http.MaxBytesReader` wraps `r.Body` and returns an error once the limit is exceeded, preventing the full oversized body from being buffered. Set it before calling `ParseMultipartForm` or `FormFile`. Without it, Go will happily buffer an arbitrarily large request body.
- `http.DetectContentType` implements the WHATWG MIME sniffing spec over the first 512 bytes. It is content-based, but its signature set is limited. For production systems handling diverse file types, pair it with `github.com/h2non/filetype` for more precise detection.
- `multipart.FileHeader.Filename` is the client-supplied filename from the `Content-Disposition` header. It can contain path separators, absolute paths, or be empty. Never use it in `filepath.Join` or any filesystem operation.
- `os.OpenFile` with `O_EXCL` ensures the file is created fresh and fails if it already exists, preventing race-condition overwrites. Always use it for upload destinations.
- Go's `multipart` reader stores small files in memory and large files in temp files (controlled by `maxMemory` in `ParseMultipartForm`). Set this value deliberately -- the default 32 MiB may be too generous for your threat model.
- When serving uploads back, set `Content-Disposition: attachment` and an explicit `Content-Type` from your allowlist. Never call `http.ServeFile` on upload directories -- it enables directory listing and serves files with sniffed content types.

## Tests to write
- Happy path: a valid JPEG under the size limit returns a JSON response with a random filename ID.
- Oversized request: a body exceeding `maxUploadBytes` returns 400 before the full body is read.
- Wrong type: a PDF uploaded as `file` field returns 415 Unsupported Media Type.
- Missing file field: a request with no `file` part returns 400.
- Concurrent uploads: two simultaneous uploads with the same original filename produce distinct storage names (no collision or overwrite).
