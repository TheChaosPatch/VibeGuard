---
schema_version: 1
archetype: crypto/tls-configuration
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Net.Security.SslStream
  acceptable:
    - Microsoft.AspNetCore.Server.Kestrel.Https
    - System.Net.Http.HttpClientHandler
  avoid:
    - name: ServerCertificateCustomValidationCallback returning true
      reason: Disables all certificate validation; the single most common TLS vulnerability in .NET code.
    - name: SslProtocols.Tls (TLS 1.0)
      reason: Deprecated protocol with known attacks; removed from browser support.
minimum_versions:
  dotnet: "10.0"
---

# TLS Configuration -- C#

## Library choice
.NET's TLS stack is built on `System.Net.Security.SslStream`, which delegates to the OS provider (Schannel on Windows, OpenSSL on Linux). For HTTP clients, `HttpClientHandler` and `SocketsHttpHandler` expose TLS settings through `SslOptions`. For ASP.NET Core servers, Kestrel's `ListenOptions.UseHttps` configures the server certificate and protocol versions. The platform default in .NET 10 already disables TLS 1.0/1.1, but explicit configuration documents intent and survives deployment to hosts with older OS defaults.

## Reference implementation
```csharp
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

public static class TlsDefaults
{
    // Outbound HTTPS: TLS 1.2+, full certificate validation.
    public static SslClientAuthenticationOptions SecureClientOptions(
        string targetHost) => new()
    {
        TargetHost = targetHost,
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        CertificateRevocationCheckMode = X509RevocationMode.Online,
    };

    // Inbound mTLS: requires client cert signed by a specific CA.
    public static SslServerAuthenticationOptions MtlsServerOptions(
        X509Certificate2 serverCert,
        X509Certificate2 clientCaCert) => new()
    {
        ServerCertificate = serverCert,
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        ClientCertificateRequired = true,
        RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
        {
            if (errors != SslPolicyErrors.None || cert is null || chain is null)
                return false;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(clientCaCert);
            return chain.Build(new X509Certificate2(cert));
        },
    };
}

// Kestrel: builder.WebHost.ConfigureKestrel(k =>
//     k.ConfigureHttpsDefaults(h => {
//         h.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
//     }));
```

## Language-specific gotchas
- `SslProtocols.None` means "let the OS decide." On modern .NET this is safe, but it is implicit -- an auditor cannot tell whether TLS 1.0 is allowed without checking the host's registry. Explicit `Tls12 | Tls13` is self-documenting.
- `ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator` is named "Dangerous" for a reason. If it appears outside a test project, it is a finding.
- `HttpClient` should be long-lived (singleton or via `IHttpClientFactory`). Creating a new `HttpClient` per request causes socket exhaustion *and* re-negotiates TLS on every call.
- For Kestrel HSTS, call `app.UseHsts()` and configure `HstsOptions` with `MaxAge = TimeSpan.FromDays(365)` and `IncludeSubDomains = true`. The middleware only emits the header over HTTPS -- it will not break local HTTP development.
- Loading certificates from PFX files requires `X509KeyStorageFlags.MachineKeySet | EphemeralKeySet` on Linux to avoid permission issues with the user keychain. Prefer loading from the OS certificate store or a secrets manager.
- When using `X509RevocationMode.Online`, OCSP/CRL checks add latency. For high-throughput services, consider OCSP stapling at the reverse proxy (nginx, Envoy) and `X509RevocationMode.NoCheck` at the application layer -- but document this tradeoff.

## Tests to write
- Protocol enforcement: create an `SslStream` with `SecureClientOptions` against a test server offering only TLS 1.0, assert handshake failure.
- Certificate validation: connect to a server with an expired or self-signed certificate, assert `AuthenticationException`.
- mTLS rejection: connect without a client certificate when `ClientCertificateRequired = true`, assert handshake failure.
- HSTS header: send a request to the HTTPS endpoint, assert `Strict-Transport-Security` header is present with `max-age >= 31536000`.
- No dangerous callback: scan the codebase for `DangerousAcceptAnyServerCertificateValidator` outside test assemblies (static analysis test).
