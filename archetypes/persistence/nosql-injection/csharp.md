---
schema_version: 1
archetype: persistence/nosql-injection
language: csharp
principles_file: _principles.md
libraries:
  preferred: MongoDB.Driver
  acceptable:
    - StackExchange.Redis
  avoid:
    - name: MongoDB.Driver BsonDocument from raw request JSON
      reason: Deserialising user JSON directly into BsonDocument passes operator keys ($where, $ne) straight to the query engine.
minimum_versions:
  dotnet: "10.0"
---

# NoSQL Injection Defense — C#

## Library choice
`MongoDB.Driver` exposes `Builders<T>.Filter` which produces typed `FilterDefinition<T>` objects. Each field access is bound to the document class via a lambda expression — there is no mechanism to inject an operator through a typed field. For Redis, `StackExchange.Redis` uses method-based commands (`StringGetAsync(key)`) rather than raw command text, which eliminates command-injection vectors; key construction from user input must still be validated.

## Reference implementation
```csharp
using MongoDB.Bson;
using MongoDB.Driver;

public sealed class UserRepository(IMongoCollection<UserDocument> collection)
{
    private static readonly HashSet<string> SortableFields =
        new(StringComparer.Ordinal) { "Email", "CreatedAt" };

    // Typed lambda — no operator injection possible through the email parameter.
    public async Task<UserDocument?> FindByEmailAsync(string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var filter = Builders<UserDocument>.Filter.Eq(u => u.Email, email);
        return await collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    // Allowlist for dynamic sort field.
    public async Task<List<UserDocument>> ListAsync(string sortField, CancellationToken ct)
    {
        if (!SortableFields.Contains(sortField))
            throw new ArgumentException("Unknown sort field.", nameof(sortField));
        var sort = Builders<UserDocument>.Sort.Ascending(sortField);
        return await collection.Find(FilterDefinition<UserDocument>.Empty)
                               .Sort(sort)
                               .ToListAsync(ct);
    }
}

public sealed class UserDocument
{
    public ObjectId Id { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

## Language-specific gotchas
- Never pass a `BsonDocument` built with `BsonDocument.Parse(requestBody)` as a filter. A client sending `{"password": {"$ne": ""}}` bypasses password checks. Always deserialise the request body into a typed DTO first.
- `FilterDefinition<T>` has an implicit conversion from `string` (raw JSON). Using that implicit conversion with user input is injection. Always use `Builders<T>.Filter` factory methods.
- `MongoDB.Driver` 3.x enables the `$where` operator by default; if your use case does not need server-side JS, disable it in the `MongoClientSettings` via the `ServerApi` strict mode flag.
- For Redis keys, validate with a regex before concatenation: `Regex.IsMatch(userId, @"^[a-zA-Z0-9\-]{1,36}$")` before `$"session:{userId}"`.

## Tests to write
- Pass `{"$ne": ""}` as the email string; assert the repository returns null and does not throw a driver exception that indicates a query was executed with an operator key.
- Pass a sort field not in the allowlist; assert `ArgumentException` is thrown before any database call.
- Integration test: inject a value containing a newline and a MongoDB operator into each typed field; assert the document is either not found or correctly matched — no driver exception.
- Redis key test: pass `*` and `../admin` as user IDs; assert the key construction throws `ArgumentException` before the Redis command is issued.
