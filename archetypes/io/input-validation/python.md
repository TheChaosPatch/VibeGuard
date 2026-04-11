---
schema_version: 1
archetype: io/input-validation
language: python
principles_file: _principles.md
libraries:
  preferred: pydantic
  acceptable:
    - attrs
  avoid:
    - name: Hand-rolled dict validation
      reason: Turns every handler into an audit liability.
minimum_versions:
  python: "3.11"
---

# Input Validation — Python

## Library choice
`pydantic` v2 gives you parse-don't-validate by default: a model instance *is* the proof that the input was valid. `attrs` with validators is acceptable if you already use it project-wide.

## Reference implementation
```python
from pydantic import BaseModel, EmailStr, Field, ValidationError

class UserRegistration(BaseModel):
    email: EmailStr = Field(max_length=254)
    password: str = Field(min_length=12, max_length=128)
    age: int = Field(ge=13, le=120)

    model_config = {
        "extra": "forbid",      # unknown fields are a validation failure
        "str_strip_whitespace": True,
    }

def register_handler(payload: dict) -> UserRegistration:
    """Validate and return the domain object, or raise.

    Callers catch ValidationError at the framework layer and
    return 400 with the formatted errors.
    """
    try:
        return UserRegistration.model_validate(payload)
    except ValidationError:
        # Re-raise so the framework maps it to a 400. Don't swallow.
        raise
```

## Language-specific gotchas
- `extra="forbid"` is load-bearing: without it, attackers can pass unexpected fields that later code silently ignores.
- `EmailStr` requires `email-validator` (install as `pydantic[email]`).
- Pydantic coerces types by default (`"42"` → `42`). If you want strict mode, use `model_config = {"strict": True}`.
- Don't pass `dict(request.json)` — pass the original dict directly. `dict()` on a dict is noise, and on a FastAPI request body, FastAPI already validated against this model if you typed the parameter.
- Resist the urge to catch `ValidationError` inside `register_handler` and return a default. Fail closed.

## Tests to write
- Round-trip: valid payload yields a populated `UserRegistration`.
- Each invalid-field variant raises `ValidationError` with a specific loc.
- `extra="forbid"`: unknown keys raise `ValidationError`.
- Boundaries: exact min/max length and age bounds accept.
- Very large payloads (>10x normal size) are rejected before deserialization at the framework level — if not, add a body size limit.
