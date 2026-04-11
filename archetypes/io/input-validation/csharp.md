---
schema_version: 1
archetype: io/input-validation
language: csharp
principles_file: _principles.md
libraries:
  preferred: FluentValidation
  acceptable:
    - System.ComponentModel.DataAnnotations
  avoid:
    - name: Manual regex in controllers
      reason: Scatters rules across the codebase; impossible to audit.
minimum_versions:
  dotnet: "10.0"
---

# Input Validation — C#

## Library choice
`FluentValidation` keeps validation rules in dedicated classes next to the domain types. `DataAnnotations` is acceptable for simple cases but gets awkward when rules depend on each other or need async work.

## Reference implementation
```csharp
using FluentValidation;

public sealed record UserRegistration(string Email, string Password, int Age);

public sealed class UserRegistrationValidator : AbstractValidator<UserRegistration>
{
    public UserRegistrationValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12)
            .MaximumLength(128);
        RuleFor(x => x.Age)
            .InclusiveBetween(13, 120);
    }
}

public static class RegistrationEndpoint
{
    public static async Task<IResult> Handle(
        UserRegistration request,
        IValidator<UserRegistration> validator,
        IUserService users)
    {
        var result = await validator.ValidateAsync(request);
        if (!result.IsValid)
        {
            return Results.ValidationProblem(result.ToDictionary());
        }
        await users.RegisterAsync(request);
        return Results.Created();
    }
}
```

## Language-specific gotchas
- Minimal APIs will happily bind a `record` from JSON even when fields are missing — `[Required]` or an equivalent `NotEmpty()` rule is still your job.
- Prefer records over classes for DTOs so you get value equality and immutability for free.
- Don't register a validator as a singleton if it depends on scoped services (e.g., a database context). Use `AddValidatorsFromAssemblyContaining<T>()` so FluentValidation picks the right lifetime.
- Set `MaximumLength` on every string, always. Unbounded strings are a DoS vector.

## Tests to write
- Round-trip: valid request validates cleanly.
- Each invalid-field variant produces a specific error message keyed by field name.
- Boundary: values exactly at min/max length and age bounds validate.
- Malicious: oversized strings (>254 for email, >128 for password) are rejected before they reach the service.
