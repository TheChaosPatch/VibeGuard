---
schema_version: 1
archetype: logging/log-injection
language: php
principles_file: _principles.md
libraries:
  preferred: monolog/monolog
  acceptable:
    - psr/log
  avoid:
    - name: error_log with string concatenation
      reason: error_log() writes the string verbatim to the error log file; control characters in user input are not escaped.
minimum_versions:
  php: "8.4"
---

# Log Injection Defense — PHP

## Library choice
`monolog/monolog` is the de-facto PHP logger. Configure it with `Monolog\Formatter\JsonFormatter` so all field values are JSON-serialised (newlines escaped). With the default `LineFormatter`, values are embedded verbatim into the line — sanitisation is then the application's responsibility. Monolog implements PSR-3 (`psr/log`), so the interface is swappable.

## Reference implementation
```php
<?php
declare(strict_types=1);

use Monolog\Logger;
use Monolog\Handler\StreamHandler;
use Monolog\Formatter\JsonFormatter;

function createLogger(string $name): Logger
{
    $handler = new StreamHandler('php://stderr', Logger::DEBUG);
    $handler->setFormatter(new JsonFormatter());
    $logger = new Logger($name);
    $logger->pushHandler($handler);
    return $logger;
}

const CONTROL_CHARS = '/[\r\n\x00-\x1f\x7f]/u';
const MAX_LOG_VALUE = 500;

function sanitize(?string $value): string
{
    if ($value === null) return '<null>';
    $truncated = mb_strlen($value) > MAX_LOG_VALUE
        ? mb_substr($value, 0, MAX_LOG_VALUE) . '…'
        : $value;
    return preg_replace(CONTROL_CHARS, ' ', $truncated) ?? $truncated;
}

final class AuthService
{
    public function __construct(private readonly \Psr\Log\LoggerInterface $logger) {}

    public function login(string $username, string $password): bool
    {
        // Context array — JsonFormatter serialises each value as a JSON string.
        $this->logger->info('Login attempt', ['username' => $username]);

        $success = $this->validate($username, $password);

        if (!$success) {
            $this->logger->warning('Login failed', ['username' => sanitize($username)]);
        }
        return $success;
    }

    private function validate(string $u, string $p): bool { return false; }
}
```

## Language-specific gotchas
- `$logger->info("Login attempt for $username")` — PHP string interpolation evaluates before the PSR-3 call. Use the context array (`['username' => $username]`) instead of interpolating into the message string.
- Monolog's `LineFormatter` does not escape control characters in context values. It renders them as their string representation, which may include literal `\n`. Use `JsonFormatter` in production.
- `error_log($message)` writes directly to the configured PHP error log with no escaping. Never use it for messages containing user input.
- PSR-3 specifies that the message string may contain `{placeholder}` tokens that are replaced from the context array. `$logger->info("Login for {username}", ['username' => $username])` — the replacement happens inside Monolog; control characters in `$username` still appear in the formatted line unless a JSON formatter is used.
- `mb_strlen` and `mb_substr` are used instead of `strlen`/`substr` to correctly handle multibyte UTF-8 characters in the length limit.

## Tests to write
- `sanitize("user\nroot")` returns `"user root"`.
- `sanitize(str_repeat("a", 600))` — `mb_strlen` of result equals 501.
- Monolog integration with `JsonFormatter`: log `['username' => "a\nb"]`; capture stderr; decode JSON; assert no literal newline in the `username` field.
- `error_log` negative test: document that `error_log("Login: $username")` with `$username = "a\nb"` produces two log lines.
