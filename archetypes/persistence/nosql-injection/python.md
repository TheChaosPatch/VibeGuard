---
schema_version: 1
archetype: persistence/nosql-injection
language: python
principles_file: _principles.md
libraries:
  preferred: pymongo
  acceptable:
    - motor
    - redis-py
  avoid:
    - name: raw dict from request JSON as filter
      reason: Passing Flask/FastAPI request JSON directly as a PyMongo filter allows operator injection via $where, $ne, and other BSON operators.
minimum_versions:
  python: "3.12"
---

# NoSQL Injection Defense — Python

## Library choice
`pymongo` is the standard synchronous MongoDB driver. `motor` wraps it for async contexts (FastAPI, asyncio). Both expose the same dict-based filter API, which means injection prevention is the application's responsibility: you must validate that each field value is a scalar of the expected type before using it in a filter. For Redis, `redis-py` uses strongly-typed command methods — the injection surface is limited to key construction.

## Reference implementation
```python
from __future__ import annotations
from pymongo.collection import Collection
from pymongo import ASCENDING
import re

_SORT_FIELDS = frozenset({"email", "created_at"})
_USER_ID_RE = re.compile(r"^[a-f0-9]{24}$")


class UserRepository:
    def __init__(self, collection: Collection) -> None:
        self._col = collection

    def find_by_email(self, email: str) -> dict | None:
        if not isinstance(email, str):
            raise TypeError("email must be a plain string")
        # Scalar string value — cannot carry a BSON operator.
        return self._col.find_one({"email": email}, {"_id": 0})

    def find_by_id(self, user_id: str) -> dict | None:
        if not _USER_ID_RE.fullmatch(user_id):
            raise ValueError("Invalid user_id format")
        from bson import ObjectId
        return self._col.find_one({"_id": ObjectId(user_id)}, {"_id": 0})

    def list_sorted(self, sort_field: str, limit: int = 50) -> list[dict]:
        if sort_field not in _SORT_FIELDS:
            raise ValueError(f"Unknown sort field: {sort_field!r}")
        cursor = self._col.find({}, {"_id": 0}).sort(sort_field, ASCENDING).limit(limit)
        return list(cursor)
```

## Language-specific gotchas
- `request.json` in Flask and `await request.json()` in FastAPI return plain Python dicts. A client can send `{"email": {"$ne": ""}}`, and PyMongo will execute it as a query with the `$ne` operator. Always check `isinstance(value, str)` (or the expected scalar type) before building the filter.
- `motor` is async but has the same injection surface as `pymongo`. The same validation rules apply.
- Never pass `**request.json()` as keyword arguments to a filter — if the JSON contains unexpected keys they silently become filter fields.
- For `redis-py`, build keys with validated components: `f"session:{user_id}"` where `user_id` has been matched against `_USER_ID_RE`.
- Avoid `$where` entirely; pymongo does not block it by default. Never construct or accept it from user input.

## Tests to write
- Call `find_by_email({"$ne": ""})` and assert `TypeError` is raised before a database call occurs.
- Call `list_sorted("password")` and assert `ValueError` — field not in allowlist.
- Integration test: insert a document, query by exact email string, assert exactly one result returned.
- Redis key test: call the key-building function with `*` and assert `ValueError` before the Redis command fires.
