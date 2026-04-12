---
schema_version: 1
archetype: io/email-injection
title: Email Header Injection Defense
summary: Preventing attackers from injecting SMTP headers through form fields to send unauthorized email or hijack mail flows.
applies_to: [csharp, python, go, javascript, java, ruby, php]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - email
  - smtp
  - header
  - injection
  - crlf
  - mail
  - bcc
  - cc
  - from
  - reply-to
related_archetypes:
  - io/input-validation
  - io/command-injection
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: Input Validation Cheat Sheet
  cwe: "93"
---

# Email Header Injection Defense — Principles

## When this applies
Any time your code constructs an outbound email message where any header value — To, From, CC, BCC, Reply-To, Subject, or any custom header — includes data supplied by an untrusted caller. Common examples: contact forms where the user's name or email becomes the From or Reply-To header; newsletter subscription flows where the user's address is embedded; feedback forms where the subject line includes user-supplied text; and any SMTP relay code that forwards user-supplied content. The attack surface includes any newline character (`\r`, `\n`, `\r\n`) in a header value, which terminates the current header and begins a new one under SMTP's line-based protocol.

## Architectural placement
Email construction lives in a dedicated `MailComposer` service that accepts typed, validated parameters — never raw strings to be spliced into a message. This service owns the complete SMTP header set and uses a well-maintained mail library that constructs the MIME message internally rather than building raw RFC 5322 lines. User-supplied values are validated against field-specific rules (email address format, subject character set, body size limit) before the composer sees them. The composer never concatenates user strings directly into header lines; it uses library-provided setter methods that handle encoding. Outbound SMTP credentials are owned by the service layer, never by the caller.

## Principles
1. **Use a mail library that constructs headers internally.** Libraries like `System.Net.Mail.MailMessage`, Python's `email.message.EmailMessage`, Go's `net/smtp` with `mime/quotedprintable`, `nodemailer`, JavaMail / Jakarta Mail, and `mail` gems for Ruby all compose headers using RFC 5322-aware APIs. They encode or reject newline characters in header values. Never build raw SMTP protocol strings with string concatenation.
2. **Strip or reject CR and LF from all header values before they reach the library.** Even well-maintained libraries may be misconfigured or may pass header values to a transport that does not sanitize them. Reject any user-supplied string containing `\r` or `\n` as malformed input. Do not strip them silently — log and return a validation error so the anomaly is visible.
3. **Validate email addresses as addresses, not as strings.** A "From" address submitted via a form must be validated against the RFC 5321 address grammar before use. A valid email address cannot contain CR, LF, or unencoded angle brackets outside of the local part. Use the library's address parser, not a generic string check.
4. **Restrict the Subject field.** The Subject header value should be restricted to printable ASCII or properly encoded (RFC 2047 encoded-words). User-supplied text that contains CR or LF must be rejected; the library must encode non-ASCII characters using quoted-printable or base64 before writing the header.
5. **Never allow user input to set arbitrary headers.** The set of allowed headers must be a closed list: To, From, Reply-To, Subject, body. CC and BCC should be set programmatically from the application's allowlist, not from user input. A user who can set an arbitrary header can add BCC addresses to redirect copies of every submission to themselves.
6. **Use a transactional email API instead of direct SMTP where possible.** Services like SendGrid, Amazon SES, Mailgun, and Postmark accept structured JSON with explicit fields for recipients, subject, and body. There is no SMTP header line to inject into because the API serializes the message. The injection surface is eliminated by construction.
7. **Log every outbound email's To, From, Subject, and send time.** Header injection attacks often use the BCC or To header to redirect mail to attacker-controlled addresses. A log of every sent message makes injections detectable after the fact.

## Anti-patterns
- `mail("$user_email", "Contact: $user_name", $body)` in PHP — if `$user_email` contains `\nBCC: attacker@evil.com`, the injected BCC header is delivered.
- Building a raw SMTP DATA string with `"Subject: " + subject + "\r\nFrom: " + from + "\r\n"` — any CR/LF in `subject` or `from` breaks out of the current header.
- Using the user-submitted "reply-to" field verbatim without validation — a value like `"legitimate@example.com\r\nBCC: 1000@spam.com"` is a header injection.
- Assuming the mail library sanitizes headers — some older library versions passed header values to the MTA without encoding, relying on the MTA to handle them; MTAs often do not.
- Allowing the user to control which headers are set at all, e.g., parsing a user-supplied map of extra headers.
- Sending mail from user-supplied From addresses without SPF/DKIM validation — the injection does not need to modify a header to be harmful if the attacker can spoof an authoritative From address.

## References
- OWASP ASVS V5.3 -- Output Encoding and Injection Prevention
- OWASP Input Validation Cheat Sheet
- CWE-93 -- Improper Neutralization of CRLF Sequences ("CRLF Injection")
- RFC 5321 -- Simple Mail Transfer Protocol
- RFC 5322 -- Internet Message Format
