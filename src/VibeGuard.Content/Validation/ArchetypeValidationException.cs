namespace VibeGuard.Content.Validation;

/// <summary>
/// Thrown when an archetype violates a structural rule from design
/// spec section 4: missing body sections, exceeding line or code budgets,
/// or required-field violations not caught at parse time.
/// </summary>
public sealed class ArchetypeValidationException : Exception
{
    public ArchetypeValidationException() { }
    public ArchetypeValidationException(string message) : base(message) { }
    public ArchetypeValidationException(string message, Exception inner) : base(message, inner) { }
}
