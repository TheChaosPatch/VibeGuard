---
schema_version: 1
archetype: logging/log-injection
language: ruby
principles_file: _principles.md
libraries:
  preferred: semantic_logger
  acceptable:
    - Logger (stdlib)
  avoid:
    - name: string interpolation into Rails.logger.info
      reason: String interpolation embeds control characters verbatim into the log message before the logger processes it.
minimum_versions:
  ruby: "3.4"
---

# Log Injection Defense — Ruby

## Library choice
`semantic_logger` (part of the `rails_semantic_logger` gem for Rails) outputs JSON by default, escaping control characters in all field values. The stdlib `Logger` class with a custom JSON formatter achieves the same. For plain-text formatters, control characters must be sanitised before passing to the logger.

## Reference implementation
```ruby
require "semantic_logger"

SemanticLogger.default_level = :info
SemanticLogger.add_appender(io: $stderr, formatter: :json)

CONTROL_CHARS = /[\r\n\x00-\x1f\x7f]/
MAX_LOG_VALUE = 500

def sanitize(value)
  return "<null>" if value.nil?
  truncated = value.length > MAX_LOG_VALUE ? value[0, MAX_LOG_VALUE] + "…" : value
  truncated.gsub(CONTROL_CHARS, " ")
end

class AuthService
  include SemanticLogger::Loggable

  def login(username, password)
    # Named payload field — semantic_logger serialises it as a JSON string.
    logger.info("Login attempt", username: username)

    success = validate(username, password)

    logger.warn("Login failed", username: sanitize(username)) unless success
    success
  end

  private

  def validate(_u, _p) = false
end
```

## Language-specific gotchas
- `Rails.logger.info("Login attempt for #{username}")` — the string interpolation evaluates before `Rails.logger.info` is called. Any `\n` in `username` appears verbatim. Use a payload hash instead.
- `Logger` (stdlib) with the default formatter writes `"I, [timestamp] INFO -- : message\n"`. A `\n` in `message` breaks the line structure. Use a JSON formatter or sanitise before passing.
- `semantic_logger` JSON output escapes control characters. Its plain-text formatter does not. Verify the configured appender format before relying on automatic escaping.
- `$logger.tagged(username) { ... }` in `ActiveSupport::TaggedLogging` embeds the tag into the prefix verbatim — sanitise the tag value.
- Exception messages in Ruby often include user input (e.g., `ArgumentError: invalid value: #{user_input}`). Sanitise `e.message` before logging it separately.

## Tests to write
- `sanitize("user\nroot")` returns `"user root"`.
- `sanitize("a" * 600).length` equals 501.
- semantic_logger integration: capture JSON output; log `{ username: "a\nb" }`; parse JSON; assert no literal newline in the `username` field.
- Plain Logger test: log a value with `\r\n` and assert the output contains two log lines — documenting why JSON is preferred.
