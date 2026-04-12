---
schema_version: 1
archetype: http/xss
language: csharp
principles_file: _principles.md
libraries:
  preferred: Razor (auto-encoded by default)
  acceptable:
    - System.Text.Encodings.Web (HtmlEncoder, JavaScriptEncoder, UrlEncoder)
    - HtmlSanitizer (Ganss.Xss)
  avoid:
    - name: String concatenation for HTML
      reason: Bypasses all encoding; the canonical one-line XSS.
    - name: HttpUtility.HtmlEncode alone
      reason: Only handles HTML body context; wrong for attributes, JS, URLs.
minimum_versions:
  dotnet: "10.0"
---

# Cross-Site Scripting Defense — C#

## Library choice
Razor Pages and Razor views auto-encode all `@` expressions by default — this is the primary defense. For Minimal APIs or non-Razor responses that produce HTML fragments, use `System.Text.Encodings.Web.HtmlEncoder.Default.Encode()` for HTML body, `JavaScriptEncoder.Default.Encode()` for JS contexts, and `UrlEncoder.Default.Encode()` for URL parameters. If you must accept rich HTML input (e.g., from a WYSIWYG editor), use `HtmlSanitizer` (Ganss.Xss) with an explicit allowlist of tags and attributes. Never call `Html.Raw()` on user-supplied data without sanitizing first.

## Reference implementation
```csharp
using System.Text.Encodings.Web;
using Ganss.Xss;

public static class OutputEncoding
{
    // For embedding user text in an HTML body outside of Razor.
    public static string ForHtml(string untrusted) =>
        HtmlEncoder.Default.Encode(untrusted);

    // For embedding user text inside a <script> block as a JS string.
    public static string ForJavaScript(string untrusted) =>
        JavaScriptEncoder.Default.Encode(untrusted);

    // For embedding user text in a URL query parameter value.
    public static string ForUrl(string untrusted) =>
        UrlEncoder.Default.Encode(untrusted);
}

// Sanitizer for rich-text fields that must accept a subset of HTML.
public sealed class RichTextSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        s.AllowedTags.UnionWith(["p", "br", "strong", "em", "ul", "ol", "li", "a"]);
        s.AllowedAttributes.Clear();
        s.AllowedAttributes.Add("href");
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("https");
        return s;
    }

    public static string Sanitize(string untrustedHtml) =>
        Sanitizer.Sanitize(untrustedHtml);
}
```

## Language-specific gotchas
- `@Html.Raw(userInput)` is the most common ASP.NET XSS vector. Grep your codebase for `Html.Raw` and `MarkupString` — every call site must sanitize its input or be justified in a comment.
- Razor auto-escapes `@variable`, but not content inside `@Html.Raw()` or `@((MarkupString)value)`. Blazor's `MarkupString` is the same risk surface.
- `System.Text.Json` serialization into a `<script>` tag requires `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` to be avoided — use the default encoder, which escapes `<`, `>`, and `&` in JSON string values.
- Tag Helpers like `asp-for` are auto-encoded. Hand-built `<input value="@raw">` without the Tag Helper is not.
- `Content-Type` matters: if a Minimal API endpoint returns `Results.Content(html, "text/html")`, it produces raw HTML with no Razor pipeline — you own the encoding.
- CSP headers must be set via middleware (`app.Use(...)` or a library like `NetEscapades.AspNetCore.SecurityHeaders`), not in `<meta>` tags that can be injected before by an attacker.

## Tests to write
- `ForHtml("<script>alert(1)</script>")` returns an encoded string containing no raw `<script>` tag.
- `ForJavaScript("</script><script>alert(1)")` returns a string that does not break out of a JS string literal.
- `RichTextSanitizer.Sanitize("<img onerror=alert(1)>")` strips the `<img>` entirely.
- `RichTextSanitizer.Sanitize("<a href='javascript:alert(1)'>click</a>")` strips the `href` or the tag (only `https` is allowed).
- Razor view rendering a model property containing `<script>` produces encoded output verified by parsing the response HTML.
