using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfacMatch.Mcp;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so every log goes to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(ListSettings.FromEnvironment());
builder.Services.AddSingleton<SanctionsListProvider>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ScreeningTools>();

await builder.Build().RunAsync();
