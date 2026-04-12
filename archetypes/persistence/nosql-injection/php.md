---
schema_version: 1
archetype: persistence/nosql-injection
language: php
principles_file: _principles.md
libraries:
  preferred: mongodb/mongodb
  acceptable:
    - predis/predis
    - phpredis
  avoid:
    - name: $_POST or $_GET as MongoDB filter array
      reason: PHP superglobals allow nested array syntax (?email[$ne]=) which directly maps to BSON operator keys.
minimum_versions:
  php: "8.4"
---

# NoSQL Injection Defense — PHP

## Library choice
`mongodb/mongodb` (the official PHP library) builds filters as PHP arrays with explicit keys. The discipline is the same: construct the filter array from validated scalar values, never from superglobals or request-parsed arrays. For Redis, `predis/predis` and `phpredis` both use method-based commands.

## Reference implementation
```php
<?php
declare(strict_types=1);

use MongoDB\Collection;
use MongoDB\BSON\ObjectId;

final class UserRepository
{
    private const SORT_FIELDS = ['email', 'created_at'];

    public function __construct(private readonly Collection $collection) {}

    public function findByEmail(string $email): ?array
    {
        if (trim($email) === '') {
            throw new \InvalidArgumentException('email must not be blank');
        }
        // Scalar string typed by PHP 8 strict_types — operator injection is impossible.
        $doc = $this->collection->findOne(['email' => $email]);
        return $doc === null ? null : (array) $doc;
    }

    public function listSorted(string $field): array
    {
        if (!in_array($field, self::SORT_FIELDS, strict: true)) {
            throw new \InvalidArgumentException("Unknown sort field: $field");
        }
        $cursor = $this->collection->find(
            [],
            ['sort' => [$field => 1], 'limit' => 50]
        );
        return iterator_to_array($cursor, false);
    }
}
```

## Language-specific gotchas
- PHP's `http_build_query` / form arrays allow `email[$ne]=` in a query string. `$_GET['email']` then contains `['$ne' => '']` — an array, not a string. `declare(strict_types=1)` and typed function parameters cause a `TypeError` if an array is passed where a string is expected, blocking injection.
- Never use `$_POST` or `$_GET` directly as a filter array: `$this->collection->findOne($_POST)` lets the client control every key, including `$where`.
- MongoDB's PHP library accepts `$where` with a JavaScript string. Never expose this to user input.
- For Predis/phpredis key construction: validate user-supplied segments with `preg_match('/^[a-zA-Z0-9_-]{1,64}$/', $segment)` before interpolating.
- Use `declare(strict_types=1)` in every file that touches the data layer — it converts implicit coercions into `TypeError` exceptions.

## Tests to write
- `findByEmail(['$ne' => ''])` raises `TypeError` due to strict_types and typed parameter.
- `listSorted('password')` raises `InvalidArgumentException`.
- PHPUnit integration: insert a document, retrieve by email string, assert field equality.
- Fuzz the email parameter with nested arrays and assert `TypeError` is always raised.
