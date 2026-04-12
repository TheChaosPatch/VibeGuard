---
schema_version: 1
archetype: http/request-smuggling
language: ruby
principles_file: _principles.md
libraries:
  preferred: Puma (Rails / Rack HTTP server)
  acceptable:
    - Falcon (HTTP/2-native, async)
  avoid:
    - name: WEBrick
      reason: Development server only; no keep-alive hardening, not for production.
    - name: Thin
      reason: No longer maintained; known HTTP/1.1 parsing gaps.
minimum_versions:
  ruby: "3.3"
  rails: "7.2"
  puma: "6.4"
---

# HTTP Request Smuggling — Ruby

## Library choice
Puma 6.x implements RFC 9112 and rejects requests with both `Content-Length` and `Transfer-Encoding` headers, returning 400. For HTTP/2 end-to-end (eliminating desync entirely), `Falcon` is the async HTTP/2-native Ruby server. Add a Rack middleware layer for defense-in-depth validation. Rack middleware runs before Rails routing and is the correct insertion point for low-level header validation.

## Reference implementation
```ruby
# lib/middleware/anti_smuggling.rb
module Middleware
  class AntiSmuggling
    BAD_REQUEST = [400, { "content-type" => "text/plain" },
                   ["Ambiguous request length headers.\n"]].freeze
    NON_STANDARD_TE = [400, { "content-type" => "text/plain" },
                       ["Non-standard Transfer-Encoding rejected.\n"]].freeze
    ALLOWED_TE = %w[chunked identity].freeze

    def initialize(app)
      @app = app
    end

    def call(env)
      has_cl = env.key?("HTTP_CONTENT_LENGTH")
      has_te = env.key?("HTTP_TRANSFER_ENCODING")

      return BAD_REQUEST if has_cl && has_te

      if has_te
        te = env["HTTP_TRANSFER_ENCODING"].strip.downcase
        return NON_STANDARD_TE unless ALLOWED_TE.include?(te)
      end

      @app.call(env)
    end
  end
end
```

```ruby
# config/application.rb
module MyApp
  class Application < Rails::Application
    config.middleware.insert_before 0, Middleware::AntiSmuggling
  end
end
```

```ruby
# config/puma.rb — Puma hardening
threads 4, 4
workers ENV.fetch("WEB_CONCURRENCY", 2).to_i
max_fast_inline 10
persistent_timeout 20    # seconds; close idle keep-alive connections sooner
```

## Language-specific gotchas
- Rack converts HTTP headers to `HTTP_*` environment keys with hyphens replaced by underscores. `Content-Length` becomes `HTTP_CONTENT_LENGTH`; `Transfer-Encoding` becomes `HTTP_TRANSFER_ENCODING`. Use these keys, not the raw header names.
- `CONTENT_LENGTH` (without `HTTP_` prefix) is set by some Rack servers for the raw Content-Length value — check both `HTTP_CONTENT_LENGTH` and `CONTENT_LENGTH` for completeness.
- Puma 6.x returns a 400 for CL+TE conflicts before the Rack app is invoked — the middleware is defense-in-depth. Do not rely solely on Puma; the middleware makes the policy explicit and testable.
- `insert_before 0` places the middleware at position 0 in the Rack stack — before Rails::Rack::Logger, ActionDispatch::Session, and all other middleware. This is intentional.
- Falcon (async HTTP/2): if using Falcon, HTTP/2 framing eliminates CL/TE desync. The middleware is still valuable for HTTP/1.1 clients if Falcon is configured in hybrid mode.
- nginx in front of Puma: set `proxy_http_version 1.1` and `proxy_set_header Connection ""` to disable HTTP/1.0 pipelining to Puma, or configure upstream HTTP/2 with Puma 6+'s `http_mode :h2c`.

## Tests to write
- RSpec request spec: POST with both `Content-Length` and `Transfer-Encoding` headers — expect 400.
- POST with `Transfer-Encoding: xchunked` — expect 400.
- Normal POST — expect 200.
- Middleware is first in `Rails.application.middleware.to_a` (position 0).
- `CONTENT_LENGTH` present alongside `HTTP_TRANSFER_ENCODING` — expect 400.
