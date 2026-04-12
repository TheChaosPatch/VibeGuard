---
schema_version: 1
archetype: http/content-security-policy
language: php
principles_file: _principles.md
libraries:
  preferred: paragonie/csp-builder
  acceptable:
    - bepsvpt/secure-headers (Laravel)
    - Manual header via header() with random_bytes()
  avoid:
    - name: Meta tag only
      reason: frame-ancestors and report-uri are not honoured in meta tags.
minimum_versions:
  php: "8.3"
---

# Content Security Policy — PHP

## Library choice
`paragonie/csp-builder` provides an immutable CSP policy object built from a JSON/array configuration, with nonce generation via `random_bytes`. For Laravel, `bepsvpt/secure-headers` integrates as middleware with a config file. Plain PHP applications can set the header directly with `header()` using a nonce from `base64_encode(random_bytes(16))`. The nonce must be generated once per request before any output is sent.

## Reference implementation
```php
<?php
// bootstrap/csp.php — include before any output
declare(strict_types=1);

use ParagonIE\CSPBuilder\CSPBuilder;

$nonce = base64_encode(random_bytes(16));

$csp = CSPBuilder::fromArray([
    'default-src' => ['self' => false, 'allow' => []],
    'script-src'  => [
        'self'          => false,
        'allow'         => [],
        'nonces'        => [$nonce],
        'strict-dynamic'=> true,
    ],
    'style-src'   => ['self' => true, 'nonces' => [$nonce]],
    'img-src'     => ['self' => true, 'data'   => true],
    'connect-src' => ['self' => true],
    'font-src'    => ['self' => true],
    'form-action' => ['self' => true],
    'frame-ancestors' => ['self' => false, 'allow' => []],
    'base-uri'    => ['self' => true],
    'upgrade-insecure-requests' => true,
]);

$csp->addHeader();      // sets Content-Security-Policy via header()
// Pass $nonce to templates
```

```php
// In a Laravel service provider or middleware
// config/secure-headers.php (bepsvpt/secure-headers)
return [
    'content-security-policy' => [
        'enable'    => true,
        'report-only' => false,
        'default-src' => ["'none'"],
        'script-src'  => ["'strict-dynamic'"],   // nonce injected by middleware
        'style-src'   => ["'self'"],
        'img-src'     => ["'self'", "data:"],
        'connect-src' => ["'self'"],
        'frame-ancestors' => ["'none'"],
        'base-uri'    => ["'self'"],
        'form-action' => ["'self'"],
    ],
];
```

## Language-specific gotchas
- `random_bytes(16)` requires PHP 7+ and is cryptographically secure. Never use `mt_rand`, `rand`, or `uniqid` for nonce generation.
- `header()` must be called before any output, including whitespace before the opening `<?php` tag. Use `ob_start()` / `ob_end_flush()` if output may occur before the CSP header is set.
- `paragonie/csp-builder` is immutable after construction — build the policy once per request with the freshly generated nonce.
- Twig templates: pass the nonce as a template variable and render as `<script nonce="{{ csp_nonce }}">`. Twig auto-escapes by default, so the nonce value is safe to render directly.
- Laravel Blade: `{!! $cspNonce !!}` is unnecessary — the nonce is alphanumeric base64, so `{{ $cspNonce }}` (escaped) is identical in output. Use the escaped version for clarity.
- `bepsvpt/secure-headers` generates the nonce internally; retrieve it via `app(SecureHeadersManager::class)->nonce()` or the `csp_nonce()` helper in Blade.
- PHP-FPM with opcode cache: ensure the nonce generation is at request scope, not module scope — `random_bytes` called at class-static initialization time would reuse the same value.

## Tests to write
- PHPUnit HTTP test: response has `Content-Security-Policy` header with `nonce-[a-zA-Z0-9+/=]{24}`.
- Two requests yield different nonce values.
- Template output contains `nonce="X"` matching the header nonce.
- `frame-ancestors 'none'` present in every HTML response.
- `header()` call order: no output before CSP header (check ob_level in test bootstrap).
