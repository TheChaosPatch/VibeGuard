---
schema_version: 1
archetype: io/xml-injection
language: go
principles_file: _principles.md
libraries:
  preferred: encoding/xml
  acceptable: []
  avoid:
    - name: encoding/xml with unrestricted io.Reader and no size cap
      reason: The stdlib decoder has no built-in document size or depth limit; an unbounded reader allows DoS via enormous or deeply nested documents.
    - name: Third-party XML parsers that enable XInclude by default
      reason: XInclude expansion can pull in local files or remote URLs the same way external entities do.
minimum_versions:
  go: "1.22"
---

# XML Injection Defense — Go

## Library choice
Go's `encoding/xml` does not process DTDs or resolve external entities — it simply ignores DTD declarations and passes entity references through as literal text. This makes it safe-by-default for XXE. The remaining risks are denial of service (no size or depth cap) and XPath injection (if you feed user input into an xpath expression with a third-party library). Wrap `encoding/xml.NewDecoder` with an `io.LimitReader` to enforce size limits. For XPath, use `github.com/antchfx/xpath` or `github.com/antchfx/xmlquery` with parameterized expressions.

## Reference implementation
```go
package xmlparser

import (
	"encoding/xml"
	"fmt"
	"io"
	"regexp"
	"strings"

	"github.com/antchfx/xmlquery"
)

const maxBodyBytes = 512 * 1024
var validUserID = regexp.MustCompile(`^[A-Za-z0-9_-]{1,64}$`)

type User struct {
	ID   string `xml:"id,attr"`
	Name string `xml:"name"`
}
type Users struct{ Users []User `xml:"user"` }

func ParseUsers(body io.Reader) (*Users, error) {
	buf, err := io.ReadAll(io.LimitReader(body, maxBodyBytes+1))
	if err != nil { return nil, fmt.Errorf("read body: %w", err) }
	if len(buf) > maxBodyBytes { return nil, fmt.Errorf("XML body exceeds %d bytes", maxBodyBytes) }
	var result Users
	if err := xml.Unmarshal(buf, &result); err != nil { return nil, fmt.Errorf("xml decode: %w", err) }
	return &result, nil
}

func QueryUsername(body io.Reader, userID string) (string, error) {
	if !validUserID.MatchString(userID) { return "", fmt.Errorf("invalid userID format") }
	doc, err := xmlquery.Parse(io.LimitReader(body, maxBodyBytes+1))
	if err != nil { return "", fmt.Errorf("xmlquery parse: %w", err) }
	expr := fmt.Sprintf("//user[@id='%s']/name", userID)
	node := xmlquery.FindOne(doc, expr)
	if node == nil { return "", nil }
	return strings.TrimSpace(node.InnerText()), nil
}
```

## Language-specific gotchas
- `encoding/xml` silently ignores `<!DOCTYPE>` declarations rather than processing them, which means it is safe against XXE by design. You do not need to set any flags — but document this assumption with a comment so future maintainers don't add a "smarter" parser.
- `encoding/xml` has no built-in size or depth limit. Always wrap the `io.Reader` with `io.LimitReader` before passing it to the decoder. The limit check after `ReadAll` confirms the document did not exceed the cap.
- `xmlquery` (from `github.com/antchfx/xmlquery`) uses `encoding/xml` internally, so it inherits the XXE safety. However, it does not support true XPath variable binding. The correct mitigation is to validate the user-supplied fragment against a strict allowlist regex before embedding it in the expression string — as shown above.
- The `encoding/xml.Decoder` can be used in streaming mode (`Token()` loop) for large documents. In streaming mode you still need `io.LimitReader` and should track nesting depth manually to guard against deeply nested structures.
- XML namespace prefixes in Go are resolved at decode time; the `xml.Name` struct carries both the local name and namespace URI. Validate the namespace URI when element identity depends on it, not just the local name.
- `encoding/xml` unmarshals into `interface{}` if the target type is `interface{}` — you lose all structural guarantees. Always provide a concrete typed struct as the decode target.

## Tests to write
- Happy path: a valid XML body with two users decodes into a `Users` struct with expected values.
- DTD ignored: a document with `<!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>` parses without any file I/O (verify with a test that checks no file was opened).
- Over-size body: a body of `maxBodyBytes+1` bytes returns an error before decoding.
- XPath injection: a userID containing `' or '1'='1` is rejected by the allowlist regex.
- XPath happy path: a known userID returns the correct name.
- Namespace mismatch: an element with an unexpected namespace URI is rejected or ignored by the typed decode target.
