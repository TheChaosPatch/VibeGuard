---
schema_version: 1
archetype: auth/authorization
language: python
principles_file: _principles.md
libraries:
  preferred: fastapi (native dependency injection)
  acceptable:
    - oso
    - casbin
    - django-guardian (Django only)
  avoid:
    - name: Scattered "if user.is_admin" checks
      reason: Untestable, un-auditable, inconsistent across endpoints.
    - name: Django's has_perm without object-level argument
      reason: Model-level perms are often mistaken for row-level; the object argument is mandatory for IDOR defense.
minimum_versions:
  python: "3.11"
---

# Authorization — Python

## Library choice
FastAPI's native dependency-injection system is the preferred authorization seam: declare a `Depends(check_order_read_access)` dependency that takes both the loaded `Order` and the `CurrentUser`, and returns the resource on success or raises `HTTPException(403)` on failure. This keeps the "load first, then authorize" shape structural rather than a convention. For richer rule systems, `oso` and `casbin` both offer external policy languages — useful when rules are complex enough to benefit from declarative definitions, but overkill for most services. Django projects have `django-guardian` for object-level permissions; use its `get_objects_for_user` for scoped queries, not just the default `has_perm`.

## Reference implementation
```python
from __future__ import annotations
from dataclasses import dataclass
from fastapi import APIRouter, Depends, HTTPException, status
from typing import Annotated


@dataclass(frozen=True, slots=True)
class Order:
    id: str
    owner_id: str
    tenant_id: str


@dataclass(frozen=True, slots=True)
class CurrentUser:
    sub: str
    tenant_id: str
    roles: frozenset[str]


class Forbidden(HTTPException):
    def __init__(self) -> None:
        super().__init__(status.HTTP_403_FORBIDDEN, "forbidden")


# Pure authorization rule — no HTTP, no DB, easy to unit-test.
def _authorize_order_read(user: CurrentUser, order: Order) -> None:
    if order.tenant_id != user.tenant_id:
        raise Forbidden()
    if order.owner_id == user.sub or "tenant-admin" in user.roles:
        return
    raise Forbidden()


# Dependency chain: load resource first, then authorize it.
async def require_order_read(
    order_id: str,
    repo: Annotated[OrderRepo, Depends()],
    user: Annotated[CurrentUser, Depends(current_user)],
) -> Order:
    order = await repo.find(order_id)
    if order is None:
        raise HTTPException(status.HTTP_404_NOT_FOUND)
    _authorize_order_read(user, order)
    return order


api = APIRouter()


@api.get("/orders/{order_id}")
async def get_order(order: Annotated[Order, Depends(require_order_read)]) -> Order:
    return order  # loaded and authorized by the dependency chain
```

## Language-specific gotchas
- `Depends(require_order_read)` *returns the loaded order* — the handler gets the authorized resource for free and can't accidentally re-query by id (which is how cross-tenant bugs slip in). Make this the standard pattern; a handler that takes `order_id: str` and queries itself has stepped outside the dependency chain and owes a manual authorization check.
- The `_authorize_order_read` function is pure: it takes `(user, order)` and either returns or raises. Keep it synchronous and side-effect-free. That makes it trivially unit-testable with no HTTP, no DB, no mocks.
- `Forbidden` is a single class so you can grep for every authorization failure in one go. Don't spell it `HTTPException(403)` inline at every call site — that's the anti-pattern of "authorization logic scattered across handlers."
- `frozenset[str]` for roles is deliberate: you can't mutate it at runtime, and set membership is O(1). Lists of roles work but encourage accidental mutation ("add a role just for this request") which is exactly the authorization bug we're trying to prevent.
- Django REST Framework: put the rule in `has_object_permission(self, request, view, obj)` on a `permissions.BasePermission` subclass, and make sure the view is a `RetrieveAPIView` or calls `self.check_object_permissions(request, obj)` after loading. DRF's default is **not** to call it on custom views — this is a frequent IDOR source.
- Multi-tenant: the repository layer enforces `tenant_id` scoping as part of the query. The authorization check then *re-validates* the scope against the loaded row. Two independent checks, different failure modes, defense in depth.
- Never cache authorization decisions past the request boundary. A user whose permissions were just revoked is still holding a valid session cookie; the next request needs a fresh check.

## Tests to write
- Deny by default: call `_authorize_order_read` with a user and order that don't match any allow rule; assert `Forbidden` is raised.
- Owner allowed: owner_id matches → returns normally, no exception.
- Tenant-admin allowed: user has `tenant-admin` and tenant matches → allowed.
- Cross-tenant blocked: tenant-admin in tenant A, order in tenant B → denied.
- Missing tenant on user: user with empty `tenant_id` → denied.
- Resource-not-loaded regression: route-level test that `GET /orders/missing` returns 404 and does *not* reach the authorization function.
- Cross-tenant integration: load order X (tenant A) as a user from tenant B → 403 *and* assert the repository returned the row (proving the authorization layer did the work, not the repository).
- Role set is frozen: assert `CurrentUser.roles` is a `frozenset` and mutation attempts fail.
