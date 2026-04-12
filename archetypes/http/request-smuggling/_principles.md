---
schema_version: 1
archetype: http/request-smuggling
title: HTTP Request Smuggling
summary: Preventing HTTP desync attacks by ensuring consistent request boundary parsing across proxy-server chains.
applies_to: [csharp, python, go, java, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - smuggling
  - desync
  - transfer-encoding
  - content-length
  - http2
  - pipeline
  - chunked
  - proxy
  - frontend
  - backend
  - te-cl
  - cl-te
related_archetypes:
  - http/security-headers
references:
  owasp_asvs: V14.5
  owasp_cheatsheet: HTTP Request Smuggling
  cwe: "444"
---

# HTTP Request Smuggling — Principles

## When this applies
Any deployment where HTTP requests pass through more than one TCP connection parser before reaching the application: a CDN or load balancer in front of an application server, a reverse proxy (nginx, HAProxy, Envoy, AWS ALB) fronting a backend, or an HTTP/1.1 to HTTP/2 translation layer. The vulnerability exists in the boundary between two parsers that disagree on where one HTTP request ends and the next begins. It does not require the application itself to be buggy — the gap is between the proxy and the server. Even a perfectly written application is vulnerable if the infrastructure is misconfigured.

## Architectural placement
Mitigation lives at two levels: **infrastructure configuration** (the proxy-server pipeline) and **application-layer hardening** (how the server processes ambiguous headers). Infrastructure should enforce HTTP/2 or HTTP/3 end-to-end where possible, eliminating HTTP/1.1 pipeline parsing ambiguity. Where HTTP/1.1 is unavoidable, the server must reject requests with both `Content-Length` and `Transfer-Encoding` headers, and all proxies must be configured to do the same. The application layer adds defense-in-depth by validating and normalizing request sizes and rejecting oversized or malformed messages before they reach business logic.

## Principles
1. **Prefer HTTP/2 or HTTP/3 end-to-end.** HTTP/2 has explicit framing at the binary protocol level — there is no `Content-Length` vs `Transfer-Encoding` ambiguity. Upgrading from HTTP/1.1 to HTTP/2 on the proxy-to-server leg eliminates the desync surface entirely. HTTP/2 downgrade attacks (H2.CL, H2.TE) occur when a front-end accepts HTTP/2 but translates to HTTP/1.1 for the backend — avoid this pattern.
2. **Reject requests with both `Content-Length` and `Transfer-Encoding`.** RFC 9112 §6.3 states that if both headers are present, the server MUST treat it as an error. A compliant server must return 400 and close the connection; a compliant proxy must not forward such a request. Validate this at both layers.
3. **Normalize `Transfer-Encoding` before forwarding.** Obfuscated variants (`Transfer-Encoding: xchunked`, `Transfer-Encoding: chunked, identity`, `Transfer-Encoding:\x0bchunked`) are used to fool one parser while the other accepts the canonical value. Proxies must canonicalize or reject non-standard `Transfer-Encoding` values.
4. **Disable HTTP keep-alive and pipelining when using HTTP/1.1 end-to-end.** Connection reuse is the prerequisite for smuggling — each smuggled request piggybacks on a prior connection. If keep-alive is not required, disable it. If it is required, set conservative timeouts and maximum request counts per connection.
5. **Use unique per-backend connection pools, not shared.** When a reverse proxy multiplexes multiple clients' requests over a shared pool of backend connections, one client's smuggled data poisons the next client's request. Connection-per-request or per-user routing eliminates cross-client contamination.
6. **Enforce strict request line and header parsing.** Reject requests with bare CR in header values, ambiguous header folding, or header names containing non-token characters. These are crafted to be parsed differently by proxy and server.
7. **Set maximum header and body size limits.** A smuggled prefix exploits the difference in how two parsers count bytes. Enforcing tight content length limits reduces the practical window for payload delivery and limits the blast radius of a successful desync.
8. **Log and alert on parsing anomalies.** Requests that trigger 400 errors due to malformed headers, conflicting length headers, or oversized preambles are operational signals. Cluster-level alerting on elevated 400 rates from specific IPs or on `Transfer-Encoding` anomalies enables rapid detection.

## Anti-patterns
- Trusting the `Content-Length` header from a client-facing proxy and forwarding it to the backend unchanged — the proxy and backend may count the body differently.
- Running HTTP/1.1 on the proxy-to-backend leg when the frontend accepts HTTP/2 — this is the H2.CL and H2.TE desync surface.
- Ignoring `Transfer-Encoding` header validation because "the app doesn't use chunked encoding" — the headers are parsed by the server regardless of the application's intent.
- Sharing a backend connection pool across multiple clients on a high-throughput proxy without per-request isolation.
- Deploying a WAF in front of a vulnerable proxy/server configuration and expecting the WAF to detect smuggling — WAFs operate after the TCP layer and typically parse only one side of the desync.
- Disabling request size limits to accommodate file upload endpoints and applying that relaxation globally.
- Assuming that upgrading the proxy alone is sufficient — the backend server must also handle ambiguous headers consistently.
- Using `Transfer-Encoding: chunked` and `Content-Length` together in any request, even in internal service-to-service calls.

## References
- OWASP ASVS V14.5 — HTTP Request Validation
- OWASP HTTP Request Smuggling Cheat Sheet
- CWE-444 — Inconsistent Interpretation of HTTP Requests
- RFC 9112 §6.3 — Message Body Length
- PortSwigger Research — HTTP Request Smuggling (James Kettle)
