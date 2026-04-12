---
schema_version: 1
archetype: crypto/tls-configuration
language: python
principles_file: _principles.md
libraries:
  preferred: ssl (stdlib)
  acceptable:
    - certifi
    - httpx
    - aiohttp with ssl context
  avoid:
    - name: ssl.create_default_context() with check_hostname=False
      reason: Disables hostname verification; MITM-trivial.
    - name: requests with verify=False
      reason: Disables all certificate validation; the Python equivalent of accepting any cert.
    - name: urllib3.disable_warnings()
      reason: Hides the security warning instead of fixing the root cause.
minimum_versions:
  python: "3.10"
---

# TLS Configuration -- Python

## Library choice
Python's `ssl` module wraps OpenSSL and provides `SSLContext` for both client and server configuration. `ssl.create_default_context()` returns a context with sensible defaults (TLS 1.2+, certificate verification enabled, hostname checking on). Use it as the starting point and tighten -- never loosen. For HTTP clients, `httpx` and `requests` both accept an `SSLContext` or `verify` parameter. `certifi` provides Mozilla's CA bundle and is the default trust store for `requests` and `httpx`. Do not set `verify=False` -- ever. If you need to trust a private CA, pass its PEM path to `verify` or load it into a custom context.

## Reference implementation
```python
from __future__ import annotations
import ssl
from pathlib import Path


def secure_client_context(
    *,
    client_cert: Path | None = None,
    client_key: Path | None = None,
    ca_bundle: Path | None = None,
) -> ssl.SSLContext:
    """Client SSL context: TLS 1.2+, full verification, optional mTLS."""
    ctx = ssl.create_default_context(cafile=str(ca_bundle) if ca_bundle else None)
    ctx.minimum_version = ssl.TLSVersion.TLSv1_2
    ctx.maximum_version = ssl.TLSVersion.MAXIMUM_SUPPORTED
    # Defaults: check_hostname=True, verify_mode=CERT_REQUIRED
    if client_cert and client_key:
        ctx.load_cert_chain(certfile=str(client_cert), keyfile=str(client_key))
    return ctx


def strict_server_context(
    cert_path: Path,
    key_path: Path,
    *,
    client_ca: Path | None = None,
) -> ssl.SSLContext:
    """Server SSL context: TLS 1.2+, strong ciphers, optional mTLS."""
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    ctx.minimum_version = ssl.TLSVersion.TLSv1_2
    ctx.maximum_version = ssl.TLSVersion.MAXIMUM_SUPPORTED
    ctx.set_ciphers(
        "ECDHE+AESGCM:ECDHE+CHACHA20:DHE+AESGCM:DHE+CHACHA20:!aNULL:!MD5:!DSS"
    )
    ctx.load_cert_chain(certfile=str(cert_path), keyfile=str(key_path))
    if client_ca:
        ctx.verify_mode = ssl.CERT_REQUIRED
        ctx.load_verify_locations(cafile=str(client_ca))
    return ctx


# Usage with httpx:
# import httpx
# ctx = secure_client_context(ca_bundle=Path("/etc/ssl/internal-ca.pem"))
# client = httpx.Client(verify=ctx)
# resp = client.get("https://internal-service/health")
```

## Language-specific gotchas
- `ssl.create_default_context()` already sets `check_hostname = True` and `verify_mode = CERT_REQUIRED`. Do not undo these. Any code that sets `check_hostname = False` or `verify_mode = CERT_NONE` is a finding.
- `requests.get(url, verify=False)` disables validation and prints a warning. `urllib3.disable_warnings()` hides the warning. Both together are the "cover your ears" pattern -- the vulnerability is still there.
- Python's `ssl` module links against the system OpenSSL. On older distros, OpenSSL may default to allowing TLS 1.0. Setting `minimum_version = TLSv1_2` explicitly overrides whatever the system default is.
- `ctx.set_ciphers()` accepts an OpenSSL cipher string. The string in the reference implementation allows only AEAD ciphers with ephemeral key exchange. Test the result with `ctx.get_ciphers()` to verify no weak suites sneak in.
- For HSTS in a Python web framework (Django, FastAPI), add the header via middleware. Django: `SECURE_HSTS_SECONDS = 31536000`, `SECURE_HSTS_INCLUDE_SUBDOMAINS = True`. FastAPI: add a `Middleware` that sets the header on every HTTPS response.
- When loading PEM keys, never embed them as string literals in source code. Load from files or a secrets manager. The key file should be `chmod 600` and owned by the service user.

## Tests to write
- Protocol floor: connect to a server offering only TLS 1.0 using `secure_client_context`, assert `ssl.SSLError`.
- Certificate validation: connect to a server with a self-signed cert not in the CA bundle, assert `ssl.SSLCertVerificationError`.
- Hostname mismatch: connect to `localhost` with a cert issued for `example.com`, assert `ssl.SSLCertVerificationError`.
- mTLS enforcement: connect without a client cert to a server with `CERT_REQUIRED`, assert handshake failure.
- Cipher strength: call `ctx.get_ciphers()` on `strict_server_context` and assert no cipher name contains `RC4`, `DES`, `MD5`, or `NULL`.
- No verify=False: scan the codebase for `verify=False` and `check_hostname\s*=\s*False` outside test files (static analysis test).
