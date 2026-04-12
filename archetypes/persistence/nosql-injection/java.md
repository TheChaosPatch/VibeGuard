---
schema_version: 1
archetype: persistence/nosql-injection
language: java
principles_file: _principles.md
libraries:
  preferred: org.mongodb:mongodb-driver-sync
  acceptable:
    - org.mongodb:mongodb-driver-reactivestreams
    - io.lettuce:lettuce-core
  avoid:
    - name: Document from JSON request body
      reason: Parsing user-supplied JSON into a MongoDB Document object passes BSON operators through to the query engine.
minimum_versions:
  java: "21"
---

# NoSQL Injection Defense — Java

## Library choice
`mongodb-driver-sync` exposes `Filters` factory methods (`Filters.eq`, `Filters.and`) that build typed `Bson` filter objects from named fields and scalar values. This is the primary safe API. For reactive pipelines, `mongodb-driver-reactivestreams` uses the same `Filters` API. For Redis, `lettuce-core` uses typed command methods.

## Reference implementation
```java
import com.mongodb.client.MongoCollection;
import com.mongodb.client.model.Filters;
import com.mongodb.client.model.Sorts;
import org.bson.Document;
import org.bson.conversions.Bson;

import java.util.List;
import java.util.Set;

public final class UserRepository {
    private static final Set<String> SORT_FIELDS = Set.of("email", "createdAt");
    private final MongoCollection<Document> collection;

    public UserRepository(MongoCollection<Document> collection) {
        this.collection = collection;
    }

    public Document findByEmail(String email) {
        if (email == null || email.isBlank())
            throw new IllegalArgumentException("email must not be blank");
        // Filters.eq creates a typed BSON equality filter — no operator injection possible.
        Bson filter = Filters.eq("email", email);
        return collection.find(filter).first();
    }

    public List<Document> listSorted(String field) {
        if (!SORT_FIELDS.contains(field))
            throw new IllegalArgumentException("Unknown sort field: " + field);
        return collection.find()
                .sort(Sorts.ascending(field))
                .limit(50)
                .into(new java.util.ArrayList<>());
    }
}
```

## Language-specific gotchas
- `Document.parse(requestBody)` converts a JSON string into a `Document` object. If the request body contains `{"email": {"$ne": ""}}`, the resulting Document has an operator key. Never use this as a filter.
- `Filters.eq("email", value)` is safe regardless of what `value` contains as a string — the driver serialises it as a BSON string, not as an operator.
- Spring Data MongoDB's `MongoTemplate.findOne(Query, Class)` uses `Criteria` objects that are similarly safe when built via the fluent API. Avoid `BasicQuery` with user-supplied JSON strings.
- For Lettuce/Redis, validate key segments with a pattern before building the key string.
- Always use try-with-resources for `MongoClient` and `MongoCursor` to avoid connection leaks.

## Tests to write
- `findByEmail` with a value containing `$ne` and `$gt` — assert `Document` is null (no match), no driver exception.
- `listSorted("password")` asserts `IllegalArgumentException`, no database call occurs.
- Integration test: insert a document, retrieve by exact email, assert field equality.
- JUnit 5 parameterised test: try each known operator key as the email value; assert null return.
