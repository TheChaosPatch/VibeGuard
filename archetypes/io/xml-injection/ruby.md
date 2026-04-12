---
schema_version: 1
archetype: io/xml-injection
language: ruby
principles_file: _principles.md
libraries:
  preferred: Nokogiri with parse options NONET | NOENT
  acceptable:
    - REXML with entity expansion disabled (StopProcessingInstruction handler)
  avoid:
    - name: Nokogiri with default parse options
      reason: Default options allow network entity resolution; NONET must be set explicitly.
    - name: REXML without entity expansion limit
      reason: REXML has no built-in limit on entity expansion depth or count; vulnerable to billion-laughs DoS.
    - name: LibXML-Ruby with default options
      reason: Same libxml2 defaults as PHP — external entities resolved unless disabled.
minimum_versions:
  ruby: "3.3"
---

# XML Injection Defense — Ruby

## Library choice
`Nokogiri` is the standard Ruby XML library and wraps libxml2. Configure it with `Nokogiri::XML::ParseOptions::NONET | Nokogiri::XML::ParseOptions::NOENT` to block network entity resolution and entity expansion respectively. Since Nokogiri 1.5.4, a `NONET` parse options constant is available; always pass it explicitly. For XPath, use Nokogiri's `xpath` method with a namespace mapping and validate user-supplied values against an allowlist before substitution.

## Reference implementation
```ruby
# frozen_string_literal: true

require "nokogiri"

module SafeXmlParser
  MAX_BODY_BYTES = 512 * 1024
  VALID_ID = /\A[A-Za-z0-9_-]{1,64}\z/

  SAFE_OPTIONS = Nokogiri::XML::ParseOptions::NONET |
                 Nokogiri::XML::ParseOptions::NOENT |
                 Nokogiri::XML::ParseOptions::NOBLANKS

  # Returns a Nokogiri::XML::Document or raises on parse failure.
  def self.parse(body)
    raise ArgumentError, "XML body exceeds #{MAX_BODY_BYTES} bytes" if body.bytesize > MAX_BODY_BYTES

    doc = Nokogiri::XML(body) { |config| config.options = SAFE_OPTIONS }
    raise ArgumentError, "XML parse errors: #{doc.errors.map(&:message).join('; ')}" if doc.errors.any?

    doc
  end

  # Returns the name text for the given user_id, or nil.
  # user_id is validated before use; it is not interpolated freely.
  def self.query_username(doc, user_id)
    raise ArgumentError, "Invalid user_id format" unless VALID_ID.match?(user_id)

    # Embed the allowlisted, validated user_id into a static XPath template.
    node = doc.at_xpath("//user[@id='#{user_id}']/name")
    node&.text&.strip
  end
end
```

## Language-specific gotchas
- `Nokogiri::XML::ParseOptions::NONET` prevents Nokogiri from making network requests to resolve external entities or DTD resources. Without it, a document with `<!ENTITY xxe SYSTEM "http://attacker.com/evil">` causes an outbound HTTP request during parsing.
- `Nokogiri::XML::ParseOptions::NOENT` substitutes entity references with their text content rather than resolving them as XML structures. Combined with `NONET` it neutralizes the XXE file-read vector.
- The block form `Nokogiri::XML(body) { |config| config.options = SAFE_OPTIONS }` is the idiomatic way to set options. Avoid `Nokogiri::XML.parse(body, nil, nil, options)` — the positional `nil` for URL and encoding is easy to misorder.
- `doc.errors` contains parse warnings and errors as `Nokogiri::XML::SyntaxError` objects even when `doc` is non-nil (partial parse). Always check `doc.errors.any?` and treat non-empty errors as invalid input.
- `doc.at_xpath` returns `nil` if no node matches, making the safe-navigation operator `&.text` idiomatic for nullable results.
- REXML is bundled with Ruby but has no network-fetch protection and is vulnerable to billion-laughs DoS without manual entity expansion limits. Avoid it for untrusted input; use Nokogiri exclusively.
- Rails' `Hash.from_xml` and `Rack::Utils.parse_nested_query` historically used REXML and were vulnerable to XXE. If you parse XML from request bodies in Rails, use Nokogiri directly rather than the Rails helpers.

## Tests to write
- Happy path: valid XML bytes parse into a `Nokogiri::XML::Document` with no errors.
- Network entity blocked: a document with `<!ENTITY xxe SYSTEM "http://localhost/evil">` does not trigger an HTTP request (use a mock or assert `doc.errors.any?`).
- Over-size body: a string of `MAX_BODY_BYTES + 1` bytes raises `ArgumentError`.
- Malformed XML: a truncated document raises `ArgumentError` listing at least one parse error.
- XPath injection: user_id `'] | //*[' ` fails the `VALID_ID` regex and raises `ArgumentError`.
- XPath happy path: a known user_id returns the correct name string.
- NONET option assertion: verify `SAFE_OPTIONS` includes `Nokogiri::XML::ParseOptions::NONET` in a unit test so the constant is never accidentally changed.
