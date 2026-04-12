---
schema_version: 1
archetype: http/request-smuggling
language: csharp
principles_file: _principles.md
libraries:
  preferred: Kestrel (built-in ASP.NET Core HTTP server)
  acceptable:
    - YARP (Yet Another Reverse Proxy) — for proxy scenarios
  avoid:
    - name: System.Web (Classic ASP.NET / IIS pipeline)
      reason: Legacy IIS pipeline has known HTTP/1.1 desync quirks; use Kestrel or migrate to ASP.NET Core.
minimum_versions:
  dotnet: "10.0"
---

# HTTP Request Smuggling — C#

## Library choice
Kestrel, the default ASP.NET Core HTTP server, handles request boundary parsing correctly for HTTP/1.1 and HTTP/2. The primary mitigation surface is **configuration**: reject ambiguous requests, enforce HTTP/2 end-to-end where possible, and configure the reverse proxy (YARP, nginx, Caddy) to match Kestrel's parsing. For proxy deployments, YARP is the recommended .NET-native reverse proxy — it preserves HTTP/2 semantics end-to-end and avoids H2-to-H1 downgrade.

## Reference implementation
```csharp
// Program.cs — Kestrel configuration
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // Reject requests with both Content-Length and Transfer-Encoding.
    options.AllowSynchronousIO = false;

    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;   // 32 KB
    options.Limits.MaxRequestBodySize         = 30 * 1024 * 1024; // 30 MB

    options.ListenAnyIP(8080, listenOptions =>
    {
        // HTTP/2 only on the internal port (behind a TLS-terminating proxy).
        listenOptions.Protocols = HttpProtocols.Http2;
    });

    options.ListenAnyIP(8443, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        listenOptions.UseHttps();
    });
});

var app = builder.Build();

// Middleware: reject requests with both CL and TE (defense-in-depth).
app.Use(async (ctx, next) =>
{
    var headers = ctx.Request.Headers;
    bool hasCl = headers.ContainsKey("Content-Length");
    bool hasTe = headers.ContainsKey("Transfer-Encoding");

    if (hasCl && hasTe)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsync("Ambiguous request length headers.");
        return;
    }
    await next(ctx);
});

app.MapGet("/health", () => Results.Ok());
app.Run();
```

## Language-specific gotchas
- Kestrel in .NET 10 rejects requests with conflicting `Content-Length` and `Transfer-Encoding` by default and returns 400 — do not disable this behavior via `KestrelServerOptions.AllowResponseHeadersMutation` or similar overrides.
- Running behind IIS (InProcess hosting) re-introduces IIS's HTTP.sys parser in front of Kestrel. Prefer OutOfProcess hosting or Kestrel direct-listen when the proxy is not IIS.
- YARP (Microsoft reverse proxy): configure `ForwardedHeaders` correctly and verify `AllowChunkedEncoding` is not set to `true` in the cluster settings unless you have analyzed the backend compatibility.
- `HttpContext.Request.ContentLength` may be `null` when `Transfer-Encoding: chunked` is in use — treat `null` as unknown, not zero.
- gRPC over HTTP/2 (in ASP.NET Core): gRPC framing eliminates HTTP/1.1 smuggling — this is a benefit of the HTTP/2-native protocol.
- Integration with nginx upstream: ensure `proxy_http_version 1.1` is paired with `proxy_set_header Connection ""` to disable pipelining, or upgrade to gRPC/HTTP2 upstream.

## Tests to write
- Send a request with both `Content-Length: 0` and `Transfer-Encoding: chunked` — expect 400.
- Send a request with `Transfer-Encoding: chunked` only — expect normal processing.
- Kestrel integration: verify `HttpProtocols.Http2` is enforced on the internal port and only HTTP/2 connections succeed.
- Middleware unit test: `hasCl && hasTe` path returns 400 with the error body.
- Load test through YARP: verify no connection reuse leaks request data across clients.
