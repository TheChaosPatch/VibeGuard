using VibeGuard.Content;
using VibeGuard.Content.Indexing;
using VibeGuard.Content.Loading;
using VibeGuard.Content.Services;
using VibeGuard.Content.Validation;
using VibeGuard.Mcp;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

// Stdio is reserved for the MCP protocol. Serilog is the sole logging
// provider, and every log event is routed to stderr so nothing pollutes
// the MCP wire format on stdout.
builder.Logging.ClearProviders();
builder.Services.AddSerilog(lc => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose));

// Resolve the archetypes root once at startup. Precedence:
// 1. Environment variable VIBEGUARD_ARCHETYPES_ROOT (absolute or relative to cwd)
// 2. appsettings.json "VibeGuard:ArchetypesRoot"
// 3. "archetypes" next to the executable
var configured = Environment.GetEnvironmentVariable("VIBEGUARD_ARCHETYPES_ROOT")
    ?? builder.Configuration["VibeGuard:ArchetypesRoot"]
    ?? "archetypes";
var archetypesRoot = Path.IsPathRooted(configured)
    ? configured
    : Path.GetFullPath(configured, AppContext.BaseDirectory);

// Drafts are hidden from the default active corpus. Contributors testing
// their own in-progress archetypes opt in with VIBEGUARD_INCLUDE_DRAFTS=1
// (any non-empty value enables it, to match the stdlib convention for
// boolean env flags).
var includeDrafts = !string.IsNullOrEmpty(
    Environment.GetEnvironmentVariable("VIBEGUARD_INCLUDE_DRAFTS"));

// Resolve the supported language set once at startup. Precedence mirrors
// the archetypes root:
// 1. VIBEGUARD_SUPPORTED_LANGUAGES (env, comma-separated)
// 2. appsettings.json "VibeGuard:SupportedLanguages" (string array)
// 3. SupportedLanguageSet.Default() — csharp, python, c, go, rust
// A malformed value aborts startup with the same fail-loud contract as
// a broken corpus: it is much better to fail at boot than to pretend
// everything is fine and serve confusing errors per call.
var supportedLanguages = ResolveSupportedLanguages(builder.Configuration);

builder.Services
    .AddSingleton(supportedLanguages)
    .AddSingleton<IArchetypeRepository>(sp => new FileSystemArchetypeRepository(
        archetypesRoot,
        includeDrafts,
        sp.GetRequiredService<SupportedLanguageSet>()))
    .AddSingleton<IArchetypeIndex>(sp =>
    {
        var repo = sp.GetRequiredService<IArchetypeRepository>();
        return KeywordArchetypeIndex.Build(repo.LoadAll());
    })
    .AddSingleton<IPrepService, PrepService>()
    .AddSingleton<IConsultationService, ConsultationService>();

builder.Services
    .AddMcpServer(opts => opts.ServerInstructions = ServerInstructions.Text)
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();

// Force-load the index synchronously before entering the event loop so that
// any content validation error aborts startup with a clear stderr message
// instead of surfacing during the first MCP call. The host is disposed on
// the failure path so the console logger provider flushes its background
// queue before the process exits.
try
{
    _ = host.Services.GetRequiredService<IArchetypeIndex>();
}
catch (Exception ex) when (
    ex is IOException
    or UnauthorizedAccessException
    or ArgumentException
    or ArchetypeLoadException
    or FrontmatterParseException
    or ArchetypeValidationException)
{
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("VibeGuard.Startup");
    StartupLogging.CorpusLoadFailed(logger, archetypesRoot, ex);
    await ((IAsyncDisposable)host).DisposeAsync().ConfigureAwait(false);
    return 1;
}

await host.RunAsync().ConfigureAwait(false);
return 0;

static SupportedLanguageSet ResolveSupportedLanguages(IConfiguration configuration)
{
    var fromEnv = Environment.GetEnvironmentVariable("VIBEGUARD_SUPPORTED_LANGUAGES");
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        var entries = fromEnv.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new SupportedLanguageSet(entries);
    }

    var fromConfig = configuration.GetSection("VibeGuard:SupportedLanguages").Get<string[]>();
    if (fromConfig is { Length: > 0 })
    {
        return new SupportedLanguageSet(fromConfig);
    }

    return SupportedLanguageSet.Default();
}
