---
schema_version: 1
archetype: io/email-injection
language: php
principles_file: _principles.md
libraries:
  preferred: Symfony Mailer (symfony/mailer)
  acceptable:
    - PHPMailer (phpmailer/phpmailer)
    - sendgrid-php / aws-sdk-php SES (transactional API)
  avoid:
    - name: mail() built-in with user-supplied $to or $additional_headers
      reason: PHP's mail() function is the canonical header injection vector; it passes raw strings to sendmail with no sanitization.
    - name: PHPMailer with AddCustomHeader from user input
      reason: Custom headers bypass PHPMailer's internal sanitization; user-controlled keys or values are injection.
minimum_versions:
  php: "8.3"
---

# Email Header Injection Defense — PHP

## Library choice
PHP's built-in `mail()` function is the canonical header injection vector: any CR/LF in the `$to` or `$additional_headers` parameter splits the SMTP DATA header and lets the attacker inject arbitrary headers including BCC. Never use `mail()` with user-supplied values. Use `symfony/mailer` (Symfony Mailer), which constructs `Email` objects through a typed API and handles RFC 2047 encoding. `PHPMailer` is an acceptable alternative if `mail()` mode is disabled (use SMTP mode). Symfony Mailer's `Address` class validates the email format before use.

## Reference implementation
```php
<?php declare(strict_types=1);

use Symfony\Component\Mailer\MailerInterface;
use Symfony\Component\Mime\Address;
use Symfony\Component\Mime\Email;

final class MailComposer
{
    private const SYSTEM_FROM = 'no-reply@example.com';
    private const SUPPORT_TO  = 'support@example.com';

    public function __construct(private readonly MailerInterface $mailer) {}

    public function sendContactEmail(string $replyToEmail, string $replyToName, string $subject, string $body): void
    {
        self::validateHeader($replyToEmail, 'replyToEmail', 254);
        self::validateHeader($replyToName, 'replyToName', 100);
        self::validateHeader($subject, 'subject', 200);
        if (strlen($body) > 100_000) throw new \LengthException('body exceeds limit');

        $email = (new Email())
            ->from(new Address(self::SYSTEM_FROM))
            ->to(new Address(self::SUPPORT_TO))
            ->replyTo(new Address($replyToEmail, $replyToName))
            ->subject($subject)
            ->text($body);

        $this->mailer->send($email);
    }

    private static function validateHeader(string $value, string $field, int $maxLen): void
    {
        if (strlen($value) > $maxLen) throw new \LengthException("{$field} exceeds {$maxLen} chars");
        if (str_contains($value, "\r") || str_contains($value, "\n")) {
            throw new \InvalidArgumentException("{$field} contains illegal line break");
        }
    }
}
```

## Language-specific gotchas
- `mail($to, $subject, $body, $headers)` is the original PHP header injection vector. A `$to` value of `"user@example.com\nBCC: attacker@evil.com"` delivers to the attacker. **Never use `mail()` with user-controlled arguments.** Replace all uses in legacy codebases with Symfony Mailer or PHPMailer.
- Symfony Mailer's `Address($email, $name)` calls `AddressParser` internally, which validates the email format and throws `RfcComplianceException` for malformed addresses. Catch it and map it to a 400 validation error.
- The `Email::subject()` setter in Symfony Mailer applies RFC 2047 encoding automatically for non-ASCII subject values. Do not encode manually.
- PHPMailer in SMTP mode (`$mail->isSMTP()`) is safe. PHPMailer in `mail()` mode (`$mail->isMail()`) routes through `mail()` and may be vulnerable depending on the version and header assembly. Always use SMTP mode.
- `PHPMailer::addCustomHeader($name, $value)` bypasses PHPMailer's internal sanitization. Never call it with user-supplied name or value.
- SPF, DKIM, and DMARC records for the sending domain are not a code-level fix, but they are required for the `From` address to be trusted by receiving MTAs. Log the `email->generateMessageId()`, the To address, and the Subject for every sent message.

## Tests to write
- Happy path: a valid contact request sends an `Email` with correct `from`, `to`, `replyTo`, and `subject`.
- CR in subject: throws `\InvalidArgumentException` before the `Email` is constructed.
- LF in name: throws `\InvalidArgumentException`.
- Subject too long: throws `\LengthException`.
- Body too long: throws `\LengthException`.
- Invalid email address: `new Address("not-an-email")` throws `RfcComplianceException`.
- From is always `SYSTEM_FROM`: assert `$email->getFrom()[0]->getAddress() === self::SYSTEM_FROM`.
- mail() banned: a static analysis test (PHPStan / Psalm) or grep check that fails if `mail(` appears in any mailer class.
