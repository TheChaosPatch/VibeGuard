---
schema_version: 1
archetype: io/email-injection
language: ruby
principles_file: _principles.md
libraries:
  preferred: Mail gem (mikel/mail)
  acceptable:
    - ActionMailer (Rails — wraps the Mail gem)
    - sendgrid-ruby / aws-sdk-ses (transactional API)
  avoid:
    - name: Net::SMTP with raw message string built from user input
      reason: Net::SMTP's smtp.send_message(msg, from, to) accepts a raw string; any user input in a header line is direct injection.
    - name: Mail gem with unvalidated user strings in header setters
      reason: The Mail gem's header setters do not sanitize CR/LF; validation must happen before calling them.
minimum_versions:
  ruby: "3.3"
---

# Email Header Injection Defense — Ruby

## Library choice
The `mail` gem (mikel/mail) is the standard Ruby SMTP library and is used internally by ActionMailer. It constructs RFC 5322 messages through a structured API and applies RFC 2047 encoding to non-ASCII header values. Like nodemailer and gomail, it does not sanitize CR/LF in user-supplied values — that is the caller's responsibility. ActionMailer wraps the `mail` gem with a mailer class pattern; apply the same validation in the mailer's `before_action` or in a dedicated validator before constructing the `mail` object.

## Reference implementation
```ruby
# frozen_string_literal: true

require "mail"

module MailComposer
  MAX_SUBJECT_LEN = 200
  MAX_BODY_LEN    = 100_000
  MAX_NAME_LEN    = 100
  SYSTEM_FROM     = "no-reply@example.com"
  SUPPORT_TO      = "support@example.com"

  ContactRequest = Data.define(:reply_to_email, :reply_to_name, :subject, :body)

  module_function

  def compose(request)
    validate_header!(request.reply_to_email, :reply_to_email, 254)
    validate_header!(request.reply_to_name,  :reply_to_name,  MAX_NAME_LEN)
    validate_header!(request.subject,        :subject,        MAX_SUBJECT_LEN)
    raise ArgumentError, "body exceeds #{MAX_BODY_LEN} chars" if request.body.length > MAX_BODY_LEN

    Mail.new do
      from    SYSTEM_FROM
      to      SUPPORT_TO
      # reply_to uses the Mail gem's address object -- display name is RFC 2047-encoded.
      reply_to "#{request.reply_to_name} <#{request.reply_to_email}>"
      subject  request.subject
      body     request.body
      content_type "text/plain; charset=UTF-8"
    end
  end

  def validate_header!(value, field, max_len)
    raise ArgumentError, "#{field} exceeds #{max_len} chars" if value.length > max_len
    raise ArgumentError, "#{field} contains illegal line break" if value.match?(/[\r\n]/)
  end
end
```

## Language-specific gotchas
- The `mail` gem's `reply_to` setter accepts a string in RFC 5322 address format. If `reply_to_name` contains `>`, it can close the display name early and inject content into the address field. Restrict display names to printable ASCII characters excluding angle brackets (`<>`), or use the structured form: `reply_to address: email, display_name: name`.
- ActionMailer: validate user-supplied values in a `before_action` callback or in the `params` validator before calling `mail(...)`. The `mail` method itself has no CR/LF guard.
- `Net::SMTP#send_message(msg, from, to)` sends the raw string `msg` as the SMTP DATA command payload. Never build `msg` by interpolating user input into header lines. Use the `mail` gem to compose the message and call `.to_s` to get the RFC 5322 string for `send_message`.
- Rails' `deliver_now` / `deliver_later` use ActiveJob for async delivery. Prefer `deliver_later` to decouple web request latency from SMTP round-trip time.
- The `mail` gem applies RFC 2047 `=?UTF-8?B?...?=` encoding to the subject automatically when it contains non-ASCII. Do not pre-encode; let the gem handle it.
- Log the `mail.message_id`, `mail.to`, and `mail.subject` after composition (before delivery) for audit purposes. Do not log the body.

## Tests to write
- Happy path: a valid `ContactRequest` produces a `Mail::Message` with correct `from`, `to`, `reply_to`, and `subject`.
- CR in subject: raises `ArgumentError` before `Mail.new` is called.
- LF in name: raises `ArgumentError`.
- Subject too long: raises `ArgumentError`.
- Body too long: raises `ArgumentError`.
- From is always `SYSTEM_FROM`: assert `mail.from == [SYSTEM_FROM]`.
- Non-ASCII subject: a subject with emoji is encoded in the header as an RFC 2047 encoded-word (assert the raw header value contains `=?UTF-8?`).
- Nil input: `nil` for any field raises before the length check (add an `is_a?(String)` guard to `validate_header!`).
