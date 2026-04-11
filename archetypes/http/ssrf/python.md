---
schema_version: 1
archetype: http/ssrf
language: python
principles_file: _principles.md
libraries:
  preferred: httpx
  acceptable:
    - aiohttp
    - urllib3
  avoid:
    - name: requests (for user-influenced URLs)
      reason: Fine library, but its convenience functions encourage passing a raw string to .get() without a validation layer; the one-line SSRF is too easy.
    - name: urllib.request.urlopen
      reason: Supports file://, ftp://, and other schemes by default; follows redirects silently.
minimum_versions:
  python: "3.11"
---

# SSRF Defense — Python

## Library choice
`httpx` is the stock answer for a safe external fetcher. It has `follow_redirects=False` as an explicit option, exposes the full request lifecycle through `httpx.Client(transport=...)` so you can inject a validation layer, and its `Client` has a named `base_url` hook that separates "this client only talks to host X" from "any host." `aiohttp` is equally acceptable and more idiomatic if you're already on asyncio. `urllib3` is fine as a lower-level alternative with explicit pool handling. Avoid raw `requests.get(user_url)` — the library is fine, but its ergonomics push you toward the one-line unsafe pattern. Avoid `urllib.request.urlopen` entirely for user-influenced URLs; it silently supports schemes like `file://` that have no business in an HTTP client.

## Reference implementation
```python
from __future__ import annotations
import ipaddress
import socket
import httpx

_ALLOWED_SCHEMES = frozenset({"http", "https"})
_TIMEOUT = httpx.Timeout(5.0, connect=2.0)


class BlockedUrlError(Exception):
    pass


def _is_public(ip: ipaddress.IPv4Address | ipaddress.IPv6Address) -> bool:
    # is_link_local covers 169.254.0.0/16 including the cloud metadata address.
    return not (ip.is_private or ip.is_loopback or ip.is_link_local
                or ip.is_reserved or ip.is_multicast or ip.is_unspecified)


def safe_fetch(url: str, max_bytes: int = 8 * 1024 * 1024) -> bytes:
    parsed = httpx.URL(url)
    if parsed.scheme not in _ALLOWED_SCHEMES:
        raise BlockedUrlError(f"scheme {parsed.scheme!r} not allowed")
    if not parsed.host:
        raise BlockedUrlError("missing host")

    # Resolve every address and validate each one — all addresses must be public.
    try:
        infos = socket.getaddrinfo(parsed.host, parsed.port or 443, type=socket.SOCK_STREAM)
    except OSError as e:
        raise BlockedUrlError("dns failure") from e
    for info in infos:
        if not _is_public(ipaddress.ip_address(info[4][0])):
            raise BlockedUrlError(f"{info[4][0]} is not a public address")

    # Pin to the first validated IP and carry the original Host header,
    # so DNS rebinding can't swap the target between validation and connect.
    pinned = parsed.copy_with(host=infos[0][4][0])
    with httpx.Client(follow_redirects=False, timeout=_TIMEOUT, verify=True) as c:
        with c.stream("GET", pinned, headers={"Host": parsed.host}) as resp:
            resp.raise_for_status()
            out, total = bytearray(), 0
            for chunk in resp.iter_bytes():
                total += len(chunk)
                if total > max_bytes:
                    raise BlockedUrlError("response exceeded size cap")
                out.extend(chunk)
            return bytes(out)
```

## Language-specific gotchas
- `ipaddress.IPv4Address.is_private` does not include `169.254.169.254` (the cloud metadata address). It *is* link-local, so `is_link_local` catches it — but make the check explicit so a future change to the stdlib definition doesn't silently regress.
- `socket.getaddrinfo` can return multiple addresses; you must validate *all* of them, not just the first. Otherwise an attacker can serve a DNS response with `[203.0.113.1, 127.0.0.1]` and your code validates the first and connects to the second. The stdlib picks one, but which one is implementation-defined.
- DNS pinning: after validation, rewrite the URL to use the literal IP and set `Host:` explicitly. Otherwise httpx resolves the name a second time at connection time and you've re-opened the rebinding window.
- `follow_redirects=False` is load-bearing. If you need to follow redirects, wrap `safe_fetch` in a loop that re-validates the `Location` header with `safe_fetch` on each hop, with a max-hop cap.
- `httpx.URL` parses correctly in edge cases where string operations fail: userinfo, percent-encoding, IPv6 brackets. Use it rather than splitting the URL by hand.
- `verify=True` is the default but make it explicit so a future "let's disable verification in staging" change shows up in review.
- Do not carry `request.headers["authorization"]` from your inbound handler into `safe_fetch`. Start with an empty header dict and add only what the outbound call explicitly needs.

## Tests to write
- Loopback blocked: `safe_fetch("http://127.0.0.1/")`, `http://[::1]/`, `http://localhost/` all raise `BlockedUrlError`.
- Metadata blocked: `http://169.254.169.254/latest/meta-data/` raises.
- Private ranges blocked: 10/8, 172.16/12, 192.168/16 all raise.
- Scheme blocked: `file:///etc/passwd`, `ftp://...`, `gopher://...` raise.
- Encoded IP blocked: `http://0x7f000001/` raises (DNS resolution must normalize it).
- DNS-rebind scenario: monkeypatch `socket.getaddrinfo` for `attacker.com` to return `127.0.0.1`; assert `BlockedUrlError`.
- Mixed-resolution scenario: getaddrinfo returns `[1.2.3.4, 127.0.0.1]`; assert the call is blocked (all addresses must be public).
- Redirect not followed: a stub server returns 302 to `http://169.254.169.254/`; assert the 302 comes back and is not auto-followed.
- Size cap: a stub server streams 9 MiB; assert `BlockedUrlError` after 8 MiB.
- No credential forwarding: monkey-test that `safe_fetch` does not carry through environment vars that look like credentials.
