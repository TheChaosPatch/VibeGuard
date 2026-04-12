---
schema_version: 1
archetype: auth/password-reset
language: ruby
principles_file: _principles.md
libraries:
  preferred: SecureRandom (stdlib)
  acceptable:
    - Devise (built-in reset flow for Rails)
  avoid:
    - name: rand
      reason: PRNG — not cryptographically secure.
    - name: Time.now.to_i as a token component
      reason: Timestamp-derived tokens are predictable.
minimum_versions:
  ruby: "3.3"
---

# Secure Password Reset — Ruby

## Library choice
Ruby's `SecureRandom.hex(32)` (64 hex characters, 32 bytes of entropy) is the correct token generator — it wraps `OpenSSL::Random` on MRI. For Rails applications, Devise's built-in `reset_password_by_token` flow is the recommended solution — it implements all these principles correctly. For non-Devise apps, use `SecureRandom` and Active Record as shown below.

## Reference implementation
```ruby
require 'digest'
require 'securerandom'

class PasswordResetService
  TOKEN_BYTES    = 32
  EXPIRY_MINUTES = 30

  def initialize(user_repo:, token_repo:, mailer:, password_hasher:)
    @users    = user_repo
    @tokens   = token_repo
    @mailer   = mailer
    @hasher   = password_hasher
  end

  def request_reset(email)
    user = @users.find_by_email(email)
    return unless user  # uniform response

    @tokens.invalidate_all(user_id: user.id)

    raw        = SecureRandom.bytes(TOKEN_BYTES)
    token_hex  = raw.unpack1('H*')
    token_hash = Digest::SHA256.hexdigest(raw)

    @tokens.create(
      user_id:    user.id,
      token_hash: token_hash,
      expires_at: Time.now.utc + (EXPIRY_MINUTES * 60),
      consumed:   false
    )
    @mailer.send_reset_link(user.email, token_hex)
  end

  def redeem_reset(token_hex, new_password)
    raw = [token_hex].pack('H*')
    return false if raw.bytesize != TOKEN_BYTES

    hash   = Digest::SHA256.hexdigest(raw)
    record = @tokens.find_valid(hash)
    return false unless record

    @tokens.consume(hash)
    @users.update_password(record.user_id, @hasher.hash(new_password))
    @users.invalidate_sessions(record.user_id)
    true
  rescue ArgumentError
    false
  end
end
```

## Language-specific gotchas
- `SecureRandom.bytes(32)` returns a binary string. Use `.unpack1('H*')` to produce the hex string for the email link; use the raw bytes for SHA-256 hashing. Never use `SecureRandom.hex(64)` as a token and then try to interpret the hex as bytes — the length will be wrong.
- `[hex].pack('H*')` raises `ArgumentError` for odd-length or non-hex input — rescue it and return `false`.
- In Rails, wrap the invalidate + create calls in `ActiveRecord::Base.transaction` to make them atomic.
- Devise's `reset_password_token` is stored as a SHA256 hex digest, and the raw token is delivered by email — matching this archetype's pattern. If you use Devise, do not reimplement the reset flow; rely on `User.send_reset_password_instructions`.
- Do not render the reset form with the token in a hidden `GET` parameter that will be logged by Rack middleware. Use a POST or put the token in the URL path, not a query string that appears in access logs.

## Tests to write
- `request_reset` for an unknown email returns `nil` and sends no mail.
- `redeem_reset` with a valid token hex returns `true`.
- `redeem_reset` called twice with the same token returns `true` then `false`.
- `redeem_reset` with a non-hex string returns `false` without raising.
- `redeem_reset` with an expired record returns `false`.
