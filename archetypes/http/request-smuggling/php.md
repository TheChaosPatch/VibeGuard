---
schema_version: 1
archetype: http/request-smuggling
language: php
principles_file: _principles.md
libraries:
  preferred: FrankenPHP (Caddy-embedded, HTTP/2 and HTTP/3 native)
  acceptable:
    - php-fpm + nginx (HTTP/1.1 to FastCGI; enforce CL/TE at nginx layer)
    - RoadRunner (Go-backed, HTTP/2 capable)
  avoid:
    - name: Apache mod_php
      reason: mod_php shares the Apache HTTP/1.1 parser; older Apache versions have known desync issues. Use php-fpm instead.
minimum_versions:
  php: "8.3"
---

# HTTP Request Smuggling — PHP

## Library choice
PHP itself does not own the HTTP parsing layer — the web server (nginx, Caddy, Apache) or application server (FrankenPHP, RoadRunner) does. The primary mitigation is at the infrastructure layer. FrankenPHP (PHP embedded in Caddy) speaks HTTP/2 and HTTP/3 end-to-end, eliminating HTTP/1.1 desync. For php-fpm deployments, enforce CL/TE validation in the nginx configuration. Add a PHP middleware layer (PSR-15) as application-layer defense-in-depth.

## Reference implementation
```php
<?php
// src/Middleware/AntiSmugglingMiddleware.php (PSR-15)
declare(strict_types=1);

use Psr\Http\Message\ResponseFactoryInterface;
use Psr\Http\Message\ResponseInterface;
use Psr\Http\Message\ServerRequestInterface;
use Psr\Http\Server\MiddlewareInterface;
use Psr\Http\Server\RequestHandlerInterface;

final class AntiSmugglingMiddleware implements MiddlewareInterface
{
    private const ALLOWED_TE = ['chunked', 'identity'];

    public function __construct(
        private readonly ResponseFactoryInterface $responseFactory,
    ) {}

    public function process(
        ServerRequestInterface $request,
        RequestHandlerInterface $handler,
    ): ResponseInterface {
        $hasCl = $request->hasHeader('Content-Length');
        $hasTe = $request->hasHeader('Transfer-Encoding');

        if ($hasCl && $hasTe) {
            return $this->responseFactory->createResponse(400)
                ->withHeader('Content-Type', 'text/plain');
        }

        if ($hasTe) {
            $te = strtolower(trim($request->getHeaderLine('Transfer-Encoding')));
            if (!in_array($te, self::ALLOWED_TE, strict: true)) {
                return $this->responseFactory->createResponse(400)
                    ->withHeader('Content-Type', 'text/plain');
            }
        }

        return $handler->handle($request);
    }
}
```

```nginx
# nginx.conf — enforce at proxy layer (primary defense)
server {
    listen 443 ssl http2;

    # Reject requests with both Content-Length and Transfer-Encoding.
    # nginx 1.21+ rejects CL+TE by default in strict mode.
    # Ensure proxy_http_version 1.1 and disable pipelining to php-fpm.
    location ~ \.php$ {
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        fastcgi_pass unix:/run/php/php8.3-fpm.sock;
        fastcgi_param SCRIPT_FILENAME $realpath_root$fastcgi_script_name;
        include fastcgi_params;
    }
}
```

## Language-specific gotchas
- PHP superglobals (`$_SERVER`) do not expose the raw `Transfer-Encoding` header when running under php-fpm — FastCGI strips it before PHP sees the request. The nginx/Caddy layer must be the validation point; the PSR-15 middleware applies to frameworks that reconstruct the ServerRequest from FastCGI data.
- Laravel: add `AntiSmugglingMiddleware` via `app/Http/Kernel.php` in the `$middleware` array (global middleware), not route-specific middleware.
- Slim / Mezzio (PSR-15 native): add the middleware as the first layer in the pipeline via `$app->add(new AntiSmugglingMiddleware($responseFactory))`.
- FrankenPHP uses Caddy's HTTP/2 framing end-to-end — `Content-Length` vs `Transfer-Encoding` desync is not possible over HTTP/2. The PSR-15 middleware still runs but primarily guards HTTP/1.1 connections from non-browser clients.
- `$request->getHeaderLine('Transfer-Encoding')` returns a comma-joined string for multi-value headers. Validate each value: `array_map('trim', explode(',', $te))`.
- php-fpm pool configuration: set `request_terminate_timeout = 30` to prevent long-lived connections from being shared across requests.

## Tests to write
- PHPUnit with a PSR-7 test request: POST with both `Content-Length` and `Transfer-Encoding` — expect 400 response from middleware.
- POST with `Transfer-Encoding: xchunked` — expect 400.
- Normal POST with `Content-Length` — expect handler called.
- Normal chunked POST — expect handler called.
- nginx integration: use a Testcontainer or docker-compose test to verify nginx rejects CL+TE at the proxy layer before php-fpm is reached.
