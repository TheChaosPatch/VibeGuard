---
schema_version: 1
archetype: crypto/tls-configuration
language: go
principles_file: _principles.md
libraries:
  preferred: crypto/tls
  acceptable:
    - crypto/x509
    - net/http (with configured Transport)
  avoid:
    - name: tls.Config with InsecureSkipVerify set to true
      reason: Disables all certificate validation; trivially MITM-able.
    - name: tls.Config with MinVersion below tls.VersionTLS12
      reason: TLS 1.0/1.1 have known protocol-level attacks and are deprecated.
minimum_versions:
  go: "1.22"
---

# TLS Configuration -- Go

## Library choice
Go's `crypto/tls` package is a pure-Go TLS implementation that ships with the standard library. It does not depend on OpenSSL. `tls.Config` controls protocol versions, cipher suites, certificate loading, and client authentication. Since Go 1.18, the default minimum version is TLS 1.2 and the default cipher suite preference is server-side with strong AEAD ciphers first. Explicit configuration is still recommended because it documents intent and survives edge cases where code is compiled with `GOFLAGS` that lower defaults. For HTTP, `net/http`'s `Transport` and `Server` both accept a `*tls.Config`.

## Reference implementation
```go
package tlsconf

import (
	"crypto/tls"
	"crypto/x509"
	"fmt"
	"os"
)

type ClientOption func(*tls.Config)

func SecureClientConfig(opts ...ClientOption) *tls.Config {
	cfg := &tls.Config{MinVersion: tls.VersionTLS12}
	for _, o := range opts {
		o(cfg)
	}
	return cfg
}

func WithClientCert(certFile, keyFile string) ClientOption {
	return func(cfg *tls.Config) {
		cert, err := tls.LoadX509KeyPair(certFile, keyFile)
		if err != nil {
			panic(fmt.Sprintf("tlsconf: load client cert: %v", err))
		}
		cfg.Certificates = []tls.Certificate{cert}
	}
}

func WithCustomCA(caFile string) ClientOption {
	return func(cfg *tls.Config) {
		pem, err := os.ReadFile(caFile)
		if err != nil {
			panic(fmt.Sprintf("tlsconf: read CA: %v", err))
		}
		pool, _ := x509.SystemCertPool()
		if pool == nil {
			pool = x509.NewCertPool()
		}
		pool.AppendCertsFromPEM(pem)
		cfg.RootCAs = pool
	}
}
```

## Language-specific gotchas
- `InsecureSkipVerify: true` is the Go equivalent of `verify=False`. If it appears outside a test file, it is a finding. Go's `golangci-lint` with `gosec` flags this as G402.
- Go's default cipher suite selection (when `CipherSuites` is nil) is excellent since Go 1.17: it prefers TLS 1.3 suites, then AES-GCM with ECDHE, then ChaCha20. Overriding `CipherSuites` manually is usually worse than the default. Only set it when compliance requires an explicit allowlist.
- `tls.LoadX509KeyPair` loads from PEM files. If the key is encrypted (passphrase-protected), you must decrypt it first with `x509.DecryptPEMBlock` (deprecated) or use a secrets manager that provides the decrypted key. Do not store unencrypted private keys in the container image.
- For mTLS servers, set `ClientAuth: tls.RequireAndVerifyClientCert` and populate `ClientCAs` with the CA pool that issued the client certificates. `tls.RequestClientCert` without verification is not mTLS -- it requests but does not enforce.
- For HSTS, add the header in your HTTP middleware. Go does not set it automatically. A one-liner middleware: `w.Header().Set("Strict-Transport-Security", "max-age=31536000; includeSubDomains")`.
- `x509.SystemCertPool()` returns an error on Windows in some Go versions. The fallback to `x509.NewCertPool()` in the reference implementation handles this, but only trusts the explicitly loaded CA in that case.
- Certificate expiry monitoring is your responsibility. Go does not warn when a loaded certificate is near expiry. Use a goroutine or external monitoring that checks `cert.Leaf.NotAfter` and alerts at 30/14/7 days.

## Tests to write
- Protocol enforcement: start a test TLS server with `MaxVersion: tls.VersionTLS10`, connect with `SecureClientConfig`, assert handshake error.
- Certificate validation: connect to a server with a self-signed cert not in the CA pool, assert error.
- mTLS required: start a server with `RequireAndVerifyClientCert`, connect without a client cert, assert handshake error.
- Custom CA: use `WithCustomCA` to trust a test CA, connect to a server using a cert from that CA, assert success.
- No InsecureSkipVerify: scan non-test Go files for `InsecureSkipVerify:\s*true` (static analysis test).
- HSTS header: send a request to the HTTPS test server, assert the `Strict-Transport-Security` header is present.
