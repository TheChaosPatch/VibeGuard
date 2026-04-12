---
schema_version: 1
archetype: io/email-injection
language: python
principles_file: _principles.md
libraries:
  preferred: Python stdlib email.message.EmailMessage + smtplib
  acceptable:
    - aiosmtplib (async SMTP with EmailMessage)
    - SendGrid Python SDK / boto3 SES (transactional API — eliminates SMTP header surface)
  avoid:
    - name: smtplib with manually constructed message strings
      reason: String concatenation of headers is injection by definition; SMTP DATA must be constructed by the email library, not assembled by hand.
    - name: email.mime.text.MIMEText with direct header dict assignment from user input
      reason: MIMEText's internal header dict does not sanitize CR/LF in values; injecting user input via __setitem__ bypasses the policy.
minimum_versions:
  python: "3.11"
---

# Email Header Injection Defense — Python

## Library choice
Python's `email.message.EmailMessage` (stdlib, Python 3.6+) with the default `EmailPolicy` correctly encodes header values and rejects or sanitizes CR/LF via the policy object. Use `email.policy.EmailPolicy(utf8=True)` (or `email.policy.SMTP`) to enable strict RFC 6532 / RFC 5321 compliance. For async applications, `aiosmtplib` accepts an `EmailMessage` directly. For production workloads, a transactional email API (SendGrid, SES) eliminates the SMTP header surface entirely by accepting structured JSON.

## Reference implementation
```python
import re, smtplib
from email.message import EmailMessage
from email.policy import SMTP

_CRLF_RE = re.compile(r"[\r\n]")
SYSTEM_FROM = "no-reply@example.com"
SUPPORT_TO = "support@example.com"

def _reject_crlf(value: str, field: str) -> None:
    if _CRLF_RE.search(value):
        raise ValueError(f"{field} contains illegal line break characters")

def compose_contact_email(
    *, reply_to_email: str, reply_to_name: str, subject: str, body: str,
) -> EmailMessage:
    _reject_crlf(reply_to_email, "reply_to_email")
    _reject_crlf(reply_to_name, "reply_to_name")
    _reject_crlf(subject, "subject")
    if len(reply_to_name) > 100: raise ValueError("reply_to_name exceeds 100 chars")
    if len(subject) > 200: raise ValueError("subject exceeds 200 chars")
    if len(body) > 100_000: raise ValueError("body exceeds 100000 chars")

    msg = EmailMessage(policy=SMTP)
    msg["From"] = SYSTEM_FROM
    msg["To"] = SUPPORT_TO
    msg["Reply-To"] = f"{reply_to_name} <{reply_to_email}>"
    msg["Subject"] = subject
    msg.set_content(body)
    return msg

def send_contact_email(msg: EmailMessage, smtp_host: str, smtp_port: int) -> None:
    with smtplib.SMTP(smtp_host, smtp_port) as smtp:
        smtp.starttls()
        smtp.send_message(msg)
```

## Language-specific gotchas
- `email.policy.SMTP` enforces RFC 5321 line length limits and raises `email.errors.HeaderParseError` for malformed header values. Always use an explicit policy — `EmailMessage()` without a policy argument uses `email.policy.compat32`, which is the legacy, lenient policy that may not validate header values.
- The legacy `email.mime.*` classes (`MIMEText`, `MIMEMultipart`) use `compat32` by default. Prefer `EmailMessage` with an explicit policy for new code.
- `smtplib.SMTP.sendmail(from, to, msg_string)` accepts a raw message string — if you build that string with user input, injection is immediate. Use `smtp.send_message(msg)` with a fully constructed `EmailMessage` instead.
- The `Reply-To` header is set as a formatted address string (`"Name <addr>"`). If `reply_to_name` contains angle brackets, it can corrupt the address structure. An allowlist or stricter validation of display names (printable ASCII only, no angle brackets) is recommended in addition to the CRLF check.
- `email.utils.parseaddr(address)` extracts the name and address parts from an RFC 5322 address string. Use it to validate user-supplied "From" or "Reply-To" values: if `parseaddr` returns an empty address, the input is malformed.
- For async web frameworks (FastAPI, Starlette), use `aiosmtplib.send(message, hostname=..., port=...)` instead of `smtplib`. It accepts the same `EmailMessage` object and adds no injection risk.

## Tests to write
- Happy path: valid inputs produce an `EmailMessage` with correct From, To, Reply-To, and Subject.
- CR in subject: `"Hello\rWorld"` raises `ValueError` before composing the message.
- LF in name: `"Name\nInjection"` raises `ValueError`.
- Subject too long: a 201-character subject raises `ValueError`.
- Body too long: a body over `_MAX_BODY_LEN` bytes raises `ValueError`.
- Reply-To angle bracket: `"<injected@evil.com>"` as reply_to_name -- assert the final header is safe (no bare angle bracket outside the address part).
- From is always system address: assert `msg["From"] == SYSTEM_FROM` regardless of input.
- Policy check: assert the constructed `EmailMessage` has `policy=SMTP` (not `compat32`).
