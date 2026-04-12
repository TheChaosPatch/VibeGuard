---
schema_version: 1
archetype: io/email-injection
language: java
principles_file: _principles.md
libraries:
  preferred: Jakarta Mail (jakarta.mail) via Spring Boot Mail or standalone
  acceptable:
    - SimpleJavaMail (wraps Jakarta Mail with a fluent builder)
    - AWS SDK SES v2 / SendGrid Java SDK (transactional API)
  avoid:
    - name: javax.mail with unvalidated header values
      reason: Older javax.mail (now superseded by jakarta.mail) passed header values through without CR/LF sanitization in some versions.
    - name: Raw SMTP via java.net.Socket
      reason: Hand-building SMTP DATA with string concatenation is direct injection; the library must construct the RFC 5322 message.
minimum_versions:
  java: "21"
---

# Email Header Injection Defense — Java

## Library choice
`jakarta.mail` (Jakarta EE 9+, successor to `javax.mail`) provides a `MimeMessage` API that constructs RFC 5322 messages through typed setter methods. `InternetAddress` validates email address grammar. Spring Boot's `JavaMailSender` wraps `jakarta.mail` with dependency injection and externalized SMTP configuration. Validate all user-supplied header values for CR/LF before calling any setter, because `MimeMessage` does not reject newlines in all versions. For cloud deployments, AWS SES v2 or SendGrid eliminate the SMTP header surface.

## Reference implementation
```java
import jakarta.mail.*;
import jakarta.mail.internet.*;
import java.util.regex.Pattern;

public final class MailComposer {
    private static final int    MAX_SUBJECT_LEN = 200;
    private static final int    MAX_BODY_LEN    = 100_000;
    private static final int    MAX_NAME_LEN    = 100;
    private static final String SYSTEM_FROM     = "no-reply@example.com";
    private static final String SUPPORT_TO      = "support@example.com";
    private static final Pattern CRLF           = Pattern.compile("[\r\n]");

    private MailComposer() {}

    public record ContactRequest(
            String replyToEmail,
            String replyToName,
            String subject,
            String body) {}

    public static MimeMessage compose(Session session, ContactRequest req)
            throws MessagingException {
        validateHeaderValue(req.replyToEmail(), "replyToEmail", 254);
        validateHeaderValue(req.replyToName(),  "replyToName",  MAX_NAME_LEN);
        validateHeaderValue(req.subject(),      "subject",      MAX_SUBJECT_LEN);
        if (req.body().length() > MAX_BODY_LEN)
            throw new IllegalArgumentException("body exceeds " + MAX_BODY_LEN + " chars");

        MimeMessage msg = new MimeMessage(session);
        msg.setFrom(new InternetAddress(SYSTEM_FROM));
        msg.setRecipient(Message.RecipientType.TO, new InternetAddress(SUPPORT_TO));
        // InternetAddress(email, personal) encodes the display name with RFC 2047.
        msg.setReplyTo(new Address[]{ new InternetAddress(req.replyToEmail(), req.replyToName(), "UTF-8") });
        msg.setSubject(req.subject(), "UTF-8");
        msg.setText(req.body(), "UTF-8");
        return msg;
    }

    private static void validateHeaderValue(String value, String field, int maxLen) {
        if (value.length() > maxLen)
            throw new IllegalArgumentException(field + " exceeds " + maxLen + " characters");
        if (CRLF.matcher(value).find())
            throw new IllegalArgumentException(field + " contains illegal line break characters");
    }
}
```

## Language-specific gotchas
- `InternetAddress(email, personal, charset)` is the correct three-argument constructor for display names that may contain non-ASCII. It applies RFC 2047 encoded-word encoding to the `personal` (display name) parameter. The single-argument `InternetAddress(addr)` does not encode the display name.
- `InternetAddress.validate()` checks only basic RFC 5321 syntax. Call it if you want an explicit check: `new InternetAddress(email).validate()`. `InternetAddress` does not prevent a malformed display name from breaking the address structure.
- `msg.setSubject(subject, "UTF-8")` writes the subject as an RFC 2047 encoded-word if the string contains non-ASCII, which prevents the subject from being interpreted as a header injection even if it contains unexpected characters.
- Spring's `JavaMailSenderImpl` reads SMTP host, port, and credentials from `application.properties`. Never hardcode SMTP credentials in `MailComposer`; inject the `Session` from the Spring context.
- `MimeMessage.setHeader(name, value)` sets a raw header with no encoding. Never use this method with user-supplied name or value arguments.
- Jakarta Mail's `Transport.send(message)` is synchronous and blocks until the message is accepted or times out. In web request handlers, dispatch to a background executor or use async messaging (a queue) to avoid tying up request threads on SMTP latency.

## Tests to write
- Happy path: a valid `ContactRequest` produces a `MimeMessage` with correct From, To, Reply-To, and Subject headers.
- CR in subject: throws `IllegalArgumentException` before composing.
- LF in name: throws `IllegalArgumentException`.
- Invalid email address: `new InternetAddress("not-an-email").validate()` throws `AddressException`.
- Subject too long: throws `IllegalArgumentException`.
- UTF-8 subject: a subject with non-ASCII characters is encoded as an RFC 2047 encoded-word (assert header value starts with `=?UTF-8?`).
- From is always `SYSTEM_FROM`: assert `msg.getFrom()[0].toString()` equals `SYSTEM_FROM`.
