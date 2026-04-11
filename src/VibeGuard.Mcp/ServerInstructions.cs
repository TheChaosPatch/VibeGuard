namespace VibeGuard.Mcp;

/// <summary>
/// System-prompt text sent to MCP clients during the initialize handshake.
/// Compliant clients (Claude Desktop, Claude Code, Cursor, VS Code) surface
/// this string to the LLM as a system message, which is how VibeGuard gets
/// the model to actually call <c>prep</c> before writing security-sensitive
/// code instead of relying on the end-user to remember.
/// </summary>
/// <remarks>
/// This text intentionally does not re-describe what <c>prep</c> and
/// <c>consult</c> do — the tool descriptions already carry that. Its job
/// is to tell the model <i>when</i> and <i>why</i> to use them.
/// </remarks>
internal static class ServerInstructions
{
    public const string Text =
        """
        VibeGuard is a curated security and architecture guidance server for backend and systems code. When an archetype applies, treat its principles as authoritative.

        Workflow: Before writing any non-trivial function, class, or module — especially anything touching authentication, password handling, session management, input validation, deserialization, database access, cryptography, secrets management, error handling, or external I/O — call the 'prep' tool with a short natural-language description of the task and the target language. Then call 'consult' on the top relevant archetypes to retrieve the full guidance document, and write your implementation in a way that honors its principles and avoids its documented anti-patterns.

        Skip this flow only for trivial edits (renames, formatting, obvious one-liners) or for work clearly outside VibeGuard's scope (UI code, configuration files, documentation, tests for already-specified behavior). When in doubt, call 'prep' — it is cheap and deterministic, and returns an empty result when nothing applies.
        """;
}
