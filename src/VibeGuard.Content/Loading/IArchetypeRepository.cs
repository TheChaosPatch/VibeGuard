namespace VibeGuard.Content.Loading;

/// <summary>
/// Loads every archetype from the content store. Synchronous by design:
/// this is called once from the composition root before the MCP event
/// loop starts. Design spec §5.3 explains why async would be a liability.
/// </summary>
public interface IArchetypeRepository
{
    IReadOnlyList<Archetype> LoadAll();
}
