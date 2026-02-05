using System.CommandLine;
using CodingAgent;
using CodingAgent.Commands;
using CodingAgent.Configuration;
using CodingAgent.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

// Resolve the repo root (parent of src/)
var basePath = FindRepoRoot(AppDomain.CurrentDomain.BaseDirectory)
    ?? Directory.GetCurrentDirectory();

// Build host with DI, Logging, and Configuration
var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

// Logging: use plain formatter for clean CLI output
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = PlainConsoleFormatter.FormatterName)
    .AddConsoleFormatter<PlainConsoleFormatter, ConsoleFormatterOptions>();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Configuration: load repo-root appsettings.json, then inject basePath
var settingsFile = Path.Combine(basePath, "appsettings.json");
if (File.Exists(settingsFile))
    builder.Configuration.AddJsonFile(settingsFile, optional: true, reloadOnChange: false);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    [$"{AgentOptions.SectionName}:BasePath"] = basePath
});

// Register services
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<OutboxBuilder>();
builder.Services.AddSingleton<CommandParser>();
builder.Services.AddSingleton<CommandExecutor>();
builder.Services.AddSingleton<Agent>();

using var host = builder.Build();

// Build CLI with System.CommandLine
var rootCommand = new RootCommand("CodingAgent - Offline coding agent using text file protocol");
rootCommand.AddCommand(NewSessionCommand.Create(host.Services));
rootCommand.AddCommand(ResumeCommand.Create(host.Services));

return await rootCommand.InvokeAsync(args);

// --- Helpers ---

static string? FindRepoRoot(string startDir)
{
    var dir = startDir;
    for (int i = 0; i < 10; i++)
    {
        if (Directory.Exists(Path.Combine(dir, "src")))
            return dir;
        var parent = Directory.GetParent(dir);
        if (parent == null) break;
        dir = parent.FullName;
    }
    return null;
}
