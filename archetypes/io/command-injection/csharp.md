---
schema_version: 1
archetype: io/command-injection
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Diagnostics.Process with ArgumentList
  acceptable: []
  avoid:
    - name: Process.Start with a single Arguments string
      reason: A single string is parsed by the OS shell rules and is vulnerable to argument injection via spaces, quotes, and metacharacters.
    - name: cmd.exe /c or powershell -Command wrappers
      reason: Re-introduces a shell interpreter, enabling full command injection through metacharacters.
minimum_versions:
  dotnet: "10.0"
---

# Command Injection Defense -- C#

## Library choice
`System.Diagnostics.Process` with `ProcessStartInfo.ArgumentList` (available since .NET Core 2.1) passes each argument as a discrete element to the OS without shell interpretation. The older `ProcessStartInfo.Arguments` property takes a single string that follows platform-specific quoting rules and is error-prone. Never route commands through `cmd.exe /c` or `powershell -Command` -- these re-introduce a shell and re-enable injection.

## Reference implementation
```csharp
using System.Diagnostics;

public sealed class ImageConverter
{
    private const string FfmpegPath = "/usr/bin/ffmpeg";
    private static readonly FrozenSet<string> AllowedFormats =
        FrozenSet.ToFrozenSet(["png", "jpg", "webp"]);

    public static string Convert(string sourcePath, string outputDir, string format)
    {
        var fmt = format.ToLowerInvariant();
        if (!AllowedFormats.Contains(fmt))
            throw new ArgumentException($"Unsupported format: {format}");

        var resolvedSource = Path.GetFullPath(sourcePath);
        var dest = Path.GetFullPath(
            Path.Combine(outputDir, $"{Path.GetFileNameWithoutExtension(resolvedSource)}.{fmt}"));
        var rootWithSep = Path.GetFullPath(outputDir) + Path.DirectorySeparatorChar;
        if (!dest.StartsWith(rootWithSep, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Output path escapes target directory.");

        using var process = new Process();
        process.StartInfo.FileName = FfmpegPath;
        // ArgumentList: each element is a discrete argv entry -- no shell.
        process.StartInfo.ArgumentList.Add("-y");
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(resolvedSource);
        process.StartInfo.ArgumentList.Add(dest);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.Start();

        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("ffmpeg exceeded the time limit.");
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"ffmpeg exited {process.ExitCode}: {stderr[..Math.Min(stderr.Length, 500)]}");
        return dest;
    }
}
```

## Language-specific gotchas
- `ProcessStartInfo.Arguments` (the string property) and `ProcessStartInfo.ArgumentList` (the collection) are mutually exclusive at runtime -- setting one clears the other. Always use `ArgumentList` for safety.
- `UseShellExecute = true` (the default on .NET Framework, `false` on .NET Core+) opens the path through the Windows shell. Ensure it is explicitly `false`.
- `Process.WaitForExit(TimeSpan)` returns `false` on timeout but does not kill the process. You must call `Kill(entireProcessTree: true)` yourself to prevent orphaned children.
- On Windows, even without a shell, `CreateProcess` applies its own command-line parsing rules. `ArgumentList` handles the escaping, but if you fall back to the `Arguments` string property, embedded quotes and backslashes before quotes follow arcane Win32 rules that attackers know better than you do.
- Redirecting `StandardError` without reading it before `WaitForExit` can deadlock if the buffer fills. Read stderr asynchronously or use `OutputDataReceived`/`ErrorDataReceived` events for large outputs.
- Never pass secrets as arguments. On Windows, `wmic process get commandline` and Task Manager expose the full argument string to any user on the machine.

## Tests to write
- Happy path: a valid source and allowed format produce the expected output path.
- Rejected format: a format outside the allowlist throws `ArgumentException`.
- Shell metacharacters in filename: a source path containing `& del C:\` is passed as a literal argument and does not execute a second command.
- Timeout: mock or use a process that exceeds the deadline; verify `TimeoutException` is thrown and the process tree is killed.
- Non-zero exit: a failing invocation throws `InvalidOperationException` with truncated stderr context.
