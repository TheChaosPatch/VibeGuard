---
schema_version: 1
archetype: io/xml-injection
title: XML Injection Defense
summary: Preventing XXE, XPath injection, and DTD-based attacks when parsing or querying XML from untrusted sources.
applies_to: [csharp, python, go, java, kotlin, php, ruby]
status: stable
author: ehabhussein
reviewed_by: [ehabhussein]
stable_since: "2026-04-12"
keywords:
  - xxe
  - xml
  - xpath
  - dtd
  - entity
  - injection
  - parser
  - external-entity
  - xinclude
related_archetypes:
  - io/input-validation
  - io/unsafe-deserialization
references:
  owasp_asvs: V5.3
  owasp_cheatsheet: XML External Entity Prevention Cheat Sheet
  cwe: "611"
---

# XML Injection Defense — Principles

## When this applies
Any time your code parses an XML document whose bytes or structure can be influenced by an attacker: HTTP request bodies sent as `application/xml` or `text/xml`, file uploads processed server-side (DOCX, SVG, XLSX, RSS), XML stored in a database or message queue that another service populated from user input, SOAP requests, and RSS/Atom feeds fetched from user-supplied URLs. XPath injection is a second surface: any time user input is concatenated into an XPath expression and evaluated against an XML document. If the XML parser or XPath evaluator sees attacker-controlled bytes, this archetype applies.

## Architectural placement
XML parsing lives in a single, encapsulated layer — a named parser class or function — that owns all parser configuration. No parser is constructed inline at the call site. The parser is configured once at startup with a frozen, safe options object (no DTD, no external entities, no XInclude, no network access) and every parse request goes through it. XPath queries are parameterized or built from a closed set of string constants; user input is never concatenated into an XPath string. The output of the parser is always a typed domain object, not a raw `XmlDocument` or `Element` tree handed to business logic.

## Principles
1. **Disable DTD processing entirely.** DTDs are the root of XXE: they define entities, including external entities that instruct the parser to fetch a URL or read a local file. An XML parser that accepts DTDs from attacker-controlled documents is a server-side request forgery and file-disclosure primitive. Disable DTD processing unconditionally; almost no legitimate modern XML payload requires DTDs.
2. **Disable external entity resolution.** Even if you leave DTD processing on for a legacy reason, explicitly disable the resolution of external general entities (`FEATURE_EXTERNAL_GENERAL_ENTITIES`) and external parameter entities (`FEATURE_EXTERNAL_PARAMETER_ENTITIES`). Belt and suspenders.
3. **Disable XInclude.** XInclude is a separate XML expansion mechanism that can also pull in local files or remote URLs. It is off by default in most parsers but must be explicitly disabled if the parser supports it.
4. **Never concatenate user input into XPath expressions.** `//user[name='` + username + `']` is XPath injection. Use parameterized XPath (where the API supports it) or extract the user-controlled value, validate it against a strict allowlist, and substitute it into a hard-coded template using the API's variable binding mechanism.
5. **Prefer pull parsers (SAX/StAX) or purpose-built readers for large documents.** DOM parsers build an in-memory tree of the entire document. A 500 MB XML upload is a denial-of-service attack if you DOM-parse it. SAX/StAX parsers emit events without materializing the whole tree, and let you enforce per-element size and depth limits as you go.
6. **Enforce document size and depth limits before parsing.** Reject documents above a configured byte limit at the transport layer. Set a max element nesting depth inside the parser. A "billion laughs" entity expansion attack is defeated by both: DTD off eliminates the entity expansion vector, and size caps are defense-in-depth.
7. **Reject or strip unexpected namespaces.** XML namespace declarations can smuggle content past schema validators that inspect only the local name. Validate the namespace URI as part of element acceptance, not just the local name.

## Anti-patterns
- `XmlDocument.Load(stream)` in .NET with default `XmlResolver` — fetches external entities over the network by design in older .NET versions.
- `DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(stream)` in Java with no feature flags set — the factory defaults are unsafe; DTD processing is on.
- `lxml.etree.parse(f)` in Python with default settings — allows network entity resolution; use `defusedxml` or configure an explicit `XMLParser` with `resolve_entities=False`.
- `SimpleXML` in PHP with no `LIBXML_NONET | LIBXML_NOENT` flags — PHP's SimpleXML will process DTDs and resolve external entities using libxml2's defaults.
- `REXML` in Ruby parsing untrusted input — vulnerable to entity expansion DoS without explicit limits.
- Building XPath strings with f-strings or `+` concatenation where any segment is user-supplied.
- Using `XPathNavigator.Evaluate(string)` where the string argument includes unsanitized user input.
- Relying on a WAF to strip DTDs from requests. WAFs can be bypassed with encoding or chunked transfer; disable DTDs in the parser.

## References
- OWASP ASVS V5.3 -- Output Encoding and Injection Prevention
- OWASP XML External Entity Prevention Cheat Sheet
- CWE-611 -- Improper Restriction of XML External Entity Reference
- CWE-643 -- Improper Neutralization of Data within XPath Expressions
