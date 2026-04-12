---
schema_version: 1
archetype: persistence/orm-security
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.EntityFrameworkCore
  acceptable:
    - Dapper
  avoid:
    - name: Binding entities directly in controllers
      reason: Mass assignment via model binding. Use dedicated DTOs.
    - name: LazyLoadingProxies in API projects
      reason: Triggers N+1 queries during serialization and leaks navigation properties.
minimum_versions:
  dotnet: "10.0"
---

# ORM Security — C#

## Library choice
`Microsoft.EntityFrameworkCore` is the default ORM. Its LINQ surface is safe against injection by construction — `.Where(u => u.Email == email)` compiles to a parameterized query. The danger is not in the query builder but in the object-mapping layer: model binding, navigation property serialization, and `FromSqlRaw`. Use `IDbContextFactory<T>` to scope contexts per operation, disable lazy-loading proxies in API-facing services, and never expose entity types outside the data-access layer.

## Reference implementation
```csharp
// Command DTO — allowlists exactly the fields the client may set.
public sealed record CreateUserCommand(string Email, string DisplayName);

// Response DTO — projects only what the caller may see.
public sealed record UserResponse(Guid Id, string Email, string DisplayName);

public sealed class UserRepository(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<UserResponse> CreateAsync(
        CreateUserCommand cmd, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = new UserEntity
        {
            Id = Guid.CreateVersion7(),
            Email = cmd.Email,
            DisplayName = cmd.DisplayName,
            // IsAdmin, CreatedAt, Version — server-controlled, never from client
        };
        db.Users.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<IReadOnlyList<UserResponse>> ListAsync(
        int limit, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .OrderBy(u => u.Email)
            .Take(limit)
            .Select(u => new UserResponse(u.Id, u.Email, u.DisplayName))
            .ToListAsync(ct);
    }

    private static UserResponse ToResponse(UserEntity e) =>
        new(e.Id, e.Email, e.DisplayName);
}
```

## Language-specific gotchas
- `[FromBody] UserEntity user` in a controller binds every public property, including `IsAdmin`, `Balance`, and `Role`. Always bind to a command DTO with only the fields the client is allowed to set.
- `return Ok(entity)` serializes every public property plus every loaded navigation property. If `User` has `ICollection<Order> Orders` and it was eagerly loaded, the entire order history is in the response. Return a projected DTO.
- `FromSqlRaw("SELECT * FROM Users WHERE Email = '" + email + "'")` is injection through the ORM. Use `FromSqlInterpolated($"... WHERE Email = {email}")` which intercepts the `FormattableString` and binds parameters.
- `UseLazyLoadingProxies()` in an API project means the JSON serializer triggers a query for every navigation property it finds. Disable proxies and use `.Include()` deliberately, or use projection (`.Select()`).
- EF Core's `Update(entity)` marks every property as modified. If the client supplied extra fields on a DTO you deserialized loosely, those values are written. Use `Attach` + explicit property marking, or map field-by-field from the command DTO.
- Pagination: always apply `.Take(limit)` with a server-enforced max. `db.Users.ToListAsync()` loads every user into memory — trivial DoS on any table with more than a few thousand rows.

## Tests to write
- Mass assignment: POST a body with `{ "email": "a@b.com", "isAdmin": true }` — assert the created user has `IsAdmin == false`.
- Response projection: GET a user — assert the response JSON does not contain `PasswordHash`, `IsAdmin`, or navigation properties.
- Pagination cap: request `limit=999999` — assert the service clamps or rejects it.
- `FromSqlInterpolated` parameterization: call with an email containing a single quote — assert no parse error and correct results.
- Concurrency token: update a user with a stale `Version` — assert `DbUpdateConcurrencyException`.
