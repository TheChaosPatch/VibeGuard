using GuardCode.Content.Indexing;

namespace GuardCode.Content.Services;

/// <summary>
/// Typed response from <see cref="IPrepService.Prep"/>.
/// Serialized as the MCP tool response body in <c>PrepTool</c>.
/// </summary>
public sealed record PrepResult(IReadOnlyList<PrepMatch> Matches);
