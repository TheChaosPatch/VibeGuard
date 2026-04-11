---
schema_version: 1
archetype: auth/authorization
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.AspNetCore.Authorization
  acceptable:
    - Casbin.NET
    - Microsoft.Identity.Web (for Entra policy integration)
  avoid:
    - name: Scattered "if (user.IsInRole(...))" checks
      reason: Untestable, un-auditable, inconsistent across endpoints.
    - name: "[Authorize(Roles = \"...\")] as the only layer"
      reason: Edge-only authorization breaks the first time a service is reached via a new code path.
minimum_versions:
  dotnet: "10.0"
---

# Authorization — C#

## Library choice
`Microsoft.AspNetCore.Authorization` is the stock answer and it is deliberately policy-based. Define an `IAuthorizationRequirement` per rule, implement an `AuthorizationHandler<TRequirement, TResource>` to evaluate it against a *loaded* resource, and call `IAuthorizationService.AuthorizeAsync(user, resource, policyName)` from inside the handler after loading the entity. This shape — resource-based evaluation via an injected service — is what keeps authorization consistent and testable. `Casbin.NET` is worth considering if your rules are complex enough to benefit from a proper policy language; for most services, the stock framework is plenty. `Microsoft.Identity.Web` layers Entra-specific group/scope integration on top of the same policy system.

## Reference implementation
```csharp
using Microsoft.AspNetCore.Authorization;

public sealed class CanReadOrderRequirement : IAuthorizationRequirement { }

public sealed class CanReadOrderHandler : AuthorizationHandler<CanReadOrderRequirement, Order>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, CanReadOrderRequirement req, Order resource)
    {
        var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = ctx.User.FindFirst("tenant_id")?.Value;
        // Deny by default — succeed only on a positive, explicit match.
        if (userId is not null && tenantId is not null
            && resource.TenantId == tenantId
            && (resource.OwnerId == userId || ctx.User.IsInRole("tenant-admin")))
        {
            ctx.Succeed(req);
        }
        return Task.CompletedTask;
    }
}

public static class Registration
{
    public static void AddOrderAuthorization(this IServiceCollection s)
    {
        s.AddAuthorizationBuilder()
            .AddPolicy("order.read", p => p.AddRequirements(new CanReadOrderRequirement()));
        s.AddSingleton<IAuthorizationHandler, CanReadOrderHandler>();
    }
}

// Handler usage — load first, then authorize on the loaded entity.
public static async Task<IResult> GetOrder(
    Guid id, ClaimsPrincipal user, IOrderRepository repo, IAuthorizationService authz)
{
    var order = await repo.FindAsync(id);
    if (order is null) return Results.NotFound();
    var result = await authz.AuthorizeAsync(user, order, "order.read");
    return result.Succeeded ? Results.Ok(order) : Results.Forbid();
}
```

## Language-specific gotchas
- `ctx.Succeed(req)` is the only way a requirement passes. If no handler calls `Succeed`, the requirement fails, which means deny-by-default is the default. Do not call `ctx.Fail()` unless you specifically want to short-circuit other handlers — it makes the requirement un-succeedable even if a later handler would have allowed it.
- Always authorize on the **loaded entity**, not the id. The signature `AuthorizationHandler<TRequirement, TResource>` forces this shape — use it. Writing your own `AuthorizationHandler<TRequirement>` without a resource type is the escape hatch that leads to IDORs.
- `IAuthorizationService.AuthorizeAsync(user, resource, policy)` returns an `AuthorizationResult` with `Succeeded` and `Failure` — check `Succeeded`, not the absence of `Failure`, because failure reasons can be present on a successful result in some edge cases.
- `[Authorize(Policy = "order.read")]` on a minimal-API route *requires* a route-level resource convention; resource-based policies almost always want explicit `authz.AuthorizeAsync(...)` calls inside the handler, after loading the entity. Don't mix the two styles.
- `Results.Forbid()` returns 403; `Results.Unauthorized()` returns 401. Authorization failures are 403. If you find yourself returning 401 from a handler, something is wrong — authentication already ran at the middleware layer.
- Multi-tenant scoping: don't put tenant filtering *only* in the handler. Repository methods take `tenantId` as a required argument and enforce the filter at the query level — defense in depth. The authorization handler double-checks it.
- Cache authorization decisions only at per-request scope. Cross-request caching is a correctness disaster waiting for a permission change to expose.

## Tests to write
- Deny by default: a user with no matching rule gets `Succeeded = false`.
- Owner allowed: order.OwnerId == user.sub → `Succeeded = true`.
- Tenant-admin allowed: user has `tenant-admin` role and matches `tenant_id` → allowed.
- Cross-tenant blocked: same user, order with a different `tenant_id` → denied, even if the user is a tenant-admin in *their* tenant.
- Missing claim handled: user has no `tenant_id` claim → denied (not NRE).
- Resource-not-loaded regression: assert the handler is only called with a non-null resource; a test that passes `null` confirms the handler branch short-circuits.
- Handler endpoint: integration test that hits `GET /orders/{id}` as a cross-tenant user and asserts 403 *and* asserts the order was loaded from the repository (proving the handler did the check, not a pre-filter that returned 404).
- Idempotence: running the handler twice with the same inputs yields the same result.
