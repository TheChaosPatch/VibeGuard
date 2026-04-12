---
schema_version: 1
archetype: crypto/hashing-integrity
language: c
principles_file: _principles.md
libraries:
  preferred: OpenSSL (libcrypto — EVP_MAC, EVP_Digest)
  acceptable:
    - libsodium (crypto_auth_hmacsha256)
    - mbedTLS (mbedtls_md_hmac)
  avoid:
    - name: memcmp for MAC comparison
      reason: Compiler may optimise to early-exit; use CRYPTO_memcmp (OpenSSL) or sodium_memcmp.
    - name: MD5 / SHA-1 via EVP_md5() / EVP_sha1()
      reason: Broken algorithms; do not use for new integrity work.
    - name: Rolling a custom HMAC from raw SHA-256 blocks
      reason: Subtle key-handling bugs are guaranteed; use the HMAC API.
minimum_versions:
  openssl: "3.0"
---

# Hashing and Data Integrity — C

## Library choice
OpenSSL 3.x's `EVP_MAC` interface (`EVP_MAC_fetch("HMAC")`, `EVP_MAC_CTX_new`, `EVP_MAC_init`, `EVP_MAC_update`, `EVP_MAC_final`) is the recommended HMAC path — it is provider-aware, supports hardware offload, and compiles cleanly under OpenSSL 3's FIPS provider. `libsodium` is the right choice if you want a simpler API and can accept its opinionated key length (`crypto_auth_hmacsha256_KEYBYTES = 32`). `mbedTLS` is appropriate for embedded targets where OpenSSL is too large. In all cases, the constant-time comparator is library-specific: `CRYPTO_memcmp` (OpenSSL), `sodium_memcmp` (libsodium), or `mbedtls_ct_memcmp` (mbedTLS) — never `memcmp`.

## Reference implementation
```c
#include <openssl/evp.h>
#include <openssl/hmac.h>
#include <openssl/crypto.h>
#include <string.h>
#include <stdint.h>

#define HMAC_SHA256_LEN 32

/* Compute HMAC-SHA256. out must be at least HMAC_SHA256_LEN bytes.
   Returns 1 on success, 0 on error. */
int compute_hmac(const uint8_t *key, size_t key_len,
                 const uint8_t *data, size_t data_len,
                 uint8_t out[HMAC_SHA256_LEN]) {
    EVP_MAC *mac = EVP_MAC_fetch(NULL, "HMAC", NULL);
    if (!mac) return 0;

    EVP_MAC_CTX *ctx = EVP_MAC_CTX_new(mac);
    EVP_MAC_free(mac);
    if (!ctx) return 0;

    OSSL_PARAM params[] = {
        OSSL_PARAM_utf8_string("digest", "SHA256", 0),
        OSSL_PARAM_END,
    };
    size_t out_len = HMAC_SHA256_LEN;
    int ok = EVP_MAC_init(ctx, key, key_len, params) &&
             EVP_MAC_update(ctx, data, data_len) &&
             EVP_MAC_final(ctx, out, &out_len, HMAC_SHA256_LEN);
    EVP_MAC_CTX_free(ctx);
    return ok;
}

/* Constant-time HMAC verification. Returns 1 if equal, 0 otherwise. */
int verify_hmac(const uint8_t *key, size_t key_len,
                const uint8_t *data, size_t data_len,
                const uint8_t expected[HMAC_SHA256_LEN]) {
    uint8_t actual[HMAC_SHA256_LEN];
    if (!compute_hmac(key, key_len, data, data_len, actual)) return 0;
    /* CRYPTO_memcmp is constant-time regardless of content */
    return CRYPTO_memcmp(actual, expected, HMAC_SHA256_LEN) == 0;
}
```

## Language-specific gotchas
- `CRYPTO_memcmp(a, b, n)` is OpenSSL's constant-time comparator. It is equivalent to a timing-safe `memcmp`. The standard `memcmp` is explicitly not constant-time — modern compilers optimise it to SIMD instructions that terminate early. Never use `memcmp` for MAC comparison.
- `EVP_MAC_CTX` must be freed with `EVP_MAC_CTX_free` even on error paths. Consider a `goto cleanup` pattern for the error path — it is idiomatic C for resource cleanup and avoids leaks.
- OpenSSL 3.x introduced the provider model. `EVP_MAC_fetch(NULL, "HMAC", NULL)` fetches from the default provider. In a FIPS build, this will be the FIPS provider, which only allows approved algorithms. SHA-256 and SHA-512 are approved; MD5 and SHA-1 are not.
- Key zeroing: after use, zero the key buffer with `OPENSSL_cleanse(key_copy, key_len)` — not `memset`. The compiler may optimise away a `memset` on memory that is about to go out of scope; `OPENSSL_cleanse` is marked to prevent this.
- `libsodium` alternative: `crypto_auth_hmacsha256(out, data, data_len, key)` and `crypto_auth_hmacsha256_verify(tag, data, data_len, key)`. The verify function is constant-time internally. The key must be exactly `crypto_auth_hmacsha256_KEYBYTES` (32) bytes.
- Stack allocation of key buffers: keep key buffers short-lived on the stack only when you can guarantee the stack frame is cleared before the function returns. In general, prefer heap allocation via `OPENSSL_malloc` and explicit `OPENSSL_cleanse` + `OPENSSL_free`.

## Tests to write
- Round-trip: compute HMAC, verify with same key and data, assert return 1.
- Wrong-key rejection: compute with key A, verify with key B, assert return 0.
- Tampered data: compute, change one byte, verify, assert return 0.
- Zero-length data: compute HMAC of empty data, assert 32-byte output (not crash).
- Error paths: pass NULL pointers, assert `compute_hmac` returns 0 without crash.
- No memcmp: grep the codebase for `memcmp` used on HMAC output and fail the test if found.
