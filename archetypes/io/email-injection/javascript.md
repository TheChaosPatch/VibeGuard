---
schema_version: 1
archetype: io/email-injection
language: javascript
principles_file: _principles.md
libraries:
  preferred: nodemailer
  acceptable:
    - "@sendgrid/mail (transactional API — eliminates SMTP header surface)"
    - "@aws-sdk/client-ses (transactional API)"
  avoid:
    - name: net.Socket with raw SMTP protocol strings
      reason: Hand-crafted SMTP DATA is injection-by-design; any user input concatenated into the data stream is a header injection vector.
    - name: nodemailer with unvalidated user strings in headers object
      reason: nodemailer's extra headers option accepts a key-value object; passing user-controlled keys or values into it bypasses all internal sanitization.
minimum_versions:
  node: "22"
---

# Email Header Injection Defense — JavaScript

## Library choice
`nodemailer` is the standard Node.js SMTP library. It constructs RFC 5322 messages internally and encodes header values using RFC 2047 when they contain non-ASCII. However, nodemailer does not sanitize CR/LF in user-supplied values — it assumes the caller has validated them. Add a pre-validation step that rejects CR/LF in all user-supplied header strings. For production, `@sendgrid/mail` or `@aws-sdk/client-ses` accept structured objects (no raw SMTP), eliminating the header injection surface entirely.

## Reference implementation
```javascript
"use strict";
const nodemailer = require("nodemailer");

const MAX_SUBJECT_LEN = 200, MAX_BODY_LEN = 100_000, MAX_NAME_LEN = 100;
const SYSTEM_FROM = "no-reply@example.com", SUPPORT_TO = "support@example.com";

function rejectCrlf(value, field) {
  if (/[\r\n]/.test(value)) throw new Error(`${field} contains illegal line break`);
}

function composeContactEmail({ replyToEmail, replyToName, subject, body }) {
  rejectCrlf(replyToEmail, "replyToEmail");
  rejectCrlf(replyToName, "replyToName");
  rejectCrlf(subject, "subject");
  if (replyToName.length > MAX_NAME_LEN) throw new RangeError(`replyToName exceeds ${MAX_NAME_LEN}`);
  if (subject.length > MAX_SUBJECT_LEN) throw new RangeError(`subject exceeds ${MAX_SUBJECT_LEN}`);
  if (body.length > MAX_BODY_LEN) throw new RangeError(`body exceeds ${MAX_BODY_LEN}`);

  return {
    from: SYSTEM_FROM, to: SUPPORT_TO,
    replyTo: `${replyToName} <${replyToEmail}>`,
    subject, text: body,
  };
}

async function sendContactEmail(transporter, req) {
  const mailOptions = composeContactEmail(req);
  const info = await transporter.sendMail(mailOptions);
  return info.messageId;
}

module.exports = { composeContactEmail, sendContactEmail };
```

## Language-specific gotchas
- nodemailer's `headers` option in `SendMailOptions` accepts an object of additional headers. Never pass user-supplied property names or values into this object — the keys become header names and the values header values with no sanitization.
- nodemailer does encode non-ASCII display names in `replyTo` using RFC 2047, but it does not reject CR/LF in the address string. The `rejectCrlf` call in the example is load-bearing.
- The `replyTo` field is formatted as `"Name <addr>"`. If `replyToName` contains `>`, the angle bracket terminates the display name and the rest is interpreted as the start of the address. Validate that display names contain only printable ASCII excluding angle brackets, or use nodemailer's structured address format: `{ name: replyToName, address: replyToEmail }`.
- For SendGrid (`@sendgrid/mail`), use `msg.setReplyTo({ email, name })` with typed fields. The SendGrid API accepts JSON and has no raw SMTP DATA to inject into.
- Nodemailer supports DKIM signing via the `dkim` option in the transporter config. Enable it — a signed From address is harder to spoof even if an attacker alters the Reply-To to redirect replies.
- Log `info.messageId`, `mailOptions.to`, and `mailOptions.subject` for every send. Use structured logging (JSON) so the log is machine-queryable for anomaly detection.

## Tests to write
- Happy path: valid input produces a mail options object with correct `from`, `to`, `replyTo`, and `subject`.
- CR in subject: `"Hello\rWorld"` throws with a message containing "line break".
- LF in name: `"Name\nInject"` throws.
- Subject too long: a 201-character subject throws `RangeError`.
- Body too long: a body over `MAX_BODY_LEN` characters throws `RangeError`.
- From is always `SYSTEM_FROM`: assert `options.from === SYSTEM_FROM`.
- Structured replyTo: passing `replyToName = "A > B"` -- verify the composed replyTo string does not produce a bare unmatched angle bracket (or add the stricter name validation).
