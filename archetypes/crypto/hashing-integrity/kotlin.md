---
schema_version: 1
archetype: crypto/hashing-integrity
language: kotlin
principles_file: _principles.md
libraries:
  preferred: javax.crypto.Mac + java.security.MessageDigest (JCA via Kotlin interop)
  acceptable:
    - Bouncy Castle (org.bouncycastle:bcprov-jdk18on) for SHA-3 / KMAC
    - kotlinx.serialization (for structured payload serialisation before hashing, not for hashing itself)
  avoid:
    - name: ByteArray.contentEquals for MAC comparison
      reason: Not constant-time; use MessageDigest.isEqual.
    - name: MessageDigest.getInstance("MD5") or "SHA-1"
      reason: Broken algorithms; do not use for integrity.
minimum_versions:
  kotlin: "2.0"
  jvm: "21"
---

# Hashing and Data Integrity — Kotlin

## Library choice
Kotlin on the JVM inherits Java's JCA stack. `javax.crypto.Mac` and `java.security.MessageDigest` require no additional dependencies and are the correct default. Kotlin's extension function model lets you write clean wrappers that enforce algorithm choices and key-length validation at the call site. `MessageDigest.isEqual` is the constant-time comparator — identical to the Java guidance. On Android, these APIs are available in the `javax.crypto` package without modification; the Android Keystore system can also back HMAC keys so the raw key material never leaves secure hardware.

## Reference implementation
```kotlin
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

private const val ALGORITHM = "HmacSHA256"
private val WEBHOOK_PREFIX = "webhook-v1:".toByteArray(Charsets.UTF_8)

fun computeHmac(key: ByteArray, data: ByteArray): ByteArray {
    require(key.size >= 32) { "HMAC key must be at least 32 bytes" }
    return Mac.getInstance(ALGORITHM).run {
        init(SecretKeySpec(key, ALGORITHM))
        doFinal(data)
    }
}

fun verifyHmac(key: ByteArray, data: ByteArray, expected: ByteArray): Boolean {
    val actual = computeHmac(key, data)
    return MessageDigest.isEqual(actual, expected)  // constant-time
}

fun sha256Digest(data: ByteArray): ByteArray =
    MessageDigest.getInstance("SHA-256").digest(data)

fun webhookTag(key: ByteArray, payload: ByteArray): ByteArray =
    computeHmac(key, WEBHOOK_PREFIX + payload)
```

## Language-specific gotchas
- `Mac` is not thread-safe on the JVM. The `run { ... }` block above creates a new `Mac` instance per call — this is the correct pattern. Do not hoist the `Mac` instance to a property on a singleton `object` and share it across coroutines.
- `ByteArray.contentEquals(other)` performs element-by-element comparison and is documented to short-circuit. Never use it for HMAC comparison. `MessageDigest.isEqual` is the JDK-provided constant-time comparator.
- Kotlin's `+` operator on `ByteArray` creates a new array, copying both arrays — `WEBHOOK_PREFIX + payload` is safe and clear. For high-throughput paths, pre-size the array and use `System.arraycopy` to avoid the intermediate allocation.
- `require(key.size >= 32)` throws `IllegalArgumentException` — the correct exception type for precondition violations. Callers should catch this in tests to verify key-length enforcement.
- `run { init(...); doFinal(data) }` calls `doFinal` which both finalises the computation and resets the `Mac`. If you need to hash multiple chunks, call `update(chunk)` in a loop, then `doFinal()` (no-argument form) once.
- On Android, use `KeyGenerator.getInstance("HmacSHA256", "AndroidKeyStore")` with a `KeyGenParameterSpec` to create a hardware-backed HMAC key. The resulting `SecretKey` cannot be exported; call `mac.init(androidKey)` directly — no raw bytes leave secure hardware.

## Tests to write
- Round-trip: compute HMAC, verify with same key and data, assert true.
- Wrong-key rejection: compute with key A, verify with key B, assert false.
- Tampered data: compute, flip one byte, verify, assert false.
- Key length guard: assert that `computeHmac(ByteArray(16), data)` throws `IllegalArgumentException`.
- Coroutine safety: launch 100 coroutines calling `computeHmac`, assert no exception (validates per-call `Mac` instantiation).
- Purpose binding: assert `webhookTag(k, data)` differs from `computeHmac(k, data)`.
