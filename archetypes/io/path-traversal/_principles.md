---
schema_version: 1
archetype: io/path-traversal
title: Path Traversal Defense
summary: Keeping user-supplied paths and filenames from escaping the directory you intended.
applies_to: [csharp, python]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - path
  - traversal
  - directory
  - upload
  - zip
  - slip
  - filename
  - canonical
  - realpath
  - file
related_archetypes:
  - io/input-validation
references:
  owasp_asvs: V12.3
  owasp_cheatsheet: File Upload Cheat Sheet
  cwe: "22"
---

# Path Traversal Defense — Principles

## When this applies
Any time a value influenced by a caller is used to open, read, write, list, delete, serve, or extract a file. File uploads are the loudest case, but the same rule applies to `?filename=` query parameters on a download handler, a `Content-Disposition` header forwarded to `open()`, a tenant id used to compute a log directory, a template name passed into a renderer, and an archive entry fed to `ZipFile.extractall`. If a string that came from outside your process is about to meet a filesystem API, this archetype applies.

## Architectural placement
Every filesystem-facing operation goes through a `SafePath` / `BoundedFileStore` helper that takes two arguments — a *root* (a path your code chose, never the user's) and an untrusted *relative* component — and returns a fully-resolved absolute path *only if* the result still lives under the root. The helper uses the OS's canonicalization primitive (`Path.GetFullPath` in .NET, `Path.resolve()` in Python), not a lexical regex. Handlers call `store.Open(id)`, not `File.OpenRead(userPath)`. The helper is the only code in the repo that touches raw paths; if it's correct, path traversal cannot happen elsewhere. If every place in the codebase constructs its own `Path.Combine`, path traversal happens somewhere.

## Principles
1. **Resolve, then verify the result is inside the root.** Lexical checks like "does it contain `..`?" are defeated by URL encoding, Unicode overlong sequences, and backslash-vs-slash on Windows. Let the OS canonicalize the path, *then* compare against the canonical root.
2. **Symlinks count.** A symlink inside an allowed directory that points outside it is a successful escape. Resolve symlinks before the containment check (`realpath` / `Path.GetFullPath` both do this when the file exists).
3. **Allowlist the filename charset.** For the user-visible portion, accept `[A-Za-z0-9._-]` only, cap the length, reject leading dots, reject reserved Windows names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`…). This is defense in depth, not the primary check.
4. **Separate upload storage from the webroot.** Files you accept from callers live in a directory your web server cannot serve and your runtime cannot execute. If you serve them back, stream them through a handler that sets `Content-Disposition: attachment` and an explicit `Content-Type`.
5. **Server-generated filenames.** Persist uploads under a random identifier (`Guid.NewGuid().ToString("N")`, `secrets.token_urlsafe(16)`), and keep the user's name as a separate metadata column. The filesystem layer never sees user strings.
6. **Zip slip: validate every entry.** When extracting an archive, resolve each entry's target path under the extraction root and fail the extraction if any entry escapes. Don't trust `ExtractToDirectory` / `extractall` — some runtimes check, some don't, version-dependent.
7. **Fail closed.** An input that cannot be resolved, cannot be safely joined, or fails the containment check returns an error. Do not fall back to "the root directory" or "skip the file and continue."

## Anti-patterns
- `Path.Combine(uploadRoot, userFilename)` and trusting the result — `Path.Combine` does nothing about `..` or absolute paths.
- `open(f"./uploads/{filename}")` or its .NET twin. String interpolation over a filesystem API is path traversal with extra steps.
- Regex-stripping `..` from the input. Unicode (`%c0%ae%c0%ae`), encoded slashes, and Windows alternate separators (`\`) routinely bypass these filters.
- Serving uploaded files directly from the same directory the static-file middleware serves from.
- `ZipFile.ExtractToDirectory` / `zipfile.extractall` without per-entry containment checks.
- Catching a "file not found" and retrying with a prefix stripped — the attacker can now enumerate your filesystem by trial.
- Logging the resolved path on failure with the raw user input inline — both pieces together give an attacker a differential oracle for valid vs. blocked names.

## References
- OWASP ASVS V12.3 — File Execution
- OWASP File Upload Cheat Sheet
- CWE-22 — Improper Limitation of a Pathname to a Restricted Directory
