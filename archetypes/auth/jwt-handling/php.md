---
schema_version: 1
archetype: auth/jwt-handling
language: php
principles_file: _principles.md
libraries:
  preferred: firebase/php-jwt
  acceptable:
    - lcobucci/jwt
  avoid:
    - name: Manual base64_decode of payload
      reason: Reads claims without signature verification.
    - name: Tymon/jwt-auth without explicit algorithm config
      reason: Older versions defaulted to HS256 with app key; wraps firebase/php-jwt without hardening.
minimum_versions:
  php: "8.3"
---

# JWT Handling — PHP

## Library choice
`firebase/php-jwt` is a widely used, actively maintained JWT library for PHP with support for RS256, ES256, and HS256. `lcobucci/jwt` is a more opinionated, strictly typed alternative with a builder pattern that makes explicit configuration easier to review. Both are acceptable; `lcobucci/jwt` is preferred for new PHP 8.3+ projects due to its stricter API.

## Reference implementation
```php
<?php
declare(strict_types=1);

use Firebase\JWT\JWT;
use Firebase\JWT\JWK;
use Firebase\JWT\Key;

const ALGORITHM = 'RS256';
const ISSUER    = 'https://auth.example.com';
const AUDIENCE  = 'https://api.example.com';
const TTL       = 900; // 15 minutes

function issueToken(string $subject, array $extraClaims, string $privateKeyPem, string $kid): string
{
    $now     = time();
    $payload = array_merge($extraClaims, [
        'sub' => $subject,
        'iss' => ISSUER,
        'aud' => AUDIENCE,
        'iat' => $now,
        'exp' => $now + TTL,
    ]);
    return JWT::encode($payload, openssl_pkey_get_private($privateKeyPem), ALGORITHM, $kid);
}

function validateToken(string $token, string $publicKeyPem): stdClass
{
    JWT::$leeway = 30;
    $decoded = JWT::decode($token, new Key($publicKeyPem, ALGORITHM));

    // Manual claim checks firebase/php-jwt doesn't do by default
    if ($decoded->iss !== ISSUER) throw new RuntimeException('Invalid issuer');
    if (!in_array(AUDIENCE, (array) $decoded->aud, true)) throw new RuntimeException('Invalid audience');

    return $decoded;
}
```

## Language-specific gotchas
- `firebase/php-jwt` validates `exp` and `nbf` automatically but does **not** validate `iss` or `aud` in the `decode` call — you must check those manually after decoding, as shown above.
- Pass a `Key` object (not a raw string) to `JWT::decode`. The `Key` constructor's second argument is the algorithm — this is the allowlist. Passing an array of `Key` objects keyed by `kid` enables automatic key selection for rotation.
- `JWT::$leeway` is a static property — it is global state. Set it once at bootstrap, not per request, and keep it at 30 seconds or less.
- `lcobucci/jwt`'s `Configuration` and `Validator` classes perform claim validation declaratively and are harder to misconfigure than `firebase/php-jwt`'s manual post-decode checks.
- Never pass the token in a GET query parameter (`?token=...`). Tokens in query strings appear in access logs, browser history, and `Referer` headers. Transport via `Authorization: Bearer` header only.

## Tests to write
- `validateToken(issueToken('u1', [], $priv, 'k1'), $pub)` returns an object with `sub === 'u1'`.
- Token with past `exp` → `JWT::decode` throws `ExpiredException`.
- Token signed with a different RSA key → throws `SignatureInvalidException`.
- Token with wrong `iss` → manual check throws `RuntimeException`.
- Token with wrong `aud` → manual check throws `RuntimeException`.
