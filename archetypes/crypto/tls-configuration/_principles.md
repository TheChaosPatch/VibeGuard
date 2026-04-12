---
schema_version: 1
archetype: crypto/tls-configuration
title: TLS Configuration
summary: Configuring TLS clients and servers for secure transport with modern protocol versions and cipher suites.
applies_to: [csharp, python, go]
status: draft
keywords:
  - tls
  - ssl
  - https
  - certificate
  - cipher-suite
  - hsts
  - mtls
  - certificate-pinning
  - transport-security
  - x509
related_archetypes:
  - crypto/key-management
  - crypto/symmetric-encryption
  - http/ssrf
references:
  owasp_asvs: V9.1
  owasp_cheatsheet: Transport Layer Security Cheat Sheet
  cwe: "295"
---

# TLS Configuration -- Principles

## When this applies
Any time your application opens a network connection that carries sensitive data: HTTPS clients calling external APIs, gRPC channels between microservices, database connections, message-broker links, SMTP/IMAP, or any custom TCP socket. If the channel crosses a trust boundary -- even between containers on the same host -- it needs TLS. "It's internal traffic" is not a valid exemption; it is a description of the blast radius.

## Architectural placement
TLS configuration belongs in infrastructure setup code -- the HTTP client factory, the Kestrel/server builder, the gRPC channel options -- not scattered across individual call sites. A single `ConfigureTls` extension method or factory function sets the minimum protocol version, the allowed cipher suites, and the certificate validation policy. Individual services consume a pre-configured client or listener and never touch `SslProtocols` or `ServerCertificateCustomValidationCallback` directly. This prevents the "one team disabled validation for testing and forgot to re-enable it" failure mode.

## Principles
1. **TLS 1.2 is the floor; TLS 1.3 is the target.** Disable SSLv3, TLS 1.0, and TLS 1.1 unconditionally. TLS 1.0/1.1 have known downgrade attacks and are removed from browser support. TLS 1.3 eliminates entire classes of vulnerabilities (renegotiation, static RSA key exchange) and reduces handshake latency.
2. **Never disable certificate validation in production.** `ServerCertificateCustomValidationCallback = (_, _, _, _) => true` is the most common TLS vulnerability in application code. If you need to trust a private CA, add that CA to the trust store -- do not bypass the entire chain.
3. **Use strong cipher suites only.** Prefer AEAD ciphers (AES-GCM, ChaCha20-Poly1305) with ECDHE key exchange. Disable CBC-mode ciphers, RC4, 3DES, and any cipher with export-grade key lengths. On TLS 1.3, the cipher suite selection is already restricted to strong options by the protocol itself.
4. **Enable HSTS for all HTTP responses.** `Strict-Transport-Security: max-age=31536000; includeSubDomains` tells browsers to refuse plaintext connections. Without HSTS, a network attacker can strip TLS via an SSL-stripping proxy on the first connection. Set the max-age to at least one year.
5. **Use mutual TLS (mTLS) for service-to-service communication.** When both sides are services you control, mTLS authenticates *both* endpoints. This eliminates the risk of a compromised network peer impersonating a service. Issue short-lived certificates from an internal CA and automate rotation.
6. **Validate the full certificate chain, including revocation.** Check expiration, chain trust, hostname match, and revocation status (OCSP stapling preferred, CRL as fallback). A valid-looking certificate from a compromised CA that has been revoked is not a valid certificate.
7. **Pin certificates or public keys only when you control both ends.** Pinning prevents CA compromise attacks but creates operational risk: a botched rotation causes a hard outage. If you pin, pin the *public key* (SPKI hash), not the leaf certificate, and always include a backup pin. For public APIs you consume, do not pin -- you cannot control their rotation schedule.
8. **Automate certificate renewal.** Manual certificate management is the leading cause of TLS outages. Use ACME (Let's Encrypt), a cloud provider's managed certificate service, or an internal CA with automated issuance. Monitor certificate expiration with alerting at 30, 14, and 7 days.

## Anti-patterns
- Disabling certificate validation "temporarily for local development" and shipping the flag to production.
- Allowing TLS 1.0/1.1 "for backward compatibility" with clients that should have been upgraded years ago.
- Hardcoding a certificate thumbprint instead of trusting the CA chain (breaks on every renewal).
- Using a self-signed certificate in production without adding it to the system trust store.
- Ignoring `SslPolicyErrors` in a custom validation callback -- the callback exists for *additional* checks, not to remove the default ones.
- Setting `max-age=0` on HSTS during "testing" and forgetting to raise it.
- Shipping a PFX/PKCS12 file with the private key inside the container image.
- Logging the TLS handshake at a level that includes the pre-master secret.

## References
- OWASP ASVS V9.1 -- Communications Security
- OWASP Transport Layer Security Cheat Sheet
- CWE-295 -- Improper Certificate Validation
