---
schema_version: 1
archetype: persistence/secrets-handling
language: csharp
principles_file: _principles.md
libraries:
  preferred: Microsoft.Extensions.Configuration
  acceptable:
    - Azure.Security.KeyVault.Secrets
    - AWSSDK.SecretsManager
  avoid:
    - name: Hardcoded const string
      reason: Not a library — the universal anti-pattern. Ban in review.
    - name: System.Environment.GetEnvironmentVariable everywhere
      reason: Direct env access scatters secret reads across the codebase.
minimum_versions:
  dotnet: "10.0"
---

# Secrets Handling — C#

## Library choice
`Microsoft.Extensions.Configuration` is the stock abstraction and it composes: a single `IConfiguration` can be layered from `appsettings.json` (non-secret defaults), environment variables (local dev), and a provider like `Azure.Security.KeyVault.Secrets` or `AWSSDK.SecretsManager` (production). Bind the resulting values into a strongly-typed options record via `IOptions<T>` and inject *that* into consumers. Code that needs a secret takes a dependency on an options type, not on the configuration system.

## Reference implementation
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

public sealed record StripeOptions
{
    public const string SectionName = "Stripe";
    public required string SecretKey { get; init; }
    public required string WebhookSigningSecret { get; init; }
}

public static class SecretsRegistration
{
    public static void AddStripeSecrets(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<StripeOptions>()
            .Bind(config.GetSection(StripeOptions.SectionName))
            .Validate(o =>
                !string.IsNullOrWhiteSpace(o.SecretKey)
                && !string.IsNullOrWhiteSpace(o.WebhookSigningSecret),
                "Stripe secrets are missing — set Stripe:SecretKey and " +
                "Stripe:WebhookSigningSecret in your secrets provider.")
            .ValidateOnStart();
    }
}

public sealed class StripeClient(IOptions<StripeOptions> options)
{
    private readonly StripeOptions _opts = options.Value;
    // _opts.SecretKey is now the only place the key lives in this class.
    // Do not expose it via a property, do not log it, do not ToString() it.
}
```

## Language-specific gotchas
- `ValidateOnStart()` is the line that turns a silent misconfiguration into a startup failure. Without it, `IOptions<T>` resolves lazily and the error surfaces on first request.
- Do **not** override `ToString()` on an options record that contains a secret — `record`'s compiler-generated `ToString()` dumps every property, which is how secrets end up in exception-reporting tools. Keep the `required` secret fields on a dedicated type that overrides `ToString()` to return the type name only.
- `IConfiguration` layering order in `Program.cs` matters: the last provider added wins. The secrets provider (Key Vault, Secrets Manager) must come after `appsettings.json` so production values override defaults.
- For local development use `dotnet user-secrets` — it stores values under `%APPDATA%\Microsoft\UserSecrets` per project, outside the repo, and `Microsoft.Extensions.Configuration.UserSecrets` picks them up automatically in Development.
- Never pass a secret as a command-line argument. `Environment.CommandLine` is readable by other processes, it gets into crash dumps, and it lands in shell history.

## Tests to write
- Missing-secret startup: instantiate the host with an empty configuration and assert that `host.StartAsync()` throws with a message naming which secret is missing — proving `ValidateOnStart()` fires.
- Round-trip: bind a `StripeOptions` from an in-memory configuration source and confirm both values land on the record.
- Log hygiene: serialize the options type via `System.Text.Json` (if you do so anywhere) and assert that the serialized output does *not* contain the raw key value — or better, mark the type as non-serializable so the attempt itself fails.
- Provider independence: swap the `IConfiguration` source between tests and production and confirm consumers don't change.
