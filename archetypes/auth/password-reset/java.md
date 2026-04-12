---
schema_version: 1
archetype: auth/password-reset
language: java
principles_file: _principles.md
libraries:
  preferred: java.security.SecureRandom (stdlib)
  acceptable:
    - Spring Security's DefaultPasswordEncoder + SecureRandom
  avoid:
    - name: java.util.Random
      reason: PRNG — not cryptographically secure.
    - name: UUID.randomUUID()
      reason: Delegates to SecureRandom internally but intent is opaque; use SecureRandom.nextBytes directly.
minimum_versions:
  java: "21"
---

# Secure Password Reset — Java

## Library choice
Java's `java.security.SecureRandom` generates cryptographically secure token bytes — no third-party library is needed for token generation. SHA-256 hashing uses `java.security.MessageDigest`. Spring Security's `PasswordEncoder` (from `auth/password-hashing`) handles the new password. JPA/Spring Data manages token persistence.

## Reference implementation
```java
@Service
@RequiredArgsConstructor
public class PasswordResetService {
    private static final int TOKEN_BYTES = 32;
    private static final Duration EXPIRY = Duration.ofMinutes(30);
    private static final SecureRandom RNG = new SecureRandom();

    private final UserRepository users;
    private final ResetTokenRepo tokens;
    private final PasswordEncoder encoder;
    private final MailService mail;

    public void requestReset(String email) {
        users.findByEmail(email).ifPresent(user -> {
            tokens.invalidateAllForUser(user.getId());
            byte[] raw = new byte[TOKEN_BYTES];
            RNG.nextBytes(raw);
            String tokenHex = HexFormat.of().formatHex(raw);
            String hash = HexFormat.of().formatHex(
                    MessageDigest.getInstance("SHA-256").digest(raw));
            tokens.save(new ResetToken(user.getId(), hash, Instant.now().plus(EXPIRY)));
            mail.sendResetLink(user.getEmail(), tokenHex);
        });
    }

    public boolean redeemReset(String tokenHex, String newPassword) {
        byte[] raw;
        try { raw = HexFormat.of().parseHex(tokenHex); }
        catch (IllegalArgumentException e) { return false; }
        if (raw.length != TOKEN_BYTES) return false;
        String hash = HexFormat.of().formatHex(
                MessageDigest.getInstance("SHA-256").digest(raw));
        return tokens.findByHashAndNotConsumedAndNotExpired(hash)
            .map(record -> {
                tokens.markConsumed(hash);
                users.updatePassword(record.getUserId(), encoder.encode(newPassword));
                return true;
            }).orElse(false);
    }
}
```

## Language-specific gotchas
- `MessageDigest` is not thread-safe — never store it as a shared mutable field without synchronization. Either synchronize access, use `ThreadLocal<MessageDigest>`, or call `MessageDigest.getInstance("SHA-256")` per operation. The static field with `synchronized` or using `HexFormat` + `MessageDigest` inline is acceptable only if calls are synchronized.
- `HexFormat.of().parseHex(hex)` (Java 17+) throws `IllegalArgumentException` on invalid input — catch it and return `false`.
- Spring Data's `@Modifying @Query` for `invalidateAllForUser` must be in a `@Transactional` method. Without a transaction, the update and subsequent insert may not be atomic.
- `SecureRandom` is expensive to instantiate but cheap to reuse. One static instance per JVM is the correct pattern — it is thread-safe.
- Never log `tokenHex`. Log `userId` and the operation name only.

## Tests to write
- `requestReset` for an unknown email returns without exception and sends no mail.
- `redeemReset` with a valid token hex returns `true` and the user's password changes.
- Second `redeemReset` with the same hex returns `false`.
- `redeemReset` with an expired token returns `false`.
- `redeemReset` with a non-hex string returns `false` without throwing.
