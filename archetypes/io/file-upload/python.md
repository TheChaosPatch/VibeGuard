---
schema_version: 1
archetype: io/file-upload
language: python
principles_file: _principles.md
libraries:
  preferred: python-magic (libmagic bindings)
  acceptable:
    - Pillow (for image re-encoding)
    - filetype (pure-Python magic-byte detection)
  avoid:
    - name: mimetypes.guess_type
      reason: Guesses from the extension, which is caller-controlled and trivially spoofed.
    - name: imghdr
      reason: Deprecated since Python 3.11, removed in 3.13; limited format support.
minimum_versions:
  python: "3.10"
---

# Secure File Upload Handling -- Python

## Library choice
`python-magic` (bindings to `libmagic`) inspects the file's actual bytes to determine its type -- the only reliable approach. `mimetypes.guess_type` guesses from the extension and must never be used as a security control. For image uploads, `Pillow` provides re-encoding that strips embedded payloads and EXIF data. `filetype` is an acceptable pure-Python alternative when installing `libmagic` is impractical.

## Reference implementation
```python
from __future__ import annotations

import io
import secrets
from pathlib import Path
from typing import Final

import magic
from PIL import Image

_MAX_SIZE: Final[int] = 10 * 1024 * 1024  # 10 MiB
_ALLOWED_MIME: Final[frozenset[str]] = frozenset({"image/jpeg", "image/png", "image/webp"})
_MIME_TO_EXT: Final[dict[str, str]] = {
    "image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp",
}

def accept_upload(content: bytes, original_filename: str, upload_dir: Path) -> tuple[Path, str]:
    if len(content) == 0:
        raise ValueError("Empty file.")
    if len(content) > _MAX_SIZE:
        raise ValueError(f"File exceeds {_MAX_SIZE} byte limit.")

    detected_mime = magic.from_buffer(content[:2048], mime=True)
    if detected_mime not in _ALLOWED_MIME:
        raise ValueError(f"File type {detected_mime!r} is not permitted.")

    # Re-encode through Pillow to strip polyglot payloads and EXIF.
    img = Image.open(io.BytesIO(content))
    img.verify()
    img = Image.open(io.BytesIO(content))  # re-open after verify
    clean = io.BytesIO()
    img.save(clean, format=img.format, exif=b"")

    ext = _MIME_TO_EXT[detected_mime]
    safe_name = secrets.token_urlsafe(16) + ext
    dest = (upload_dir / safe_name).resolve()
    if not dest.is_relative_to(upload_dir.resolve()):
        raise PermissionError("Storage path escapes upload directory.")
    dest.write_bytes(clean.getvalue())
    return dest, original_filename
```

## Language-specific gotchas
- `python-magic` and `file-magic` are two different PyPI packages with incompatible APIs. The correct one is `python-magic` (`import magic; magic.from_buffer(...)`).
- `Pillow`'s `Image.open` is lazy -- it does not read the full file until you access pixel data. Call `img.verify()` to detect corruption, then re-open the image because `verify` invalidates the object.
- `Pillow` can be tricked into decompression bombs (a small file that expands to gigabytes of pixel data). Set `Image.MAX_IMAGE_PIXELS` to a safe threshold before processing.
- Framework-level size limits (e.g., Django's `DATA_UPLOAD_MAX_MEMORY_SIZE`, FastAPI/Starlette's body limit) must be set in addition to the handler-level check. The framework limit prevents buffering the oversized body; the handler limit is a defense-in-depth assertion.
- `original_filename` from a multipart upload can contain path separators, null bytes, or be absurdly long. Never use it for filesystem operations. Store it as metadata only, after truncating and sanitizing for display.
- When serving uploads back, always set `Content-Disposition: attachment; filename="safe_name"` and an explicit `Content-Type` from your allowlist. Never reflect the stored MIME type verbatim if it came from the uploader.

## Tests to write
- Happy path: a valid JPEG under the size limit is stored with a random filename and the original name is returned as metadata.
- Oversized file: content exceeding the limit raises `ValueError` before any disk write.
- Wrong type: a PDF renamed to `.jpg` is detected by magic bytes and rejected.
- Empty file: zero-byte content raises `ValueError`.
- Polyglot: a JPEG with embedded JavaScript is re-encoded; the output does not contain the script payload.
