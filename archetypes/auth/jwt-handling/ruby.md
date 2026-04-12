---
schema_version: 1
archetype: auth/jwt-handling
language: ruby
principles_file: _principles.md
libraries:
  preferred: ruby-jwt
  acceptable:
    - jose
  avoid:
    - name: Manual Base64.decode64 of payload
      reason: Reads claims without verifying the signature.
minimum_versions:
  ruby: "3.3"
---

# JWT Handling — Ruby

## Library choice
`ruby-jwt` (`jwt` gem) is the standard JWT library in the Ruby ecosystem, used by Devise JWT, Doorkeeper, and most Rails authentication gems. It supports RS256, ES256, HS256, and `none`-rejection, and validates registered claims in a single `decode` call. The `jose` gem is a more complete JOSE implementation but is rarely needed for access token issuance and validation.

## Reference implementation
```ruby
require 'jwt'
require 'openssl'

ALGORITHM = 'RS256'
ISSUER    = 'https://auth.example.com'
AUDIENCE  = 'https://api.example.com'
TTL       = 900 # 15 minutes

PRIVATE_KEY = OpenSSL::PKey::RSA.new(File.read('/run/secrets/jwt_private.pem'))
PUBLIC_KEY  = PRIVATE_KEY.public_key

def issue_token(subject, extra_claims = {})
  payload = {
    sub: subject,
    iss: ISSUER,
    aud: AUDIENCE,
    iat: Time.now.to_i,
    exp: Time.now.to_i + TTL,
    **extra_claims
  }
  JWT.encode(payload, PRIVATE_KEY, ALGORITHM)
end

def validate_token(token)
  options = {
    algorithm:  ALGORITHM,      # explicit allowlist
    iss:        ISSUER,
    verify_iss: true,
    aud:        AUDIENCE,
    verify_aud: true,
    verify_iat: true,
    leeway:     30,
  }
  payload, _header = JWT.decode(token, PUBLIC_KEY, true, options)
  payload
rescue JWT::DecodeError => e
  raise AuthenticationError, "Invalid token: #{e.message}"
end
```

## Language-specific gotchas
- `JWT.decode(token, key, true, options)` — the third argument `true` means "verify the signature." Passing `false` disables verification entirely. Never pass `false` outside of test introspection.
- The `algorithm` option in the options hash is an allowlist. Without it, `ruby-jwt` raises a warning but may fall through to verifying with whatever algorithm the header specifies.
- `verify_aud: true` requires `aud` to match exactly. When your audience is a single string, pass a string; when it is an array, pass an array. Mismatched types silently skip the check in older versions.
- `JWT::ExpiredSignature`, `JWT::ImmatureSignature`, `JWT::InvalidIssuerError`, and `JWT::InvalidAudError` are all subclasses of `JWT::DecodeError`. Rescue the base class in middleware; log the subclass for diagnostics.
- In Rails, do not rescue `JWT::DecodeError` in ApplicationController and re-render a 200. Map it to a 401 via `rescue_from JWT::DecodeError, with: :unauthorized`.

## Tests to write
- `validate_token(issue_token('u1'))` returns a hash with `'sub' => 'u1'`.
- Token with past `exp` → raises `JWT::ExpiredSignature`.
- Token signed with a different RSA key → raises `JWT::VerificationError`.
- Token with `alg: none` in header → raises `JWT::IncorrectAlgorithm`.
- Token with wrong `iss` → raises `JWT::InvalidIssuerError`.
