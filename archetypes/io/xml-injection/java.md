---
schema_version: 1
archetype: io/xml-injection
language: java
principles_file: _principles.md
libraries:
  preferred: javax.xml.parsers.DocumentBuilderFactory (hardened)
  acceptable:
    - StAX (javax.xml.stream.XMLInputFactory, hardened)
    - JAXB with hardened underlying parser
  avoid:
    - name: DocumentBuilderFactory with default settings
      reason: DTD processing and external entity resolution are enabled by default; the factory must be explicitly hardened before use.
    - name: SAXParserFactory with default settings
      reason: Same default-unsafe behavior as DocumentBuilderFactory.
    - name: XMLInputFactory with default settings
      reason: StAX factories may support external entities depending on the JDK version; hardening flags must be set explicitly.
minimum_versions:
  java: "21"
---

# XML Injection Defense — Java

## Library choice
Java's built-in XML stack (`DocumentBuilderFactory`, `SAXParserFactory`, `XMLInputFactory`) is safe when hardened but dangerous out of the box. The OWASP XXE prevention cheat sheet documents the exact feature flags for each factory. Apply them once, wrap the factory in an application-scoped singleton, and never construct a naked factory at the call site. For XPath, use `javax.xml.xpath.XPath` with `XPathVariableResolver` for parameterization; never concatenate user data into the expression string.

## Reference implementation
```java
import javax.xml.parsers.*;
import javax.xml.xpath.*;
import org.w3c.dom.Document;
import java.io.*;
import java.util.regex.Pattern;

public final class SafeXmlParser {
    private static final long MAX_BODY_BYTES = 512 * 1024L;
    private static final Pattern VALID_ID = Pattern.compile("^[A-Za-z0-9_-]{1,64}$");
    private static final DocumentBuilderFactory SAFE_FACTORY = buildSafeFactory();

    private static DocumentBuilderFactory buildSafeFactory() {
        DocumentBuilderFactory f = DocumentBuilderFactory.newInstance();
        try {
            f.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true);
            f.setFeature("http://xml.org/sax/features/external-general-entities", false);
            f.setFeature("http://xml.org/sax/features/external-parameter-entities", false);
            f.setFeature("http://apache.org/xml/features/nonvalidating/load-external-dtd", false);
            f.setXIncludeAware(false);
            f.setExpandEntityReferences(false);
        } catch (ParserConfigurationException e) { throw new ExceptionInInitializerError(e); }
        return f;
    }

    public static Document parse(InputStream body) throws Exception {
        byte[] buf = body.readNBytes((int) MAX_BODY_BYTES + 1);
        if (buf.length > MAX_BODY_BYTES)
            throw new IllegalArgumentException("XML body exceeds " + MAX_BODY_BYTES + " bytes");
        return SAFE_FACTORY.newDocumentBuilder().parse(new ByteArrayInputStream(buf));
    }

    public static String queryUsername(Document doc, String userId) throws XPathExpressionException {
        if (!VALID_ID.matcher(userId).matches())
            throw new IllegalArgumentException("Invalid userId format");
        XPath xpath = XPathFactory.newInstance().newXPath();
        xpath.setXPathVariableResolver(v -> "uid".equals(v.getLocalPart()) ? userId : null);
        return (String) xpath.evaluate("//user[@id = $uid]/name", doc, XPathConstants.STRING);
    }
}
```

## Language-specific gotchas
- `DocumentBuilderFactory` defaults differ by JDK vendor and version. The FEATURE_EXTERNAL_GENERAL_ENTITIES flag is not disabled in any standard JDK distribution by default. Always set all four feature flags — do not assume any one JDK version is safe by default.
- `disallow-doctype-decl` is an Apache Xerces-specific feature (the parser bundled with the JDK). It throws a `SAXParseException` at the first `<!DOCTYPE` token, stopping parsing before any entity processing. Prefer this over trying to disable individual entity features.
- `DocumentBuilder` is not thread-safe; obtain a new instance from the (thread-safe) factory per request, or use a pool.
- JAXB's `JAXBContext.createUnmarshaller()` uses an underlying `SAXParser` that inherits the same defaults. Set the SAX parser on the unmarshaller: `unmarshaller.setProperty("com.sun.xml.bind.v2.runtime.unmarshaller.SAXConnector", hardenedSaxParser)`.
- `XPathVariableResolver` is the correct parameterization hook. The resolved value is typed and treated as a data atom by the XPath evaluator — it cannot alter the expression structure.
- Spring's `RestTemplate` and `WebClient` deserve their own XML hardening if you consume XML responses from third parties; the auto-configured `Jackson2ObjectMapperBuilder` does not cover the XML stack.

## Tests to write
- Happy path: a valid XML document parses and the root element has the expected tag name.
- DTD forbidden: a document with `<!DOCTYPE>` throws `SAXParseException` containing "DOCTYPE".
- External entity: a SYSTEM entity reference is blocked before any file or network access.
- Over-size body: a stream exceeding `MAX_BODY_BYTES` throws before the parser is invoked.
- XPath injection: a userId containing `'] | //secret[' ` is rejected by the allowlist check.
- XPath happy path: a known userId resolves to the correct name string via `XPathVariableResolver`.
- Factory singleton: assert the static factory has `disallow-doctype-decl` set to `true`.
