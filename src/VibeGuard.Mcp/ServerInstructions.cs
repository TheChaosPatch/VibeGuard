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
        VibeGuard is a curated engineering guidance server covering security, architecture, and operational safety for backend and systems code. Its corpus spans authentication, cryptography, injection defense, input handling, HTTP security, memory safety, concurrency, logging, and dependency management. When an archetype applies, treat its principles as authoritative.

        Workflow: Before writing any non-trivial function, class, or module, call 'prep' with a short natural-language description of the task and the target language. Then call 'consult' on the top relevant archetypes and write your implementation honoring the returned principles and avoiding the documented anti-patterns. Some archetypes are language-agnostic — they return architectural guidance without code examples; others include language-specific reference implementations, library recommendations, and gotchas.

        Topics that warrant a prep call: authentication, authorization, sessions, passwords, OAuth/OIDC, MFA, cryptography, key management, TLS configuration, random number generation, SQL/ORM queries, input validation, deserialization, file uploads, command execution, path traversal, SSRF, XSS, CSRF, CORS, security headers, error handling, logging, audit trails, secrets management, race conditions, buffer overflows, use-after-free (C/Rust unsafe code), and dependency/supply-chain security.

        Skip only for trivial edits (renames, formatting, one-liners) or work clearly outside scope (pure frontend without server interaction, config files, documentation). When in doubt, call 'prep' — it is cheap and deterministic, and returns an empty result when nothing applies.
        """;
}
