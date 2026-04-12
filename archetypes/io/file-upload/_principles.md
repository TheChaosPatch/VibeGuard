---
schema_version: 1
archetype: io/file-upload
title: Secure File Upload Handling
summary: Safely receiving, validating, and storing files uploaded by untrusted callers.
applies_to: [csharp, python, go]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - file-upload
  - upload
  - multipart
  - content-type
  - mime
  - file-size
  - magic-bytes
  - file-validation
  - antivirus
  - storage
  - media
  - image-upload
  - attachment
  - file-extension
related_archetypes:
  - io/path-traversal
  - io/input-validation
references:
  owasp_asvs: V12.1
  owasp_cheatsheet: File Upload Cheat Sheet
  cwe: "434"
---

# Secure File Upload Handling -- Principles

## When this applies
Any endpoint or handler that accepts a file from a caller -- HTTP multipart uploads, base64-encoded payloads in JSON, files received over gRPC streams, message-queue attachments, or CLI tools that process user-supplied files. The file's name, size, content type, and contents are all untrusted. If your code writes a caller-supplied byte stream to disk or forwards it to another service, this archetype applies.

## Architectural placement
File uploads pass through a dedicated upload service that owns the entire lifecycle: receive, validate, store, and serve. The service enforces size limits at the transport layer before the full body is buffered, validates the file's content (not just its declared type), stores it under a server-generated name outside the webroot, and records metadata (original filename, content type, uploader, timestamp) in a database. No other component in the system touches the raw upload or constructs storage paths from user-supplied names. Downloads are served through a separate handler that streams the file with an explicit `Content-Disposition: attachment` and a safe `Content-Type`.

## Principles
1. **Enforce size limits at the transport layer.** Reject oversized uploads before the full body is buffered in memory. Configure your web server or framework's maximum request body size, and enforce a per-file limit in the handler. An unbounded upload is a denial-of-service vector.
2. **Validate content, not just the declared type.** The `Content-Type` header and the file extension are caller-controlled and trivially spoofed. Read the file's magic bytes (file signature) to determine the actual type and compare against an allowlist. A `.jpg` whose first bytes are `%PDF` or `PK` is not a JPEG.
3. **Allowlist permitted file types.** Define the exact set of MIME types and extensions your application accepts. Reject everything else. "Block known-bad extensions" is always incomplete.
4. **Store under a server-generated name outside the webroot.** Use a random identifier (`UUID`, `secrets.token_urlsafe`) as the storage filename. Keep the user's original filename as metadata in the database, never on the filesystem. Store uploads in a directory that the web server cannot serve and the runtime cannot execute.
5. **Sanitize and re-encode when possible.** For image uploads, decode and re-encode the image through a known-good library. This strips embedded scripts, polyglot payloads, EXIF data containing GPS coordinates or injection payloads, and malformed headers designed to exploit parser vulnerabilities.
6. **Scan for malware when the threat model demands it.** For applications that accept arbitrary documents (PDFs, Office files, archives), integrate a malware scanning step before the file is persisted or forwarded. Treat scan failure as a rejection, not a pass.
7. **Separate upload and download paths.** The upload handler writes to storage. The download handler reads from storage, sets `Content-Disposition: attachment`, sets an explicit `Content-Type` from your allowlist (never from the stored metadata verbatim), and streams the bytes. Never serve uploaded files through static-file middleware.

## Anti-patterns
- Trusting `Content-Type` or the file extension to determine what the file is. Both are caller-controlled strings.
- Storing uploads under the user-supplied filename in a directory the web server serves. This is arbitrary file write plus potential remote code execution if the web server executes uploaded scripts.
- Accepting uploads with no size limit, or setting the limit at the handler level after the framework has already buffered the entire body in memory.
- Serving uploaded files inline (`Content-Disposition: inline`) with a content type derived from the extension. A `.html` file is now an XSS vector served from your origin.
- Blocking a list of "dangerous" extensions (`.exe`, `.sh`, `.php`) instead of allowlisting the few types you actually need.
- Saving uploads inside the webroot "temporarily" with the intent to move them later. The window between write and move is exploitable.
- Skipping re-encoding for images because "it's slow." A polyglot JPEG/JavaScript file served from your origin is a stored XSS.

## References
- OWASP ASVS V12.1 -- File Upload Requirements
- OWASP File Upload Cheat Sheet
- CWE-434 -- Unrestricted Upload of File with Dangerous Type
