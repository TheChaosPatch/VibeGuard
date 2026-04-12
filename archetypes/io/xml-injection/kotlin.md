---
schema_version: 1
archetype: io/xml-injection
language: kotlin
principles_file: _principles.md
libraries:
  preferred: javax.xml.parsers.DocumentBuilderFactory (hardened, same as Java)
  acceptable:
    - kotlinx.serialization with XML format (kotlinx-serialization-xml, hardened parser)
  avoid:
    - name: DocumentBuilderFactory with default settings
      reason: Identical risk to Java — DTD and external entity processing are on by default.
    - name: Unmarshalling XML into Any or Map without schema
      reason: Type-unsafe; validation becomes impossible and extra fields may carry injection payloads.
minimum_versions:
  kotlin: "2.0"
  java: "21"
---

# XML Injection Defense — Kotlin

## Library choice
Kotlin runs on the JVM and uses the same `javax.xml` APIs as Java. The hardening approach is identical: configure `DocumentBuilderFactory` once with all unsafe features disabled and keep the factory in a companion object. Kotlin's null safety and `use` extension make resource management cleaner. For XPath parameterization, use `XPathVariableResolver` as in the Java archetype. If you use `kotlinx-serialization-xml`, configure the underlying SAX parser before creating the format instance.

## Reference implementation
```kotlin
import org.w3c.dom.Document
import java.io.ByteArrayInputStream
import java.io.InputStream
import java.util.regex.Pattern
import javax.xml.parsers.DocumentBuilderFactory
import javax.xml.xpath.XPathConstants
import javax.xml.xpath.XPathFactory

object SafeXmlParser {
    private const val MAX_BODY_BYTES = 512 * 1024L
    private val VALID_ID = Pattern.compile("^[A-Za-z0-9_-]{1,64}$")

    private val safeFactory: DocumentBuilderFactory = DocumentBuilderFactory.newInstance().apply {
        setFeature("http://apache.org/xml/features/disallow-doctype-decl", true)
        setFeature("http://xml.org/sax/features/external-general-entities", false)
        setFeature("http://xml.org/sax/features/external-parameter-entities", false)
        setFeature("http://apache.org/xml/features/nonvalidating/load-external-dtd", false)
        isXIncludeAware = false
        isExpandEntityReferences = false
    }

    fun parse(body: InputStream): Document {
        val buf = body.readNBytes((MAX_BODY_BYTES + 1).toInt())
        require(buf.size <= MAX_BODY_BYTES) { "XML body exceeds $MAX_BODY_BYTES bytes" }
        // DocumentBuilder is not thread-safe; create one per call from the safe factory.
        return safeFactory.newDocumentBuilder().parse(ByteArrayInputStream(buf))
    }

    fun queryUsername(doc: Document, userId: String): String? {
        require(VALID_ID.matcher(userId).matches()) { "Invalid userId format" }
        val xpath = XPathFactory.newInstance().newXPath().apply {
            setXPathVariableResolver { v -> if (v.localPart == "uid") userId else null }
        }
        return (xpath.evaluate("//user[@id = \$uid]/name", doc, XPathConstants.STRING) as String)
            .takeIf { it.isNotEmpty() }
    }
}
```

## Language-specific gotchas
- The Kotlin `apply` block on `DocumentBuilderFactory` is a clean way to configure the factory inline, but any unchecked exception thrown by `setFeature` will propagate at initialization time and crash the application. Wrap in `runCatching` or a `try` block if you want a meaningful error message at startup rather than an `ExceptionInInitializerError`.
- `DocumentBuilder.newDocumentBuilder()` is not thread-safe. The factory is, so create a new builder per parse call in multi-threaded environments. Alternatively, use a `ThreadLocal<DocumentBuilder>` pool.
- `body.readNBytes(MAX_BODY_BYTES + 1)` caps the read at `MAX_BODY_BYTES + 1` bytes. The `+1` ensures you can detect an over-size body (the buffer will contain more bytes than the limit) without reading the entire stream.
- Kotlin's string template `"//user[@id = \$uid]/name"` uses a backslash to escape the `$` so it becomes an XPath variable reference (`$uid`) rather than a Kotlin string interpolation.
- If you use Spring Boot with Jackson's XML module (`jackson-dataformat-xml`), the underlying `XmlMapper` uses its own `XMLInputFactory`. Configure it: `XmlMapper().apply { xmlInputFactory.setProperty(XMLInputFactory.IS_SUPPORTING_EXTERNAL_ENTITIES, false) }`.

## Tests to write
- Happy path: a valid XML InputStream parses into a `Document` with the expected root element.
- DTD forbidden: `<!DOCTYPE>` in the input throws `SAXParseException`.
- External entity reference is blocked before any I/O.
- Over-size body: a stream of `MAX_BODY_BYTES + 1` bytes throws `IllegalArgumentException`.
- XPath injection: userId `'] | //secret['` is rejected by the allowlist `require`.
- XPath happy path: a known userId returns the correct name string.
- Thread safety: parse called concurrently from 10 threads produces correct results and no exceptions.
