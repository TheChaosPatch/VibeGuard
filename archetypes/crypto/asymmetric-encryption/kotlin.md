---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: kotlin
principles_file: _principles.md
libraries:
  preferred: java.security (JCA/JCE via Kotlin interop)
  acceptable:
    - Bouncy Castle (org.bouncycastle:bcprov-jdk18on)
    - Nimbus JOSE+JWT (com.nimbusds:nimbus-jose-jwt)
    - AWS SDK v2 KMS (software.amazon.awssdk:kms)
  avoid:
    - name: Cipher.getInstance("RSA") without padding
      reason: Defaults to PKCS1v1.5 on SunJCE — Bleichenbacher-vulnerable.
    - name: kotlinx-serialization for key material
      reason: Serializing a private key through a general-purpose serializer risks accidental logging or persistence.
minimum_versions:
  kotlin: "2.0"
  jvm: "21"
---

# Asymmetric Encryption and Signing — Kotlin

## Library choice
Kotlin runs on the JVM and interoperates with JCA/JCE without friction. The guidance from the Java archetype applies directly: use `java.security.Signature` with `"Ed25519"` or `"SHA256withECDSA"`, and `javax.crypto.Cipher` with `"RSA/ECB/OAEPWithSHA-256AndMGF1Padding"`. Kotlin extension functions let you wrap the verbose JCA API into concise, non-null-safe wrappers that enforce algorithm choices at the call site. Nimbus JOSE+JWT is the right JWT library. On Android, use the Android Keystore system (`KeyPairGenerator` with `"AndroidKeyStore"` provider) — private keys are hardware-backed and never exported.

## Reference implementation
```kotlin
import java.security.*
import javax.crypto.Cipher

object AsymmetricCrypto {
    fun generateEd25519(): KeyPair =
        KeyPairGenerator.getInstance("Ed25519").generateKeyPair()

    fun sign(data: ByteArray, privateKey: PrivateKey): ByteArray =
        Signature.getInstance("Ed25519").run {
            initSign(privateKey)
            update(data)
            sign()
        }

    fun verify(data: ByteArray, signature: ByteArray, publicKey: PublicKey): Boolean =
        Signature.getInstance("Ed25519").run {
            initVerify(publicKey)
            update(data)
            verify(signature)
        }

    fun encryptDek(dek: ByteArray, pub: PublicKey): ByteArray =
        Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding").run {
            init(Cipher.ENCRYPT_MODE, pub)
            doFinal(dek)
        }

    fun decryptDek(wrapped: ByteArray, priv: PrivateKey): ByteArray =
        Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding").run {
            init(Cipher.DECRYPT_MODE, priv)
            doFinal(wrapped)
        }
}
```

## Language-specific gotchas
- `Signature` is not thread-safe on the JVM. The `object` singleton above will race if multiple coroutines call `sign` or `verify` simultaneously. Either synchronize, use `ThreadLocal`, or create a new `Signature` instance per call. Creating a new instance is cheap.
- In Kotlin, `ByteArray` is `byte[]` on the JVM. Do not use `Array<Byte>` for cryptographic buffers — it boxes each byte as a `Byte` object and prevents constant-time memory operations.
- Android Keystore: use `KeyPairGenerator.getInstance("EC", "AndroidKeyStore")` with a `KeyGenParameterSpec` that specifies `PURPOSE_SIGN` and `DIGEST_SHA256`. The private key object returned cannot be exported — `getEncoded()` returns null. This is the intended behavior; treat it as a feature.
- Coroutine safety: wrap JCA operations in `withContext(Dispatchers.Default)` or `Dispatchers.IO` if they are called from a coroutine context — cryptographic operations can block for several milliseconds (key generation especially).
- For Nimbus JWT: `val verifier = ECDSAVerifier(ecPublicKey)`. Call `signedJWT.verify(verifier)` before accessing `signedJWT.jwtClaimsSet`. Kotlin's null safety does not protect you from calling `jwtClaimsSet` on an unverified JWT — the claim set is accessible regardless.
- `run { ... }` blocks on a `Cipher` or `Signature` instance look clean but mask that the instance is mutable. Prefer named variables when operations are multi-step.

## Tests to write
- Round-trip Ed25519: sign a `ByteArray`, verify with the corresponding public key, assert true.
- Wrong-key rejection: sign with key A, verify with key B's public key, assert false.
- Tampered payload: flip a byte in the signature, verify, assert false.
- RSA OAEP round-trip: encrypt a 32-byte DEK, decrypt, assert arrays equal.
- Concurrency safety: launch 100 coroutines calling `sign` simultaneously, assert no `IllegalStateException` (validates per-call instantiation).
- Android Keystore (if targeting Android): assert that `keyPair.private.encoded` is null (key is hardware-backed).
