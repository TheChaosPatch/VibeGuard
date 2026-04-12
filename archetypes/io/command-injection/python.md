---
schema_version: 1
archetype: io/command-injection
language: python
principles_file: _principles.md
libraries:
  preferred: subprocess
  acceptable: []
  avoid:
    - name: os.system
      reason: Invokes a shell unconditionally, enabling injection via metacharacters.
    - name: os.popen
      reason: Spawns a shell to run the command string; equivalent to shell=True.
    - name: subprocess with shell=True
      reason: Routes the command through /bin/sh, re-enabling every metacharacter the argument list was supposed to neutralize.
minimum_versions:
  python: "3.10"
---

# Command Injection Defense -- Python

## Library choice
`subprocess` with `shell=False` (the default) is the only safe choice. Pass the command as a list of strings so each element becomes a discrete `argv` entry with no shell interpretation. `os.system`, `os.popen`, and `subprocess` with `shell=True` all route through a shell and must be treated as banned APIs in any codebase that processes untrusted input.

## Reference implementation
```python
from __future__ import annotations

import subprocess
from pathlib import Path
from typing import Final

_FFMPEG: Final[str] = "/usr/bin/ffmpeg"
_ALLOWED_FORMATS: Final[frozenset[str]] = frozenset({"png", "jpg", "webp"})
_TIMEOUT_SECONDS: Final[int] = 30


def convert_image(
    source: Path,
    output_dir: Path,
    output_format: str,
) -> Path:
    fmt = output_format.lower()
    if fmt not in _ALLOWED_FORMATS:
        raise ValueError(f"unsupported format: {output_format!r}")

    # Resolve paths to prevent traversal and verify containment.
    resolved_source = source.resolve(strict=True)
    dest = (output_dir / f"{resolved_source.stem}.{fmt}").resolve()
    if not dest.is_relative_to(output_dir.resolve()):
        raise PermissionError("output path escapes target directory")

    # Argument list -- no shell, each element is a discrete argv entry.
    result = subprocess.run(
        [_FFMPEG, "-y", "-i", str(resolved_source), str(dest)],
        capture_output=True,
        timeout=_TIMEOUT_SECONDS,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(
            f"ffmpeg exited {result.returncode}: "
            f"{result.stderr.decode(errors='replace')[:500]}"
        )
    return dest
```

## Language-specific gotchas
- `shell=False` is the default for `subprocess.run`, but many tutorials and Stack Overflow answers pass `shell=True` "because it's easier." Treat any occurrence of `shell=True` in code review as a finding that requires justification.
- `shlex.quote()` exists to escape a single argument for a shell command string. It is defense-in-depth for logging or edge cases where a shell is truly unavoidable -- it is not a substitute for using an argument list.
- `shlex.split()` is for parsing a trusted configuration string into an argument list, not for sanitizing untrusted input. Splitting user input with `shlex.split` and passing the result to `subprocess.run` is still injection if the user controls the structure.
- `subprocess.run` with `timeout` raises `subprocess.TimeoutExpired`, which leaves the child process running. Call `result.kill()` in the exception handler and `result.communicate()` to reap it, or use the context-manager form via `Popen`.
- On Windows, `subprocess` with `shell=False` still searches `PATH` for the executable. Use an absolute path to prevent DLL/binary planting attacks.
- `capture_output=True` buffers stdout and stderr in memory. For commands that may produce unbounded output, use `Popen` with streaming reads or set explicit size limits.

## Tests to write
- Happy path: a valid source file and an allowed format produce the expected output file.
- Rejected format: a format not in the allowlist raises `ValueError`.
- Malicious filename: a source path containing shell metacharacters (`; rm -rf /`) does not invoke a shell -- the command either fails cleanly or processes the literal filename.
- Timeout: a command that hangs beyond the deadline raises `subprocess.TimeoutExpired`.
- Non-zero exit code: a failing ffmpeg invocation raises `RuntimeError` with truncated stderr.
