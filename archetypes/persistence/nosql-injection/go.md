---
schema_version: 1
archetype: persistence/nosql-injection
language: go
principles_file: _principles.md
libraries:
  preferred: go.mongodb.org/mongo-driver/v2
  acceptable:
    - github.com/redis/go-redis/v9
  avoid:
    - name: bson.D from decoded request body
      reason: Decoding user JSON into bson.D passes raw operator keys to the query engine without any type checking.
minimum_versions:
  go: "1.23"
---

# NoSQL Injection Defense — Go

## Library choice
`go.mongodb.org/mongo-driver/v2` is the official MongoDB driver. Filters are built using `bson.D` literals with explicitly named keys, or via `mongo-driver`'s typed filter helpers. The key discipline: never decode a user-supplied JSON body directly into a `bson.D` or `bson.M` and use it as a filter. For Redis, `github.com/redis/go-redis/v9` exposes typed command methods; key validation is still required.

## Reference implementation
```go
package persistence

import (
    "context"
    "errors"
    "regexp"

    "go.mongodb.org/mongo-driver/v2/bson"
    "go.mongodb.org/mongo-driver/v2/mongo"
    "go.mongodb.org/mongo-driver/v2/mongo/options"
)

var (
    validSortFields = map[string]struct{}{"email": {}, "created_at": {}}
    userIDPattern   = regexp.MustCompile(`^[a-f0-9]{24}$`)
    ErrInvalidInput = errors.New("invalid input")
)

type User struct {
    ID    bson.ObjectID `bson:"_id"`
    Email string        `bson:"email"`
}
type UserRepository struct{ col *mongo.Collection }

func NewUserRepository(col *mongo.Collection) *UserRepository { return &UserRepository{col: col} }

func (r *UserRepository) FindByEmail(ctx context.Context, email string) (*User, error) {
    filter := bson.D{{Key: "email", Value: email}}
    var user User
    err := r.col.FindOne(ctx, filter).Decode(&user)
    if errors.Is(err, mongo.ErrNoDocuments) { return nil, nil }
    return &user, err
}

func (r *UserRepository) ListSorted(ctx context.Context, field string) ([]User, error) {
    if _, ok := validSortFields[field]; !ok { return nil, ErrInvalidInput }
    opts := options.Find().SetSort(bson.D{{Key: field, Value: 1}}).SetLimit(50)
    cur, err := r.col.Find(ctx, bson.D{}, opts)
    if err != nil { return nil, err }
    var users []User
    return users, cur.All(ctx, &users)
}
```

## Language-specific gotchas
- `bson.M` is `map[string]any`. If you unmarshal user JSON into a `bson.M` and use it as a filter, the user controls the operator keys. Use a typed struct or validate each key explicitly.
- `mongo-driver` v2 changed the import path — ensure `go.mongodb.org/mongo-driver/v2` is used, not the v1 path which has different API semantics.
- `$where` sends a JavaScript string to the MongoDB server. Never accept it from request input. The driver does not block it.
- For `go-redis`, build keys from validated segments: `fmt.Sprintf("session:%s", userID)` where `userIDPattern.MatchString(userID)` is checked first.
- Always pass `context` with a deadline to `FindOne` and `Find` — an unbounded query can hold a connection indefinitely.

## Tests to write
- Unit test: pass a map `{"$ne": ""}` serialised as JSON, decode it into the expected string type, assert type assertion fails before filter construction.
- `ListSorted` with an unknown field asserts `ErrInvalidInput` is returned, no collection call made.
- Integration test: confirm `FindByEmail` with a value containing `$gt` returns no result and no driver error.
- Redis key: pass `*` as userID, assert the key function returns an error before any Redis command.
