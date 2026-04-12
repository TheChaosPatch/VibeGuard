---
schema_version: 1
archetype: persistence/orm-security
language: python
principles_file: _principles.md
libraries:
  preferred: SQLAlchemy
  acceptable:
    - Django ORM
  avoid:
    - name: Assigning request.json directly to model attributes
      reason: Mass assignment. Allowlist fields explicitly.
    - name: session.execute(text(f"...{user_input}..."))
      reason: SQL injection through the ORM's raw escape hatch.
minimum_versions:
  python: "3.10"
---

# ORM Security — Python

## Library choice
`SQLAlchemy` 2.x (ORM or Core) is the default. Its `Mapped[]` declarative syntax separates the column model from any API schema. For Django projects, the Django ORM is acceptable — use `ModelForm` with explicit `fields` (never `exclude`), and never serialize model instances directly with `model_to_dict` unless you control the field list. In both ORMs, raw SQL via `text()` or `raw()` drops you out of the safe surface and requires manual parameterization.

## Reference implementation
```python
from __future__ import annotations
from dataclasses import dataclass
from uuid import uuid4
from sqlalchemy import select
from sqlalchemy.orm import Session, Mapped, mapped_column, DeclarativeBase

class Base(DeclarativeBase):
    pass

class UserEntity(Base):
    __tablename__ = "users"
    id: Mapped[str] = mapped_column(primary_key=True)
    email: Mapped[str] = mapped_column(unique=True)
    display_name: Mapped[str]
    is_admin: Mapped[bool] = mapped_column(default=False)  # never from client
    password_hash: Mapped[str] = mapped_column(default="")

@dataclass(frozen=True, slots=True)
class CreateUserCommand:
    email: str
    display_name: str

@dataclass(frozen=True, slots=True)
class UserResponse:
    id: str
    email: str
    display_name: str

_MAX_PAGE = 100

class UserRepository:
    def __init__(self, session: Session) -> None:
        self._session = session

    def create(self, cmd: CreateUserCommand) -> UserResponse:
        entity = UserEntity(id=str(uuid4()), email=cmd.email, display_name=cmd.display_name)
        self._session.add(entity)
        self._session.flush()
        return UserResponse(id=entity.id, email=entity.email, display_name=entity.display_name)

    def list_users(self, limit: int = 20) -> list[UserResponse]:
        limit = min(max(limit, 1), _MAX_PAGE)
        stmt = select(UserEntity).order_by(UserEntity.email).limit(limit)
        return [UserResponse(id=u.id, email=u.email, display_name=u.display_name)
                for u in self._session.scalars(stmt)]
```

## Language-specific gotchas
- `User(**request.json)` passes every key from the request body into the model constructor, including `is_admin` and `password_hash`. Unpack only the fields you expect: `User(email=data["email"], display_name=data["display_name"])`.
- Django's `ModelForm(fields="__all__")` or `exclude=["password_hash"]` is a denylist that rots. Use `fields=["email", "display_name"]` — explicit allowlist.
- `session.execute(text(f"SELECT * FROM users WHERE email = '{email}'"))` is injection through SQLAlchemy. Use `text("... WHERE email = :email")` with `{"email": email}`.
- SQLAlchemy's `relationship()` with `lazy="select"` (the default) fires a query per access. In a list endpoint, serializing 100 users with a lazy `orders` relationship produces 101 queries. Use `lazy="raise"` and load relationships explicitly with `joinedload()` or `selectinload()` when needed.
- Returning a SQLAlchemy model instance from a Flask/FastAPI endpoint relies on the serializer to decide which fields to include. Pydantic's `from_attributes=True` will read every public attribute. Always map to a response dataclass or Pydantic model with only the intended fields.
- `jsonify(user.__dict__)` dumps SQLAlchemy internal state (`_sa_instance_state`) and every loaded attribute. Never serialize ORM instances directly.

## Tests to write
- Mass assignment: POST `{"email": "a@b.com", "display_name": "A", "is_admin": true}` — assert `is_admin` is `False` on the created entity.
- Response shape: assert the response dict contains only `id`, `email`, `display_name` — no `password_hash`, no `is_admin`.
- Pagination cap: `list_users(limit=999999)` returns at most `_MAX_PAGE` rows.
- Raw SQL parameterization: if any code path uses `text()`, call it with `"'; DROP TABLE users--"` and assert no error and no dropped table.
- Lazy-load safety: access a relationship on a detached entity with `lazy="raise"` — assert `sqlalchemy.exc.InvalidRequestError`.
