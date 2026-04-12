---
schema_version: 1
archetype: auth/password-reset
language: php
principles_file: _principles.md
libraries:
  preferred: random_bytes (stdlib)
  acceptable:
    - Laravel Password Broker (built-in, for Laravel apps)
  avoid:
    - name: rand() / mt_rand()
      reason: PRNG — not cryptographically secure.
    - name: uniqid()
      reason: Based on microtime — predictable and not a CSPRNG.
minimum_versions:
  php: "8.3"
---

# Secure Password Reset — PHP

## Library choice
PHP's `random_bytes(32)` (stdlib, PHP 7+) draws from the OS CSPRNG. `bin2hex` encodes for URL delivery; `hash('sha256', $raw, true)` hashes for storage. Laravel's built-in Password Broker (`Password::sendResetLink`) implements all of these principles — use it for Laravel applications rather than reimplementing.

## Reference implementation
```php
<?php declare(strict_types=1);

class PasswordResetService
{
    private const TOKEN_BYTES = 32;
    private const EXPIRY_SECONDS = 1800;

    public function __construct(
        private readonly UserRepository $users, private readonly TokenRepository $tokens,
        private readonly Mailer $mailer, private readonly PasswordHasher $hasher,
    ) {}

    public function requestReset(string $email): void
    {
        $user = $this->users->findByEmail($email);
        if ($user === null) { return; }
        $this->tokens->invalidateAll($user->id);
        $raw = random_bytes(self::TOKEN_BYTES);
        $hash = bin2hex(hash('sha256', $raw, true));
        $this->tokens->create(userId: $user->id, tokenHash: $hash, expiresAt: time() + self::EXPIRY_SECONDS);
        $this->mailer->sendResetLink($user->email, bin2hex($raw));
    }

    public function redeemReset(string $tokenHex, string $newPassword): bool
    {
        if (!ctype_xdigit($tokenHex) || strlen($tokenHex) !== self::TOKEN_BYTES * 2) { return false; }
        $raw = hex2bin($tokenHex);
        $hash = bin2hex(hash('sha256', $raw, true));
        $record = $this->tokens->findValid($hash);
        if ($record === null || $record->consumed || $record->expiresAt < time()) { return false; }
        $this->tokens->consume($hash);
        $this->users->updatePassword($record->userId, $this->hasher->hash($newPassword));
        $this->users->invalidateSessions($record->userId);
        return true;
    }
}
```

## Language-specific gotchas
- `hash('sha256', $raw, true)` returns raw bytes — pass `true` as the third argument for binary output, then `bin2hex` it for storage. `hash('sha256', $raw)` (without `true`) returns a hex string, and double-hex-encoding the hash wastes space and causes lookup mismatches.
- `ctype_xdigit($tokenHex)` validates that the input is pure hex. `hex2bin` on arbitrary user input can silently produce truncated results on odd-length strings — always validate length and character set first.
- Laravel's `Password::reset` closure is the right integration point when using the framework — do not call `DB::table('password_reset_tokens')` directly from a controller.
- Wrap `invalidateAll` + `create` in a database transaction (`DB::transaction` in Laravel, or `PDO::beginTransaction`) to prevent a race condition where two simultaneous requests both produce valid tokens.
- Never log `$tokenHex`. Log `$user->id` and the action only.

## Tests to write
- `requestReset` for an unknown email returns `void` and sends no mail.
- `redeemReset` with a valid hex token returns `true`.
- `redeemReset` called twice with the same token returns `true` then `false`.
- `redeemReset` with a non-hex string returns `false` without throwing.
- `redeemReset` with an expired record returns `false`.
