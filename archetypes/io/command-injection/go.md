---
schema_version: 1
archetype: io/command-injection
language: go
principles_file: _principles.md
libraries:
  preferred: os/exec
  acceptable: []
  avoid:
    - name: os/exec with shell wrappers (sh -c, cmd /c)
      reason: Re-introduces a shell interpreter, enabling metacharacter injection.
    - name: syscall.Exec with unsanitized args
      reason: Low-level exec with no argument safety net; only for advanced use cases with fully trusted inputs.
minimum_versions:
  go: "1.22"
---

# Command Injection Defense -- Go

## Library choice
`os/exec` is the standard library's process-spawning API and it does the right thing by default: `exec.Command(name, args...)` takes the executable and arguments as separate strings, passes them as discrete `argv` entries, and never invokes a shell. There is no `shell=True` equivalent to accidentally reach for. The danger is circumventing this design by calling `exec.Command("sh", "-c", userString)` or `exec.Command("cmd", "/c", userString)`, which re-introduces the shell and every injection vector with it.

## Reference implementation
```go
package imaging

import (
	"context"
	"fmt"
	"os/exec"
	"path/filepath"
	"strings"
	"time"
)

const ffmpegPath = "/usr/bin/ffmpeg"

var allowedFormats = map[string]struct{}{"png": {}, "jpg": {}, "webp": {}}

func ConvertImage(ctx context.Context, source, outputDir, format string) (string, error) {
	lower := strings.ToLower(format)
	if _, ok := allowedFormats[lower]; !ok {
		return "", fmt.Errorf("unsupported format: %q", format)
	}
	resolvedSource, err := filepath.Abs(source)
	if err != nil {
		return "", fmt.Errorf("resolve source: %w", err)
	}
	base := strings.TrimSuffix(filepath.Base(resolvedSource), filepath.Ext(resolvedSource))
	dest := filepath.Join(outputDir, base+"."+lower)
	absOut, _ := filepath.Abs(outputDir)
	if !strings.HasPrefix(filepath.Clean(dest), absOut+string(filepath.Separator)) {
		return "", fmt.Errorf("output path escapes target directory")
	}
	// Argument slice -- no shell, each element is a discrete argv entry.
	ctx, cancel := context.WithTimeout(ctx, 30*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, ffmpegPath, "-y", "-i", resolvedSource, dest)
	if output, err := cmd.CombinedOutput(); err != nil {
		return "", fmt.Errorf("ffmpeg failed: %w: %s", err, output)
	}
	return dest, nil
}
```

## Language-specific gotchas
- `exec.Command` does not invoke a shell. The first argument is resolved via `PATH` lookup (use `exec.LookPath` explicitly or provide an absolute path). Never wrap user input in `exec.Command("sh", "-c", ...)` -- this is the Go equivalent of `shell=True`.
- `exec.CommandContext` kills the process when the context is cancelled or times out, but only sends `SIGKILL` by default. If you need graceful shutdown, send `SIGTERM` first via `cmd.Process.Signal`, wait briefly, then kill.
- `cmd.CombinedOutput()` buffers all output in memory. For commands that may produce unbounded output, use `cmd.StdoutPipe()` / `cmd.StderrPipe()` with `io.LimitReader`.
- Go's `exec.Command` searches `PATH` for the executable. On systems where an attacker can influence `PATH` or place a binary in the working directory, use an absolute path to the executable.
- `cmd.Dir` sets the working directory for the child process. If you do not set it, the child inherits the parent's working directory, which may be writable by the attacker in containerized or multi-tenant setups.
- On Windows, `exec.Command` uses `CreateProcess`, which has its own argument-parsing rules. The `os/exec` package handles quoting, but be aware that empty arguments and arguments containing spaces or quotes can behave differently than on Unix.

## Tests to write
- Happy path: a valid source and allowed format return the expected output path and no error.
- Rejected format: a format not in the allowlist returns an error containing "unsupported format."
- Shell metacharacters: a source filename containing `$(rm -rf /)` is passed as a literal argv entry and does not trigger command substitution.
- Context timeout: a context with an expired deadline causes the command to be killed and returns a context error.
- Path traversal in output: an output dir combined with a crafted filename that escapes the directory returns an error.
