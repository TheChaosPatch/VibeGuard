---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: java
principles_file: _principles.md
libraries:
  preferred: java.security (JCA/JCE, stdlib)
  acceptable:
    - Bouncy Castle (org.bouncycastle:bcprov-jdk18on)
    - Nimbus JOSE+JWT (com.nimbusds:nimbus-jose-jwt)
    - AWS SDK v2 KMS (software.amazon.awssdk:kms)
  avoid:
    - name: Cipher.getInstance("RSA") without padding spec
      reason: Defaults to RSA/ECB/PKCS1Padding on most JVMs — vulnerable to Bleichenbacher.
    - name: Custom X9.62 point encoding
      reason: Error-prone; use KeyFactory.generatePublic with an ECPoint from the provider.
minimum_versions:
  java: "21"
---

# Asymmetric Encryption and Signing — Java

## Library choice
Java's JCA/JCE (`java.security`, `javax.crypto`) provides EC key generation, ECDSA, EdDSA (Java 15+), RSA-PSS, and RSA-OAEP without external dependencies. Always specify the full transformation string including padding — `"RSA/ECB/OAEPWithSHA-256AndMGF1Padding"` — never just `"RSA"`. Bouncy Castle is the right choice when you need curves beyond NIST (X25519, X448, Ed448) or need to parse X.509/PKCS structures directly. Nimbus JOSE+JWT is the ecosystem standard for JWT/JWK operations. For production signing where the private key must not leave an HSM, use a PKCS#11 provider (`SunPKCS11`) or a cloud KMS SDK.

## Reference implementation
```java
import java.security.*;
import java.security.spec.*;
import javax.crypto.*;
import javax.crypto.spec.*;

public final class AsymmetricCrypto {
    // Ed25519 signing — deterministic, no nonce, Java 15+
    public static KeyPair generateEd25519() throws GeneralSecurityException {
        KeyPairGenerator kpg = KeyPairGenerator.getInstance("Ed25519");
        return kpg.generateKeyPair();
    }

    public static byte[] sign(byte[] data, PrivateKey key)
            throws GeneralSecurityException {
        Signature sig = Signature.getInstance("Ed25519");
        sig.initSign(key);
        sig.update(data);
        return sig.sign();
    }

    public static boolean verify(byte[] data, byte[] signature, PublicKey key)
            throws GeneralSecurityException {
        Signature sig = Signature.getInstance("Ed25519");
        sig.initVerify(key);
        sig.update(data);
        return sig.verify(signature);
    }

    // RSA-OAEP for wrapping a DEK only — never the full payload
    public static byte[] encryptDek(byte[] dek, PublicKey pub)
            throws GeneralSecurityException {
        Cipher cipher = Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding");
        cipher.init(Cipher.ENCRYPT_MODE, pub);
        return cipher.doFinal(dek);
    }

    public static byte[] decryptDek(byte[] wrapped, PrivateKey priv)
            throws GeneralSecurityException {
        Cipher cipher = Cipher.getInstance("RSA/ECB/OAEPWithSHA-256AndMGF1Padding");
        cipher.init(Cipher.DECRYPT_MODE, priv);
        return cipher.doFinal(wrapped);
    }
}
```

## Language-specific gotchas
- `Signature` is not thread-safe. Create a new instance per operation or use a `ThreadLocal<Signature>`. `KeyPairGenerator` is thread-safe after initialization.
- `Cipher.getInstance("RSA")` resolves to `RSA/ECB/PKCS1Padding` on the SunJCE provider — this is the Bleichenbacher-vulnerable padding. Always use `"RSA/ECB/OAEPWithSHA-256AndMGF1Padding"` for encryption and `"RSASSA-PSS"` for signing.
- Ed25519 requires Java 15+. On Java 11, use EC P-256 with `"SHA256withECDSA"` or add Bouncy Castle for Ed25519 via `"Ed25519"` with the BC provider.
- For JWT with Nimbus: `JWSVerifier verifier = new ECDSAVerifier(ecPublicKey)`. Call `signedJWT.verify(verifier)` and check `signedJWT.getJWTClaimsSet()` only after verification returns true. Do not parse claims before calling verify.
- RSA key generation: `KeyPairGenerator.getInstance("RSA"); kpg.initialize(3072)`. The `initialize(int)` overload uses `SecureRandom` automatically. `initialize(1024)` will compile and run — there is no runtime guard. Enforce the minimum in your generator factory.
- `GeneralSecurityException` is the checked supertype. Catch specific subtypes (`InvalidKeyException`, `SignatureException`) when you can log meaningfully; let others propagate as unchecked wrappers.

## Tests to write
- Round-trip Ed25519: sign, verify with matching public key, assert true.
- Wrong-key rejection: sign with key A, verify with key B, assert false or throws `SignatureException`.
- Tampered data: sign, flip a byte, verify, assert false.
- RSA OAEP round-trip: encrypt 32-byte DEK, decrypt, assert equality.
- Algorithm string completeness: assert that `Cipher.getInstance("RSA")` is not used anywhere in the codebase (static analysis or reflection test).
- Key size: generate RSA key pair, assert `((RSAKey) keyPair.getPublic()).getModulus().bitLength() >= 2048`.
