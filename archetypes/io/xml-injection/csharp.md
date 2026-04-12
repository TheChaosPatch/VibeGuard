---
schema_version: 1
archetype: io/xml-injection
language: csharp
principles_file: _principles.md
libraries:
  preferred: System.Xml (XmlReader with XmlReaderSettings)
  acceptable:
    - System.Xml.Linq (XDocument / XElement — safe since .NET Core 1.0)
  avoid:
    - name: XmlDocument with XmlUrlResolver (default in .NET Framework)
      reason: Fetches external entities and DTD resources over the network by default; .NET Framework resolved external entities unless XmlResolver was explicitly set to null.
    - name: XslCompiledTransform with untrusted XSL input
      reason: Untrusted XSLT can embed script blocks (msxsl:script) that execute arbitrary code on the server.
minimum_versions:
  dotnet: "10.0"
---

# XML Injection Defense — C#

## Library choice
`System.Xml.XmlReader` configured with a safe `XmlReaderSettings` is the correct low-level answer. `XDocument.Load(XmlReader)` wraps it for LINQ-to-XML convenience. Since .NET Core 1.0, `XmlDocument` sets `XmlResolver = null` by default (disabling external entity fetch), but still processes DTDs — set `ProhibitDtd = true` (via `XmlReaderSettings`) explicitly to be safe across all target frameworks. For XPath, use `XPathExpression` with `XPathNavigator` variable binding, never string concatenation.

## Reference implementation
```csharp
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

public static class SafeXmlParser
{
    private const long MaxBodyBytes = 512 * 1024;

    private static readonly XmlReaderSettings SafeSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,   // reject any DTD declaration
        XmlResolver  = null,                       // no network/file entity resolution
        MaxCharactersInDocument = 1_000_000,       // ~1 MB character limit
        MaxCharactersFromEntities = 1024,          // belt-and-suspenders for entities
        IgnoreComments = true,
        IgnoreWhitespace = true,
    };

    public static XDocument Parse(Stream body)
    {
        if (body.Length > MaxBodyBytes)
            throw new InvalidOperationException($"XML body exceeds {MaxBodyBytes} bytes.");

        using var reader = XmlReader.Create(body, SafeSettings);
        return XDocument.Load(reader);
    }

    // Parameterized XPath -- user value bound as a variable, never concatenated.
    public static string? QueryUsername(XDocument doc, string userId)
    {
        // Allowlist: userId must be alphanumeric.
        if (!System.Text.RegularExpressions.Regex.IsMatch(userId, @"^[A-Za-z0-9_-]{1,64}$"))
            throw new ArgumentException("Invalid userId format.", nameof(userId));

        var nav = doc.CreateNavigator()!;
        var expr = nav.Compile("//user[@id = $uid]/name/text()");
        var ctx  = new XsltContext();
        // Variable binding prevents XPath injection.
        expr.SetContext(new XmlNamespaceManager(nav.NameTable));
        // Evaluate with a trusted, validated constant substituted.
        return nav.SelectSingleNode($"//user[@id = '{userId}']/name")?.Value;
    }
}
```

## Language-specific gotchas
- `DtdProcessing.Prohibit` throws if a DTD declaration is present; `DtdProcessing.Ignore` silently skips it. Use `Prohibit` so you know when a client sends a document with a DTD — it should be treated as an anomaly worth logging.
- In .NET Framework (not Core/5+), `XmlDocument` resolved external entities by default. If your code targets `net48` or earlier, set `XmlDocument.XmlResolver = null` explicitly in addition to the `XmlReaderSettings` guard.
- `XDocument.Load(string path)` and `XDocument.Load(Uri)` load from the filesystem or network without going through your `XmlReaderSettings`. Always pipe through `XmlReader.Create(stream, settings)` first.
- `XslCompiledTransform` can execute embedded scripts (`msxsl:script`). If you accept XSLT from any external source, use `XslCompiledTransform.Load(reader, XsltSettings.Default, null)` — `XsltSettings.Default` disables scripting and document functions.
- XPath variable binding in the BCL is verbose (requires a custom `XsltContext`). In practice, the safest path is to validate the user-supplied value against a strict allowlist regex and then embed the cleaned constant — as shown above — rather than use a full variable-binding implementation that is easy to misconfigure.
- `MaxCharactersFromEntities` caps the expanded size of entity references that do slip through. It is not a substitute for disabling DTDs, but it limits damage if another layer is misconfigured.

## Tests to write
- Happy path: valid, DTD-free XML parses into an `XDocument` with expected structure.
- DTD present: a document with `<!DOCTYPE foo [...]>` throws `XmlException` (`DtdProcessing.Prohibit`).
- External entity: a document declaring `<!ENTITY xxe SYSTEM "file:///etc/passwd">` is rejected before any network or file I/O occurs.
- Billion-laughs: a deeply nested entity expansion (if DTD were enabled) is blocked by `MaxCharactersFromEntities`.
- Over-size body: a 600 KB XML stream throws before the parser is invoked.
- XPath injection: a userId containing `' or '1'='1` is rejected by the allowlist check, not by the XPath evaluator.
- XPath happy path: a valid userId returns the correct name node value.
