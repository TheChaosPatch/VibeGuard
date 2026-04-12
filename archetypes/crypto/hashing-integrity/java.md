---
schema_version: 1
archetype: crypto/hashing-integrity
language: java
principles_file: _principles.md
libraries:
  preferred: javax.crypto.Mac + java.security.MessageDigest (JCA, stdlib)
  acceptable:
    - Bouncy Castle (org.bouncycastle:bcprov-jdk18on) for SHA-3 / KMAC
    - Spring Security CryptoUtils (if already using Spring)
  avoid:
    - name: MessageDigest.getInstance("MD5")
      reason: Collision attacks are practical; broken for integrity.
    - name: MessageDigest.getInstance("SHA-1")
      reason: SHAttered collision demonstrated; deprecated.
    - name: Arrays.equals or == for MAC comparison
      reason: Not constant-time; use MessageDigest.isEqual or a constant-time loop.
minimum_versions:
  java: "21"
---

# Hashing and Data Integrity — Java

## Library choice
Java's JCA provides `javax.crypto.Mac` for HMAC and `java.security.MessageDigest` for keyless digests, both backed by the platform's crypto provider with no external dependencies. The constant-time comparator is `MessageDigest.isEqual(byte[], byte[])` — this is part of the JDK and is the correct tool. Bouncy Castle adds SHA-3 family (SHA3-256, KMAC) if you need them. Avoid `Arrays.equals` and `Arrays.compare` for MAC comparison — neither is documented as constant-time.

## Reference implementation
```java
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.security.MessageDigest;
import java.nio.charset.StandardCharsets;

public final class IntegrityService {
    private static final String ALGORITHM = "HmacSHA256";
    private static final byte[] WEBHOOK_PREFIX =
        "webhook-v1:".getBytes(StandardCharsets.UTF_8);

    // HMAC-SHA256: key must be at least 32 random bytes
    public static byte[] computeHmac(byte[] key, byte[] data)
            throws Exception {
        Mac mac = Mac.getInstance(ALGORITHM);
        mac.init(new SecretKeySpec(key, ALGORITHM));
        return mac.doFinal(data);
    }

    // Constant-time verification
    public static boolean verifyHmac(byte[] key, byte[] data, byte[] expected)
            throws Exception {
        byte[] actual = computeHmac(key, data);
        return MessageDigest.isEqual(actual, expected);
    }

    // Keyless digest for checksums / content-addressed storage
    public static byte[] sha256Digest(byte[] data) throws Exception {
        return MessageDigest.getInstance("SHA-256").digest(data);
    }

    // Bind purpose string to prevent cross-context MAC reuse
    public static byte[] webhookTag(byte[] key, byte[] payload) throws Exception {
        byte[] prefixed = new byte[WEBHOOK_PREFIX.length + payload.length];
        System.arraycopy(WEBHOOK_PREFIX, 0, prefixed, 0, WEBHOOK_PREFIX.length);
        System.arraycopy(payload, 0, prefixed, WEBHOOK_PREFIX.length, payload.length);
        return computeHmac(key, prefixed);
    }
}
```

## Language-specific gotchas
- `Mac` is not thread-safe. Create a new instance per operation, or use a `ThreadLocal<Mac>` pattern. `Mac.getInstance` is cheap — prefer per-call instantiation over sharing.
- `MessageDigest.isEqual(a, b)` is constant-time even when lengths differ (it returns false immediately for length mismatch). This is different from `Arrays.equals`, which is not constant-time and is length-aware only in the sense that it checks length first.
- `new SecretKeySpec(key, "HmacSHA256")` — the algorithm parameter must exactly match the `Mac` algorithm name. Mismatched strings (e.g., `"SHA256"` vs `"HmacSHA256"`) will throw `InvalidKeyException` at `init` time.
- `Mac.getInstance("HmacSHA256")` vs `Mac.getInstance("HMACSHA256")` — algorithm names are case-insensitive on the SunJCE provider, but case-sensitive on some third-party providers (Bouncy Castle). Use the canonical JCA name `"HmacSHA256"` consistently.
- For streaming large inputs, call `mac.update(chunk)` in a loop, then `mac.doFinal()` without arguments. Do not call `mac.doFinal(data)` on each chunk — `doFinal` finalises the computation and resets the `Mac` object; further updates after `doFinal` without `reset` will mix state.
- Spring Security's `HmacUtils` class is a convenience wrapper that is acceptable if Spring is already in the classpath — it delegates to JCA and uses `MessageDigest.isEqual`.

## Tests to write
- Round-trip: compute HMAC, verify with same key and data, assert true.
- Wrong-key rejection: compute with key A, verify with key B, assert false.
- Tampered data: compute, change one byte, verify, assert false.
- Thread safety: run 100 concurrent `computeHmac` calls (via `ExecutorService`), assert no `IllegalStateException`.
- Purpose binding: assert `webhookTag(k, data) != computeHmac(k, data)` (different byte arrays).
- Key length validation: add a guard in your factory that throws `IllegalArgumentException` for keys shorter than 32 bytes.
