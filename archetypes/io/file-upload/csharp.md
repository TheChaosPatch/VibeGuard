---
schema_version: 1
archetype: io/file-upload
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.IO + ASP.NET Core IFormFile
  acceptable:
    - ImageSharp (for image re-encoding)
    - MimeDetective (magic-byte detection)
  avoid:
    - name: ContentType from IFormFile without validation
      reason: The Content-Type header is caller-controlled and trivially spoofed.
    - name: Path.Combine with IFormFile.FileName
      reason: The filename is untrusted and can contain path separators or absolute paths.
minimum_versions:
  dotnet: "10.0"
---

# Secure File Upload Handling -- C#

## Library choice
ASP.NET Core's `IFormFile` provides the transport-layer abstraction. For content-type detection by magic bytes, use `MimeDetective` or read the header bytes yourself against a known signature table. For image re-encoding (stripping EXIF, polyglot payloads), `SixLabors.ImageSharp` is the modern, cross-platform choice. Never trust `IFormFile.ContentType` or `IFormFile.FileName` as security controls -- both are caller-supplied strings.

## Reference implementation
```csharp
public sealed class FileUploadService(string uploadRoot)
{
    private const int MaxSizeBytes = 10 * 1024 * 1024; // 10 MiB
    private readonly string _root = Path.GetFullPath(uploadRoot);

    private static readonly FrozenDictionary<string, byte[]> Signatures = new Dictionary<string, byte[]>
    {
        ["image/jpeg"] = [0xFF, 0xD8, 0xFF],
        ["image/png"]  = [0x89, 0x50, 0x4E, 0x47],
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> MimeToExt = new Dictionary<string, string>
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png",
    }.ToFrozenDictionary();

    public async Task<string> AcceptAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file.Length is 0 or > MaxSizeBytes)
            throw new ArgumentException("File size out of acceptable range.");

        var header = new byte[8];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, false, ct);
        var mime = DetectMime(header.AsSpan(0, read))
            ?? throw new InvalidOperationException("File type is not permitted.");

        var safeName = $"{Guid.NewGuid():N}{MimeToExt[mime]}";
        var dest = Path.GetFullPath(Path.Combine(_root, safeName));
        if (!dest.StartsWith(_root + Path.DirectorySeparatorChar))
            throw new UnauthorizedAccessException("Path escapes upload root.");

        stream.Position = 0;
        await using var output = File.Create(dest);
        await stream.CopyToAsync(output, ct);
        return safeName;
    }

    private static string? DetectMime(ReadOnlySpan<byte> header)
    {
        foreach (var (mime, sig) in Signatures)
            if (header.Length >= sig.Length && header[..sig.Length].SequenceEqual(sig))
                return mime;
        return null;
    }
}
```

## Language-specific gotchas
- Configure `Kestrel`'s `MaxRequestBodySize` (or the reverse proxy's equivalent) to reject oversized requests before ASP.NET Core buffers the body. The handler-level check is defense-in-depth, not the primary control.
- `IFormFile.FileName` can contain path separators, absolute paths, or null characters depending on the client. Never use it in `Path.Combine` or any filesystem API. Store it as display metadata only, after truncating to a safe length.
- `IFormFile.ContentType` is the value from the `Content-Type` header of the multipart section -- it is not validated by ASP.NET Core. Detect the actual type from the file's magic bytes.
- For image re-encoding with ImageSharp, load the image, strip metadata, and re-save. This neutralizes polyglot payloads and embedded scripts. Set `Configuration.Default.MemoryAllocator` limits to prevent decompression bombs.
- Use `[RequestSizeLimit]` or `[DisableRequestSizeLimit]` attributes to override global limits per endpoint. An endpoint that accepts a 100 MiB video upload should not raise the global limit for all endpoints.
- The `IFormFile` stream may not be seekable depending on buffering configuration. If you need to read headers and then copy the full stream, either enable buffering (`EnableBuffering()`) or copy to a `MemoryStream` first for small files.

## Tests to write
- Happy path: a valid JPEG under the size limit is stored with a GUID filename and the safe name is returned.
- Oversized file: content exceeding the limit throws `ArgumentException` before any disk write.
- Wrong type: a PDF with a `.jpg` extension is detected by magic bytes and rejected.
- Empty file: a zero-length upload throws `ArgumentException`.
- Path escape: verify that a GUID-based filename cannot escape the upload root (regression test for the trailing-separator containment check).
