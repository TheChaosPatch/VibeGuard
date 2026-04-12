---
schema_version: 1
archetype: crypto/asymmetric-encryption
language: python
principles_file: _principles.md
libraries:
  preferred: cryptography
  acceptable:
    - PyNaCl (for Ed25519/X25519)
    - boto3 (AWS KMS asymmetric)
    - google-cloud-kms
  avoid:
    - name: pycrypto / pycryptodome for RSA PKCS1v1.5 encryption
      reason: PKCS1v1.5 encryption is vulnerable to Bleichenbacher; use OAEP.
    - name: M2Crypto
      reason: Thin OpenSSL wrapper with awkward memory semantics; cryptography package is safer.
minimum_versions:
  python: "3.11"
---

# Asymmetric Encryption and Signing — Python

## Library choice
The `cryptography` package (PyCA) covers ECDSA, Ed25519, RSA-PSS, RSA-OAEP, and X25519 key exchange with a consistent, well-audited API. For Ed25519 signing specifically, `PyNaCl` (`nacl.signing`) is an excellent alternative — its API makes misuse structurally difficult. For cloud-KMS-backed keys (AWS, GCP, Azure), use the respective SDK; the private key never leaves the HSM. Avoid `pycrypto`/`pycryptodome` for new RSA encryption work — there are too many examples using the insecure PKCS1v1.5 padding.

## Reference implementation
```python
from __future__ import annotations
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
from cryptography.hazmat.primitives.asymmetric import padding, rsa
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.exceptions import InvalidSignature

def generate_ed25519_key() -> Ed25519PrivateKey:
    return Ed25519PrivateKey.generate()

def sign_ed25519(data: bytes, private_key: Ed25519PrivateKey) -> bytes:
    return private_key.sign(data)

def verify_ed25519(data: bytes, sig: bytes, public_key: Ed25519PublicKey) -> bool:
    try:
        public_key.verify(sig, data)
        return True
    except InvalidSignature:
        return False

_OAEP_PADDING = padding.OAEP(
    mgf=padding.MGF1(algorithm=hashes.SHA256()),
    algorithm=hashes.SHA256(), label=None,
)

def rsa_encrypt_dek(dek: bytes, public_key: rsa.RSAPublicKey) -> bytes:
    return public_key.encrypt(dek, _OAEP_PADDING)

def rsa_decrypt_dek(wrapped: bytes, private_key: rsa.RSAPrivateKey) -> bytes:
    return private_key.decrypt(wrapped, _OAEP_PADDING)

def export_public_pem(private_key: Ed25519PrivateKey) -> bytes:
    return private_key.public_key().public_bytes(
        serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo,
    )
```

## Language-specific gotchas
- `Ed25519PrivateKey.sign` takes `data` directly — it hashes internally with SHA-512. Do not pre-hash. ECDSA via `EllipticCurvePrivateKey.sign` takes a `Prehashed` or a hash object; passing raw bytes to the wrong method silently signs the hash of nothing.
- `InvalidSignature` is raised on bad signatures; it is in `cryptography.exceptions`, not `ValueError`. Catch it explicitly and return False — never silence it and return True.
- RSA key generation: `rsa.generate_private_key(public_exponent=65537, key_size=3072)`. Never `key_size=1024` or `key_size=512`. The exponent must be 65537.
- For JWT signing with `cryptography`, use `PyJWT` with `algorithms=["ES256"]` or `algorithms=["EdDSA"]`. Always pass `algorithms` to `jwt.decode` — omitting it allows `alg: none`.
- Private key PEM serialization requires choosing an encryption scheme. For storage: `serialization.BestAvailableEncryption(passphrase)`. For in-memory transfer to a secrets manager that will re-wrap it, `serialization.NoEncryption()` is acceptable only if the transfer is immediate and the variable is deleted afterward.
- `PyNaCl`'s `SigningKey` is a good alternative for Ed25519: `nacl.signing.SigningKey.generate()` and its `sign`/`verify` methods handle encoding cleanly.

## Tests to write
- Round-trip Ed25519: sign data, verify with the corresponding public key, assert True.
- Wrong-key rejection: sign with key A, verify with public key of key B, assert False.
- Tampered data: sign, change a byte, verify, assert False.
- RSA OAEP round-trip: generate a 32-byte DEK, encrypt with public key, decrypt with private key, assert equality.
- Algorithm pinning: assert that `jwt.decode` with `algorithms=["ES256"]` rejects a token signed with a different algorithm.
- RSA key size: assert `private_key.key_size >= 2048`.
