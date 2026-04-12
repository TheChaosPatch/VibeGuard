---
schema_version: 1
archetype: io/xml-injection
language: python
principles_file: _principles.md
libraries:
  preferred: defusedxml
  acceptable:
    - lxml with explicit XMLParser(resolve_entities=False, no_network=True)
  avoid:
    - name: xml.etree.ElementTree (stdlib, untrusted input)
      reason: Does not protect against entity expansion DoS (billion laughs) in Python < 3.8; no DTD controls in any version.
    - name: xml.sax / xml.dom.minidom (stdlib, untrusted input)
      reason: Both process external entities by default; no safe configuration path without defusedxml.
    - name: lxml with default XMLParser
      reason: Resolves external entities and fetches network URLs unless explicitly configured otherwise.
minimum_versions:
  python: "3.11"
---

# XML Injection Defense — Python

## Library choice
`defusedxml` is a drop-in replacement for the stdlib XML modules that disables DTD processing, external entity expansion, and XInclude on all backends. Use `defusedxml.ElementTree.parse()` or `defusedxml.minidom.parseString()` wherever you would use the stdlib equivalents. For high-performance or schema-validated workloads, `lxml` with an explicit `XMLParser(resolve_entities=False, no_network=True, load_dtd=False)` is acceptable. XPath with `lxml` must use `lxml.etree.XPath` with parameterized variable substitution, never f-string concatenation.

## Reference implementation
```python
from __future__ import annotations

import defusedxml.ElementTree as ET
from lxml import etree
from typing import Final

MAX_BODY_BYTES: Final[int] = 512 * 1024

# defusedxml path -- safest, simplest.
def parse_xml_safe(body: bytes) -> ET.Element:
    if len(body) > MAX_BODY_BYTES:
        raise ValueError(f"XML body {len(body)} B exceeds {MAX_BODY_BYTES} B")
    # defusedxml raises DefusedXmlException for DTDs, entities, XInclude.
    return ET.fromstring(body)


# lxml path -- for XPath queries with variable binding.
_LXML_PARSER = etree.XMLParser(
    resolve_entities=False,
    no_network=True,
    load_dtd=False,
    forbid_dtd=True,
    forbid_entities=True,
    forbid_external=True,
    huge_tree=False,
)

_XPATH_USERNAME = etree.XPath("//user[@id = $uid]/name/text()")

def query_username(body: bytes, user_id: str) -> str | None:
    if not user_id.replace("-", "").replace("_", "").isalnum() or len(user_id) > 64:
        raise ValueError("Invalid user_id format")
    if len(body) > MAX_BODY_BYTES:
        raise ValueError(f"XML body {len(body)} B exceeds {MAX_BODY_BYTES} B")
    tree = etree.fromstring(body, parser=_LXML_PARSER)
    # Variable binding -- user_id is passed as a typed XPath variable, not concatenated.
    results = _XPATH_USERNAME(tree, uid=user_id)
    return results[0] if results else None
```

## Language-specific gotchas
- `xml.etree.ElementTree` in Python 3.8+ defends against billion-laughs but not against external entity network fetch. `defusedxml` plugs both gaps and requires no configuration.
- `lxml`'s `XMLParser` has several overlapping flags: `resolve_entities=False` stops entity expansion, `no_network=True` blocks network fetches, `forbid_dtd=True` rejects documents with DTD declarations. Set all three — they guard different attack surfaces.
- `lxml.etree.XPath` accepts keyword arguments as XPath variables (`$uid` in the expression, `uid=user_id` in the call). This is the correct parameterization mechanism; it was introduced in lxml 2.2 and is stable.
- Never pass user input through `etree.XPath(f"//user[@id='{user_id}']")` — XPath injection via quote characters bypasses element filters the same way SQL injection bypasses WHERE clauses.
- `defusedxml` raises `defusedxml.DTDForbidden`, `defusedxml.EntitiesForbidden`, or `defusedxml.ExternalReferenceForbidden` — all are subclasses of `defusedxml.DefusedXmlException`. Catch the base class in your error handler and map it to a 400 response.
- `huge_tree=False` (the lxml default) caps DOM tree size. Do not set `huge_tree=True` for untrusted input — it disables the internal depth and node count limits.
- Never use `ElementTree.iterparse` on untrusted input without defusedxml — it shares the same vulnerable backend as `parse()`.

## Tests to write
- Happy path: valid XML bytes parse into an `Element` with expected tag and text.
- DTD forbidden: a document with `<!DOCTYPE>` raises `defusedxml.DTDForbidden`.
- External entity: `<!ENTITY xxe SYSTEM "file:///etc/passwd">` raises before any I/O.
- Billion-laughs: a deeply nested entity payload is rejected (DTD forbidden before expansion).
- Over-size body: 600 KB of XML raises `ValueError` before parsing.
- XPath injection: a user_id containing `' or '1'='1` is rejected by the allowlist check.
- XPath happy path: a known user_id returns the correct name text.
- lxml parser config regression: assert `_LXML_PARSER.resolve_entities` is `False` and `forbid_dtd` is `True`.
