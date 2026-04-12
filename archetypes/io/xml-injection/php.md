---
schema_version: 1
archetype: io/xml-injection
language: php
principles_file: _principles.md
libraries:
  preferred: "DOMDocument with LIBXML_NONET | LIBXML_NOENT flags"
  acceptable:
    - "SimpleXMLElement with LIBXML_NONET | LIBXML_NOENT flags"
  avoid:
    - name: simplexml_load_string / simplexml_load_file without libxml flags
      reason: Uses libxml2 defaults which resolve external entities and fetch network resources.
    - name: DOMDocument::loadXML without setting libxml options
      reason: External entities are resolved by default in libxml2; omitting LIBXML_NOENT leaves XXE open.
    - name: XPath with string concatenation of user input
      reason: DOMXPath::query accepts a plain string; concatenating user data into it enables XPath injection.
minimum_versions:
  php: "8.3"
---

# XML Injection Defense — PHP

## Library choice
PHP's XML stack is built on libxml2. The safe configuration requires passing `LIBXML_NOENT` (do not expand entities) and `LIBXML_NONET` (no network access) to every load call, and using `libxml_disable_entity_loader(true)` as a belt-and-suspenders call (deprecated in PHP 8.0 in favor of the flags, but still useful on mixed-version codebases). Use `DOMXPath` for querying; validate user-supplied values against an allowlist before embedding them in expressions.

## Reference implementation
```php
<?php
declare(strict_types=1);

final class SafeXmlParser
{
    private const MAX_BODY_BYTES = 524_288; // 512 KB
    private const VALID_ID = '/^[A-Za-z0-9_-]{1,64}$/';

    public static function parse(string $body): \DOMDocument
    {
        if (strlen($body) > self::MAX_BODY_BYTES) {
            throw new \LengthException('XML body exceeds ' . self::MAX_BODY_BYTES . ' bytes');
        }

        $dom = new \DOMDocument();
        // LIBXML_NOENT: do not expand entities.
        // LIBXML_NONET: disable network access during parsing.
        // LIBXML_DTDLOAD is NOT set, so external DTDs are not loaded.
        $options = LIBXML_NOENT | LIBXML_NONET;

        libxml_use_internal_errors(true);
        $ok = $dom->loadXML($body, $options);
        $errors = libxml_get_errors();
        libxml_clear_errors();

        if (!$ok || !empty($errors)) {
            throw new \InvalidArgumentException('XML parse error: ' . ($errors[0]->message ?? 'unknown'));
        }
        return $dom;
    }

    public static function queryUsername(\DOMDocument $dom, string $userId): ?string
    {
        if (!preg_match(self::VALID_ID, $userId)) {
            throw new \InvalidArgumentException('Invalid userId format');
        }
        $xpath = new \DOMXPath($dom);
        // Allowlisted userId is embedded in a static template -- no free-form concatenation.
        $nodes = $xpath->query('//user[@id="' . $userId . '"]/name');
        return ($nodes && $nodes->length > 0) ? $nodes->item(0)->textContent : null;
    }
}
```

## Language-specific gotchas
- `libxml_disable_entity_loader(true)` was the primary XXE defense in PHP 7 and below. In PHP 8.0+ it is deprecated because `LIBXML_NOENT` achieves the same effect per-call. Use the flag-based approach on PHP 8+; keep `libxml_disable_entity_loader` on legacy PHP 7 code.
- `LIBXML_NOENT` means "do not expand entity references" — counterintuitively, the flag name reads as "no entities" but its behaviour is to substitute entities with their text rather than expand them as XML nodes. Regardless, setting it prevents the entity-expansion attack vector.
- `LIBXML_DTDLOAD` is not set in the example. Do not set it; it allows loading the external DTD subset, which is the XXE network-fetch vector.
- `libxml_use_internal_errors(true)` suppresses PHP warnings and lets you retrieve errors via `libxml_get_errors()`. Always call `libxml_clear_errors()` after inspection, or errors accumulate across requests.
- `DOMXPath::query` returns a `DOMNodeList` or `false` on error. Always check both before calling `->item(0)`.
- PHP's `SimpleXMLElement` coerces values to strings implicitly; unvalidated user input in an XPath passed to `SimpleXMLElement::xpath()` has the same injection risk as `DOMXPath`.
- `preg_match` returns `false` on regex error and `0` on no match — use `!== 1` rather than `!` if you need to distinguish error from no-match, but the example's allowlist regex is simple enough that `!preg_match` is safe.

## Tests to write
- Happy path: valid XML string parses into a `DOMDocument` with expected root element name.
- External entity: `<!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>` does not trigger file read (assert `LIBXML_NOENT` is set and no file I/O occurs).
- Over-size body: a string of `MAX_BODY_BYTES + 1` bytes throws `LengthException`.
- Malformed XML: a truncated XML string throws `InvalidArgumentException` with parse error message.
- XPath injection: userId `"]/parent::* | //*[@id="` is rejected by the allowlist regex.
- XPath happy path: a known userId returns the correct name text content.
