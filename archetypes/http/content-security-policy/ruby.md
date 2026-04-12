---
schema_version: 1
archetype: http/content-security-policy
language: ruby
principles_file: _principles.md
libraries:
  preferred: secure_headers gem (Rails / Rack)
  acceptable:
    - rack-protection (basic header support only)
  avoid:
    - name: Manual string concatenation for header values
      reason: Error-prone; no escaping of nonce or directive values.
minimum_versions:
  ruby: "3.3"
  rails: "7.2"
---

# Content Security Policy — Ruby

## Library choice
The `secure_headers` gem (by GitHub Security) provides a full CSP DSL, per-request nonce support, and automatic nonce injection into Rails view helpers (`content_tag`, `javascript_tag`). It integrates with Rails via Railtie. Rack-only applications can use it through `SecureHeaders::Middleware`. The nonce is accessible in views and controllers via `content_security_policy_nonce`.

## Reference implementation
```ruby
# config/initializers/secure_headers.rb
SecureHeaders::Configuration.default do |config|
  config.csp = {
    default_src: %w['none'],
    script_src:  %w['strict-dynamic'],  # nonce added automatically
    style_src:   %w['self'],
    img_src:     %w['self' data:],
    connect_src: %w['self'],
    font_src:    %w['self'],
    form_action: %w['self'],
    frame_ancestors: %w['none'],
    base_uri:    %w['self'],
    upgrade_insecure_requests: true,
    report_uri:  ['/csp-report'],
  }
  config.csp_report_only = {} # empty = no report-only header in production
end
```

```ruby
# app/controllers/application_controller.rb
class ApplicationController < ActionController::Base
  # secure_headers auto-applies via before_action; nonce available as:
  #   content_security_policy_nonce   # in controllers
  #   @content_security_policy_nonce  # in views (set by helper)
end
```

```erb
<%# app/views/layouts/application.html.erb %>
<script nonce="<%= content_security_policy_nonce %>">
  // inline bootstrap
</script>
```

## Language-specific gotchas
- `secure_headers` generates the nonce via `SecureRandom.base64(16)` — 128 bits, unique per request. Do not override the nonce generator with a static value.
- Rails 6+ has a built-in `content_security_policy` DSL in `config/initializers/content_security_policy.rb`. The two systems conflict — use one or the other. `secure_headers` is more expressive; the built-in DSL is simpler for new projects. If using the built-in DSL, enable nonces with `config.content_security_policy_nonce_generator = ->(req) { SecureRandom.base64(16) }`.
- `javascript_tag nonce: true` and `stylesheet_link_tag` with `nonce: true` inject the nonce automatically when using `secure_headers` or the Rails built-in DSL nonce generator.
- Report-only mode: set `config.csp_report_only` with the full policy hash in the initializer during rollout, then flip to `config.csp`.
- `rack-protection` sets a static CSP string without nonce support — it is insufficient for modern strict-dynamic policies.
- Turbo (Hotwire) injects `<script>` tags into the DOM after the initial load. These scripts must also carry the nonce: set `data-turbo-track="reload"` or configure Turbo to pass the nonce from the meta tag.

## Tests to write
- `get :show` in a controller spec: `response.headers["Content-Security-Policy"]` contains `nonce-` value.
- Two requests produce different nonce values.
- View renders `<script nonce="X">` where `X` matches the header nonce.
- `frame-ancestors 'none'` present in the policy.
- Report-only initializer: header is `Content-Security-Policy-Report-Only`, not the enforcing variant.
