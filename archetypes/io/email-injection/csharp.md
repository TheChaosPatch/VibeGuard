---
schema_version: 1
archetype: io/email-injection
language: csharp
principles_file: _principles.md
libraries:
  preferred: MailKit + MimeKit
  acceptable:
    - System.Net.Mail.SmtpClient (legacy; rejects CR/LF in headers in .NET Core)
  avoid:
    - name: System.Net.Mail.SmtpClient on .NET Framework
      reason: Older .NET Framework versions of SmtpClient did not sanitize header values; vulnerable to injection on some builds.
    - name: Raw SMTP string construction with SmtpClient.SendRaw
      reason: Bypasses the structured API and all its sanitization; any string concatenation of user data into a raw SMTP DATA block is injection.
minimum_versions:
  dotnet: "10.0"
---

# Email Header Injection Defense — C#

## Library choice
`MailKit` with `MimeKit` is the recommended SMTP stack for .NET. `MimeKit` constructs RFC 5322 messages through a structured API: `MimeMessage` properties accept typed `MailboxAddress` objects, not raw strings, for address fields. `MimeKit` encodes non-ASCII header values using RFC 2047 encoded-words and throws `ArgumentException` if a value contains CR or LF. `System.Net.Mail.MailMessage` is acceptable on .NET Core / .NET 5+ (where it validates headers) but has a murkier history on .NET Framework. Never build raw SMTP DATA strings.

## Reference implementation
```csharp
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Text;

public sealed class MailComposer
{
    private const int MaxSubjectLength = 200;
    private const int MaxBodyLength = 100_000;
    private static readonly MailboxAddress SystemFrom = new("Notifications", "no-reply@example.com");

    public sealed record ContactRequest(string ReplyToEmail, string ReplyToName, string Subject, string Body);

    public static MimeMessage Compose(ContactRequest req)
    {
        ValidateHeaderValue(req.Subject, nameof(req.Subject), MaxSubjectLength);
        ValidateHeaderValue(req.ReplyToName, nameof(req.ReplyToName), 100);
        if (req.Body.Length > MaxBodyLength)
            throw new ArgumentException("Body too long.", nameof(req.Body));
        var replyTo = new MailboxAddress(req.ReplyToName, req.ReplyToEmail);
        var message = new MimeMessage();
        message.From.Add(SystemFrom);
        message.To.Add(new MailboxAddress("Support Team", "support@example.com"));
        message.ReplyTo.Add(replyTo);
        message.Subject = req.Subject;
        message.Body = new TextPart(TextFormat.Plain) { Text = req.Body };
        return message;
    }

    private static void ValidateHeaderValue(string value, string paramName, int maxLen)
    {
        if (value.Length > maxLen)
            throw new ArgumentException($"{paramName} exceeds {maxLen} characters.", paramName);
        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException($"{paramName} contains illegal line break.", paramName);
    }
}
```

## Language-specific gotchas
- `MimeKit.MailboxAddress` calls `InternetAddress.Parse` internally and throws `ParseException` for malformed addresses. Catch it and map it to a 400 validation error, not a 500.
- `MimeMessage.Subject` setter in MimeKit accepts a `string` and encodes non-ASCII using RFC 2047 `=?utf-8?...?=` encoded-words. Do not pre-encode the subject yourself — double-encoding produces garbled output in mail clients.
- `System.Net.Mail.SmtpClient` is marked as not recommended for new development by Microsoft (see the "Remarks" in the docs). `MailKit` is the officially suggested replacement.
- Never set `MailMessage.Headers.Add("BCC", userInput)` — this bypasses the typed `Bcc` collection and writes directly to the raw header block, re-introducing injection.
- If you use a transactional email API (SendGrid, Amazon SES), use the SDK's typed request model (`SendGridMessage`, `SendRawEmailRequest`) rather than building raw MIME strings to pass to the API.
- Log the composed `message.MessageId`, `message.To`, `message.Subject`, and send timestamp for every outbound message. Do not log the body (PII / confidentiality).

## Tests to write
- Happy path: a valid `ContactRequest` produces a `MimeMessage` with the correct From, To, ReplyTo, and Subject.
- CR in subject: a subject containing `\r` throws `ArgumentException` before MimeKit sees it.
- LF in reply-to name: a name containing `\n` throws `ArgumentException`.
- Invalid email address: a reply-to email of `"not-an-email"` causes `ParseException` from MimeKit.
- Subject too long: a 201-character subject throws `ArgumentException`.
- Non-ASCII subject: a subject with emoji is encoded as a valid RFC 2047 encoded-word (assert the header value starts with `=?`).
- From is always the system address: assert `message.From[0]` equals `SystemFrom` regardless of input.
