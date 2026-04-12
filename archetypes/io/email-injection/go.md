---
schema_version: 1
archetype: io/email-injection
language: go
principles_file: _principles.md
libraries:
  preferred: gopkg.in/gomail.v2
  acceptable:
    - net/smtp (stdlib) with mime/multipart and manual header validation
    - github.com/sendgrid/sendgrid-go (transactional API — eliminates SMTP surface)
  avoid:
    - name: net/smtp with raw message string built by fmt.Sprintf
      reason: String-interpolating user data into SMTP DATA headers is injection; net/smtp sends exactly what you hand it.
    - name: gopkg.in/gomail.v2 with SetHeader using unvalidated user strings
      reason: gomail's SetHeader does not sanitize CR/LF in values; validation must happen before calling SetHeader.
minimum_versions:
  go: "1.22"
---

# Email Header Injection Defense — Go

## Library choice
Go's `net/smtp` stdlib package sends exactly the bytes you give it — it has no header sanitization. Use `gopkg.in/gomail.v2`, which constructs proper MIME messages with RFC 2047 encoding for non-ASCII values, and pair it with a validation layer that rejects CR/LF in every user-supplied header value before calling `SetHeader`. For production, a transactional email API (SendGrid Go SDK, AWS SES SDK) eliminates the SMTP header surface entirely.

## Reference implementation
```go
package mailer

import (
	"fmt"; "regexp"
	gomail "gopkg.in/gomail.v2"
)

const maxSubjectLen, maxBodyLen, maxNameLen = 200, 100_000, 100
const systemFrom, supportTo = "no-reply@example.com", "support@example.com"

var crlfRE = regexp.MustCompile(`[\r\n]`)

type ContactRequest struct {
	ReplyToEmail, ReplyToName, Subject, Body string
}

func Compose(req ContactRequest) (*gomail.Message, error) {
	if err := validateHeader(req.ReplyToEmail, "reply_to_email", 254); err != nil {
		return nil, err
	}
	if err := validateHeader(req.ReplyToName, "reply_to_name", maxNameLen); err != nil {
		return nil, err
	}
	if err := validateHeader(req.Subject, "subject", maxSubjectLen); err != nil {
		return nil, err
	}
	if len(req.Body) > maxBodyLen {
		return nil, fmt.Errorf("body exceeds %d bytes", maxBodyLen)
	}
	m := gomail.NewMessage()
	m.SetHeader("From", systemFrom)
	m.SetHeader("To", supportTo)
	m.SetHeader("Reply-To", m.FormatAddress(req.ReplyToEmail, req.ReplyToName))
	m.SetHeader("Subject", req.Subject)
	m.SetBody("text/plain", req.Body)
	return m, nil
}

func validateHeader(value, field string, maxLen int) error {
	if len(value) > maxLen { return fmt.Errorf("%s exceeds %d characters", field, maxLen) }
	if crlfRE.MatchString(value) { return fmt.Errorf("%s contains illegal line break", field) }
	return nil
}
```

## Language-specific gotchas
- `net/smtp.SendMail(addr, auth, from, to, []byte(msg))` sends the raw byte slice as the DATA segment. If `msg` is assembled with `fmt.Sprintf` and includes user input in a header line, it is injection. Use a message library that constructs the DATA segment, not `net/smtp` alone.
- `gomail.Message.SetHeader` does not sanitize CR/LF. The `validateHeaderValue` function in the example is load-bearing — remove it and the injection surface reopens.
- `gomail.Message.FormatAddress` applies RFC 2047 encoding to the display name if it contains non-ASCII characters, which prevents the display name from being misinterpreted by mail clients or the MTA.
- For async-heavy services, consider the SendGrid Go SDK (`github.com/sendgrid/sendgrid-go`). Its `mail.NewV3Mail()` API accepts typed `mail.Email` objects with explicit `Name` and `Address` fields, and sends them as a JSON API call — no SMTP DATA, no header injection surface.
- Go does not have a built-in email address validator. Use `net/mail.ParseAddress(addr)` to validate the RFC 5322 grammar before placing the address in a header: if `ParseAddress` returns an error, the address is malformed.
- Log the `Message-Id` (set by gomail automatically), the To address, and the Subject for every sent message. Do not log the body.

## Tests to write
- Happy path: a valid `ContactRequest` returns a `*gomail.Message` with no error and correct headers.
- CR in subject: `"Hello\rWorld"` returns an error containing "illegal line break".
- LF in reply-to name: `"Name\nInject"` returns an error.
- Reply-to email too long: a 255-character email returns an error.
- Body too long: a body over `maxBodyLen` bytes returns an error.
- From is always system address: assert the "From" header equals `systemFrom`.
- `net/mail.ParseAddress` integration: an invalid email address `"not-an-email"` causes an error from `net/mail.ParseAddress` before Compose proceeds (add this check to the implementation).
