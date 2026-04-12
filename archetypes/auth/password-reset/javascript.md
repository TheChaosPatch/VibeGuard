---
schema_version: 1
archetype: auth/password-reset
language: javascript
principles_file: _principles.md
libraries:
  preferred: node:crypto (stdlib)
  acceptable:
    - crypto-js (browser contexts only)
  avoid:
    - name: Math.random()
      reason: PRNG — not cryptographically secure for tokens.
    - name: uuid
      reason: UUIDs are not sourced from crypto.randomBytes explicitly in all versions.
minimum_versions:
  node: "22"
---

# Secure Password Reset — JavaScript

## Library choice
Node.js's built-in `node:crypto` module provides `randomBytes(32)` for token generation and `createHash('sha256')` for storage hashing — no third-party dependency is needed. Database persistence uses whichever ORM or query builder the project already uses (Prisma, Drizzle, pg, etc.).

## Reference implementation
```js
import { randomBytes, createHash } from 'node:crypto';

const TOKEN_BYTES = 32;
const EXPIRY_MS = 30 * 60 * 1000;

export async function requestReset(db, emailSender, email) {
    const user = await db.user.findUnique({ where: { email } });
    if (!user) return;
    await db.passwordResetToken.updateMany({
        where: { userId: user.id, consumed: false },
        data: { consumed: true },
    });
    const raw = randomBytes(TOKEN_BYTES);
    const hash = createHash('sha256').update(raw).digest('hex');
    await db.passwordResetToken.create({
        data: {
            userId: user.id, tokenHash: hash,
            expiresAt: new Date(Date.now() + EXPIRY_MS), consumed: false,
        },
    });
    await emailSender.sendResetLink(user.email, raw.toString('hex'));
}

export async function redeemReset(db, hashPassword, tokenHex, newPassword) {
    let raw;
    try {
        raw = Buffer.from(tokenHex, 'hex');
        if (raw.length !== TOKEN_BYTES) return false;
    } catch { return false; }
    const hash = createHash('sha256').update(raw).digest('hex');
    const record = await db.passwordResetToken.findUnique({ where: { tokenHash: hash } });
    if (!record || record.consumed || record.expiresAt < new Date()) return false;
    await db.passwordResetToken.update({ where: { tokenHash: hash }, data: { consumed: true } });
    await db.user.update({
        where: { id: record.userId },
        data: { passwordHash: await hashPassword(newPassword) },
    });
    await db.session.deleteMany({ where: { userId: record.userId } });
    return true;
}
```

## Language-specific gotchas
- `Buffer.from(hex, 'hex')` silently ignores trailing odd characters in older Node versions — check `raw.length === TOKEN_BYTES` explicitly.
- The `consumed` flag update and the new token insert should be in a database transaction to prevent a race where two simultaneous requests both get valid tokens.
- Never put the raw token hex in a server-side log. Log `userId` only. The link is emailed — treat it with the same sensitivity as a password.
- In Express, map `false` return values to `400 Bad Request`, not `404` — revealing whether a token hash exists (even as "not found") is an oracle.
- Use Prisma's `$transaction` or pg's `BEGIN/COMMIT` to make the invalidate-old + insert-new atomic.

## Tests to write
- `requestReset` for an unknown email returns `undefined` and sends no email.
- `redeemReset` with a valid token returns `true` and deletes sessions.
- `redeemReset` called twice returns `true` then `false`.
- `redeemReset` with an expired record returns `false`.
- `redeemReset` with a non-hex string returns `false` without throwing.
