using GuardCode.Content.Indexing;
using GuardCode.Content.Loading;
using GuardCode.Content.Services;
using GuardCode.Content.Validation;
using GuardCode.Mcp;
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
// 1. Environment variable GUARDCODE_ARCHETYPES_ROOT (absolute or relative to cwd)
// 2. appsettings.json "GuardCode:ArchetypesRoot"
// 3. "archetypes" next to the executable
var configured = Environment.GetEnvironmentVariable("GUARDCODE_ARCHETYPES_ROOT")
    ?? builder.Configuration["GuardCode:ArchetypesRoot"]
    ?? "archetypes";
var archetypesRoot = Path.IsPathRooted(configured)
    ? configured
    : Path.GetFullPath(configured, AppContext.BaseDirectory);

// Drafts are hidden from the default active corpus. Contributors testing
// their own in-progress archetypes opt in with GUARDCODE_INCLUDE_DRAFTS=1
// (any non-empty value enables it, to match the stdlib convention for
// boolean env flags).
var includeDrafts = !string.IsNullOrEmpty(
    Environment.GetEnvironmentVariable("GUARDCODE_INCLUDE_DRAFTS"));

builder.Services
    .AddSingleton<IArchetypeRepository>(_ => new FileSystemArchetypeRepository(archetypesRoot, includeDrafts))
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
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GuardCode.Startup");
    StartupLogging.CorpusLoadFailed(logger, archetypesRoot, ex);
    await ((IAsyncDisposable)host).DisposeAsync().ConfigureAwait(false);
    return 1;
}

await host.RunAsync().ConfigureAwait(false);
return 0;
