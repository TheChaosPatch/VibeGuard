namespace VibeGuard.Mcp;

/// <summary>
/// Source-generated logger messages for startup diagnostics. Kept in
/// a partial class so the roslyn source generator can emit strongly
/// typed, allocation-free logging methods (satisfies CA1848).
/// Top-level statements in Program.cs compile into the synthesized
/// Program type, which cannot host partial LoggerMessage declarations,
/// so this companion class lives in its own file.
/// </summary>
internal static partial class StartupLogging
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Critical,
        Message = "VibeGuard failed to load archetype corpus from {Root}")]
    public static partial void CorpusLoadFailed(ILogger logger, string root, Exception ex);
}
