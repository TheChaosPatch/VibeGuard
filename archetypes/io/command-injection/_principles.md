---
schema_version: 1
archetype: io/command-injection
title: Command Injection Defense
summary: Preventing untrusted input from being interpreted as OS commands or arguments.
applies_to: [csharp, python, go]
status: draft
keywords:
  - command-injection
  - os-command
  - shell
  - exec
  - subprocess
  - system
  - popen
  - rce
  - injection
  - process
  - spawn
  - argument-injection
  - shell-true
  - remote-code-execution
related_archetypes:
  - io/input-validation
  - io/path-traversal
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: OS Command Injection Defense Cheat Sheet
  cwe: "78"
---

# Command Injection Defense -- Principles

## When this applies
Any time your code spawns an OS process and any part of the command line -- the executable name, its arguments, environment variables, or the working directory -- is influenced by a value you did not hard-code. This includes web form fields used to construct an `ffmpeg` invocation, filenames fed to `convert`, tenant identifiers used to build a `pg_dump` command, and even "internal" values read from a queue or database that another service wrote from user input. If the string reaches a shell or an `exec` syscall and an attacker can alter it, this archetype applies.

## Architectural placement
Command execution is isolated behind a purpose-built service or wrapper that owns the entire argument vector. Callers pass strongly-typed parameters (`ConvertImageRequest { SourcePath, OutputFormat }`) and the wrapper maps those to a fixed executable and a static argument template. The wrapper never passes a caller-supplied string as a raw argument without validating it against an allowlist. No code outside the wrapper constructs command lines, invokes shells, or calls `system()` equivalents.

## Principles
1. **Never invoke a shell.** Use APIs that take an argument array (`subprocess.run([...])`, `Process.StartInfo.ArgumentList`, `exec.Command(name, args...)`) instead of APIs that pass a single string through a shell (`os.system`, `shell=True`, `cmd /c`). Shells interpret metacharacters -- semicolons, pipes, backticks, `$()` -- and that interpretation is the injection vector.
2. **Separate the executable from its arguments.** The executable path must be a literal or a configuration constant, never a user-supplied string. Arguments must be passed as discrete list elements so the OS delivers them to the process verbatim, with no shell parsing.
3. **Allowlist argument values.** When an argument must vary based on input, validate it against a closed set of permitted values. A format selector should match `{"png", "jpg", "webp"}`, not "anything that doesn't contain a semicolon."
4. **Never embed user input in a command string, even with quoting.** Shell quoting is fragile, platform-dependent, and historically riddled with bypasses. The correct answer is an argument array, not a cleverer escaping function.
5. **Restrict the executable search path.** Use absolute paths to the executable (`/usr/bin/ffmpeg`, not `ffmpeg`). If the attacker can influence `PATH` or place a binary in the working directory, a bare name resolves to their payload.
6. **Drop privileges and constrain the child process.** Run the child with the minimum required permissions, a restricted environment, and -- where the OS supports it -- a timeout. A successful injection in a process running as `root` is catastrophic; the same injection in a sandboxed, non-privileged process is contained.
7. **Log every invocation.** Record the executable, argument vector, exit code, and wall-clock time. Do not log argument values that contain secrets (credentials passed via CLI are an anti-pattern anyway -- use environment variables or temp files with restrictive permissions).

## Anti-patterns
- `os.system(f"convert {user_filename} output.png")` -- the classic injection: a filename containing `; rm -rf /` is executed as a command.
- `subprocess.run(cmd, shell=True)` when `cmd` includes any caller-supplied fragment. `shell=True` is the single most common enabler of command injection in Python.
- Building a command string and calling `Process.Start(new ProcessStartInfo { FileName = "cmd", Arguments = "/c " + commandString })`. This is `shell=True` for .NET.
- Using `shlex.quote()` or `ProcessStartInfo.ArgumentList` as the primary defense while still routing through a shell. Quoting is defense-in-depth, not the primary control.
- Accepting an arbitrary executable name from the user ("run this tool for me"). The allowlist must cover both arguments and the executable itself.
- Passing secrets as command-line arguments. On Linux, `/proc/PID/cmdline` is world-readable by default; on Windows, `wmic process` exposes the full command line.
- Ignoring exit codes and stderr. A partially-successful command that the attacker manipulated into doing extra work looks like success if you only check stdout.

## References
- OWASP ASVS V5.3 -- Output Encoding and Injection Prevention
- OWASP OS Command Injection Defense Cheat Sheet
- CWE-78 -- Improper Neutralization of Special Elements used in an OS Command
