---
schema_version: 1
archetype: http/ssrf
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Net.Http.HttpClient
  acceptable:
    - Microsoft.Extensions.Http (IHttpClientFactory)
    - Polly (resilience policies)
  avoid:
    - name: HttpWebRequest
      reason: Legacy, awkward cancellation, poor redirect handling for safety checks.
    - name: WebClient
      reason: Obsolete, no DNS pinning hooks, no per-request redirect callback.
minimum_versions:
  dotnet: "10.0"
---

# SSRF Defense — C#

## Library choice
`HttpClient` via `IHttpClientFactory` is the stock answer. The factory lets you register a *named* client — `"safe-external-fetch"` — with a fixed set of handlers: a custom `SocketsHttpHandler` that disables automatic redirects, and a `DelegatingHandler` that runs URL validation and DNS pinning on every request. That named client is the only one allowed to fetch user-influenced URLs; a second client with different (stricter) policy serves internal fetches. The factory also handles connection pooling and socket lifetime correctly, which matters because a long-lived client with stale DNS is exactly the condition DNS-rebinding exploits.

## Reference implementation
```csharp
using System.Net;
using System.Net.Sockets;

public sealed class SafeFetchHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri ?? throw new BlockedUrlException("no uri");
        if (uri.Scheme is not ("http" or "https"))
            throw new BlockedUrlException($"scheme {uri.Scheme} not allowed");

        // Resolve every address up front, validate each one, then pin.
        var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        if (addresses.Length == 0) throw new BlockedUrlException("no addresses");
        foreach (var addr in addresses)
            if (!IsPublic(addr))
                throw new BlockedUrlException($"{addr} is not a public address");

        // Rewrite to the literal IP so DNS rebinding can't swap on reconnect.
        var pinned = new UriBuilder(uri) { Host = addresses[0].ToString() }.Uri;
        request.RequestUri = pinned;
        request.Headers.Host = uri.Host;
        return await base.SendAsync(request, ct);
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && !ip.IsIPv6Multicast;
        return ip.GetAddressBytes() switch
        {
            [10, ..] => false,
            [172, >= 16 and <= 31, ..] => false,
            [192, 168, ..] => false,
            [169, 254, ..] => false, // link-local + cloud metadata (169.254.169.254)
            _ => true,
        };
    }
}
```

## Language-specific gotchas
- `AllowAutoRedirect = false` on the `SocketsHttpHandler` is non-negotiable. If redirects are on, the handler's `SendAsync` only sees the first URL, and a 302 to a private address defeats every check.
- DNS pinning is the line that prevents the DNS-rebinding class of attacks. Resolve once, validate, and rewrite the URI to use the literal address before the connection happens. `SocketsHttpHandler.ConnectCallback` is a more correct (but more involved) hook for the same thing in production code.
- `Uri.IsLoopback` catches `127.0.0.1`, `::1`, and `localhost`, but it does *not* catch `127.1` (which parses as `127.0.0.1`), octal forms, or hex-encoded IPs in the host component. Always resolve and validate the *bytes*, not the string.
- `IPAddress.IsIPv6LinkLocal` exists; `IsIPv4LinkLocal` does not. Write the 169.254/16 check by hand.
- Do not pass the inbound `HttpContext.Request.Headers["Authorization"]` through on outbound calls. The named client should have an empty default header set and add only the outbound credentials it explicitly needs.
- `HttpClient.Timeout` is a coarse per-operation timeout; prefer a `CancellationTokenSource` with `CancelAfter` for a total deadline, and use `ReadAsStream` + `LimitStream` to cap response size.
- `IHttpClientFactory` reuses handlers for ~2 minutes by default; if your IP-allowlist policy is dynamic, pass a snapshot through to `SafeFetchHandler` rather than reading global state inside `SendAsync`.

## Tests to write
- Loopback blocked: `client.GetAsync("http://127.0.0.1/")` throws `BlockedUrlException`.
- IPv6 loopback blocked: `http://[::1]/` throws.
- Metadata blocked: `http://169.254.169.254/latest/meta-data/` throws.
- Private ranges blocked: `http://10.0.0.1/`, `http://192.168.1.1/`, `http://172.16.0.1/` all throw.
- Scheme blocked: `file:///etc/passwd`, `gopher://...`, `dict://...` all throw.
- DNS-rebind scenario: stub `Dns.GetHostAddressesAsync` to return `127.0.0.1` for `attacker.com`; assert the request is blocked.
- Redirect not followed: a test server returns 302 to `http://169.254.169.254/`; assert the client returns the 302 and does *not* follow it.
- Public address allowed: a deliberate fetch to a known public host succeeds (use a test server on a public IP in CI or a mocked DNS).
- Header stripping: the outbound request does not carry any `Authorization` header from the inbound context.
