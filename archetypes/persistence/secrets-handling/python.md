---
schema_version: 1
archetype: persistence/secrets-handling
language: python
principles_file: _principles.md
libraries:
  preferred: pydantic-settings
  acceptable:
    - python-dotenv
    - boto3
  avoid:
    - name: os.environ scattered across the codebase
      reason: Direct env access defeats the one-provider principle.
    - name: Hardcoded string constants
      reason: Secrets in source code are secrets on GitHub ten seconds later.
minimum_versions:
  python: "3.11"
---

# Secrets Handling — Python

## Library choice
`pydantic-settings` is the default. It loads values from environment variables (including a `.env` file for development), validates them against a typed model at construction time, and fails closed if a required field is missing. For fetching secrets from cloud providers in production, layer `boto3` / `google-cloud-secret-manager` / `azure-keyvault-secrets` underneath the same settings object — the model is the single seam every consumer reads from. `python-dotenv` alone is fine for local development but insufficient for production because it performs no validation.

## Reference implementation
```python
from functools import lru_cache
from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict


class StripeSettings(BaseSettings):
    """Stripe credentials. Values come from env or a secrets provider."""

    model_config = SettingsConfigDict(
        env_prefix="STRIPE_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="forbid",
    )

    secret_key: SecretStr = Field(
        ..., description="Stripe secret API key (sk_live_... or sk_test_...)"
    )
    webhook_signing_secret: SecretStr = Field(
        ..., description="Stripe webhook signing secret (whsec_...)"
    )


@lru_cache(maxsize=1)
def get_stripe_settings() -> StripeSettings:
    """Resolve once per process; fails the process if either secret is missing."""
    return StripeSettings()  # type: ignore[call-arg]


class StripeClient:
    def __init__(self, settings: StripeSettings) -> None:
        # get_secret_value() is the only place the raw string exists.
        self._key = settings.secret_key.get_secret_value()

    def __repr__(self) -> str:
        return "StripeClient(key=<redacted>)"
```

## Language-specific gotchas
- `SecretStr` is the important type here. It wraps the raw value so that `repr()`, `str()`, and default serialization return `'**********'` instead of the secret. Only `get_secret_value()` exposes the underlying string. This single primitive eliminates a whole class of "we logged the config object" incidents.
- `pydantic-settings` validates on construction — a missing `STRIPE_SECRET_KEY` raises `ValidationError` at `get_stripe_settings()`, not at first use. Call the getter early in startup (from `main()` or an ASGI lifespan hook) so the process fails loudly.
- `extra="forbid"` rejects unknown environment variables prefixed with `STRIPE_`. It catches typos (`STRIPE_SECRET_KEY` vs `STRIPE_SECERT_KEY`) that would otherwise surface as a confusing missing-field error.
- Never call `get_secret_value()` outside the layer that actually makes the outbound request. Passing bare strings around defeats the whole point of `SecretStr`.
- FastAPI and Django both have their own config conventions; `pydantic-settings` plays nicely with both and gives you a single typed seam regardless of the framework's defaults.

## Tests to write
- Missing-secret startup: unset `STRIPE_SECRET_KEY` in the test environment and assert that `get_stripe_settings()` raises `ValidationError` naming the missing field.
- Redaction: `repr(settings.secret_key)` returns `"SecretStr('**********')"`, never the raw value. Regression-test this because it's easy to break by subclassing `SecretStr`.
- Round-trip: instantiate `StripeSettings` from an in-memory env dict and confirm `get_secret_value()` returns the original string.
- Environment isolation: assert that the test suite clears Stripe env variables between tests so a leftover value from one test cannot satisfy another.
