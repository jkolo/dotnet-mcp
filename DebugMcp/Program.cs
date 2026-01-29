using System.CommandLine;
using DebugMcp.Services;
using DebugMcp.Services.Breakpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var rootCommand = new RootCommand("MCP server for debugging .NET applications");

rootCommand.SetAction(async _ =>
{
    var builder = Host.CreateApplicationBuilder([]);

    // Configure logging - log to stderr to keep stdout clean for MCP
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Register debug services
    builder.Services.AddSingleton<IProcessDebugger, ProcessDebugger>();
    builder.Services.AddSingleton<IDebugSessionManager, DebugSessionManager>();

    // Register breakpoint services
    builder.Services.AddSingleton<PdbSymbolCache>();
    builder.Services.AddSingleton<IPdbSymbolReader, PdbSymbolReader>();
    builder.Services.AddSingleton<BreakpointRegistry>();
    builder.Services.AddSingleton<IConditionEvaluator, SimpleConditionEvaluator>();
    builder.Services.AddSingleton<IBreakpointManager, BreakpointManager>();

    // Configure MCP server with stdio transport
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var host = builder.Build();
    await host.RunAsync();
});

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
