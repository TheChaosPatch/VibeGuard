---
schema_version: 1
archetype: auth/password-reset
language: typescript
principles_file: _principles.md
libraries:
  preferred: node:crypto (stdlib)
  acceptable:
    - crypto-js (browser contexts only)
  avoid:
    - name: Math.random()
      reason: PRNG — not cryptographically secure for tokens.
minimum_versions:
  node: "22"
  typescript: "5.4"
---

# Secure Password Reset — TypeScript

## Library choice
Same as JavaScript: `node:crypto` stdlib for token generation and hashing. TypeScript adds strict types around the token lifecycle, making it harder to accidentally use a raw token where a hash is expected.

## Reference implementation
```typescript
import { randomBytes, createHash } from 'node:crypto';

const TOKEN_BYTES = 32 as const;
const EXPIRY_MS   = 30 * 60 * 1000 as const;

type RawToken  = string & { readonly __brand: 'RawToken' };
type TokenHash = string & { readonly __brand: 'TokenHash' };

function generateToken(): { raw: RawToken; hash: TokenHash } {
    const buf  = randomBytes(TOKEN_BYTES);
    const raw  = buf.toString('hex') as RawToken;
    const hash = createHash('sha256').update(buf).digest('hex') as TokenHash;
    return { raw, hash };
}

function hashToken(rawHex: string): TokenHash | null {
    try {
        const buf = Buffer.from(rawHex, 'hex');
        if (buf.length !== TOKEN_BYTES) return null;
        return createHash('sha256').update(buf).digest('hex') as TokenHash;
    } catch {
        return null;
    }
}

export async function requestReset(db: Db, send: Mailer, email: string): Promise<void> {
    const user = await db.getUserByEmail(email);
    if (!user) return;

    await db.invalidateResetTokens(user.id);
    const { raw, hash } = generateToken();
    await db.createResetToken(user.id, hash, new Date(Date.now() + EXPIRY_MS));
    await send.sendResetLink(user.email, raw);
}

export async function redeemReset(db: Db, hasher: Hasher, tokenHex: string, newPw: string): Promise<boolean> {
    const hash = hashToken(tokenHex);
    if (!hash) return false;

    const record = await db.getResetToken(hash);
    if (!record || record.consumed || record.expiresAt < new Date()) return false;

    await db.consumeToken(hash);
    await db.updatePassword(record.userId, await hasher.hash(newPw));
    await db.invalidateSessions(record.userId);
    return true;
}
```

## Language-specific gotchas
- Branded types (`RawToken`, `TokenHash`) are compile-time only but prevent accidentally passing a raw token hex where a hash is expected — a common source of security bugs in token storage code.
- `TokenHash | null` return type from `hashToken` forces callers to handle the invalid-input case at the type level, not just at runtime.
- TypeScript does not enforce branded types at runtime — the `as` casts are escape hatches. Isolate all raw-to-hash conversions in the `hashToken` function so it is the single auditability point.
- In NestJS, put the reset service behind a rate-limited guard on the controller endpoints. TypeScript types do not substitute for middleware-level rate limiting.
- `Buffer.from(hex, 'hex')` in TypeScript is the same as in JavaScript — length check after construction is still required.

## Tests to write
- `requestReset` for unknown email returns `void` and mailer is not called.
- `redeemReset` with valid `RawToken` hex returns `true`.
- `redeemReset` called twice with the same token returns `true` then `false`.
- `redeemReset` with malformed hex string returns `false` without throwing.
- `redeemReset` with expired record returns `false`.
