# Architecture Overview

## Table of Contents

- [System Architecture](#system-architecture)
- [Component Design](#component-design)
- [Dependency Injection](#dependency-injection)
- [Configuration](#configuration)
- [Data Flow](#data-flow)
- [Design Patterns](#design-patterns)
- [Diagrams](#diagrams)

---

## System Architecture

CodingAgent follows a **pipeline architecture** where data flows through a series of processing stages:

```
CLI Args -> System.CommandLine -> DI Container -> Agent -> Outbox -> [User] -> Inbox -> Parser -> Executor -> Loop
```

The application is built on three Microsoft.Extensions pillars:

| Pillar | Purpose |
|--------|---------|
| **DependencyInjection** | Wire all services with constructor injection |
| **Logging** | Structured logging via `ILogger<T>` with custom plain formatter |
| **Configuration** | `IOptions<AgentOptions>` from appsettings.json + environment variables |

---

## Component Design

### Program (Entry Point)

Top-level statements that:
1. Resolve the repo root directory
2. Build the `IHost` with all services, logging, and configuration
3. Set up `System.CommandLine` with the `new` and `resume` subcommands
4. Invoke the CLI parser

### Commands (System.CommandLine)

Each CLI command lives in its own file under `Commands/`:

| File | Command | Description |
|------|---------|-------------|
| `NewSessionCommand.cs` | `new <task>` | Resolves `Agent` from DI, calls `StartNewSession` + `Run` |
| `ResumeCommand.cs` | `resume` | Resolves `Agent` from DI, calls `ResumeSession` + `Run` |

Commands receive the `IServiceProvider` and resolve services on invocation.

### Configuration Layer

`AgentOptions` is a POCO class bound to the `Agent` section of configuration:

```csharp
public class AgentOptions
{
    public string BasePath { get; set; }
    public string WorkspaceDir { get; set; } = "workspace";
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxOutputLength { get; set; } = 4000;
    // ... computed paths via properties
}
```

Configuration sources (in override order):
1. Default property values
2. `appsettings.json` at repo root
3. Environment variables (`Agent__Key=Value`)
4. In-memory collection (for computed `BasePath`)

### Services

All business logic lives in `Services/`:

| Service | Injected Dependencies | Responsibility |
|---------|----------------------|----------------|
| `Agent` | `IOptions<AgentOptions>`, `CommandExecutor`, `CommandParser`, `SessionStore`, `OutboxBuilder`, `ILogger<Agent>` | Orchestration loop, session lifecycle, file I/O coordination |
| `CommandExecutor` | `IOptions<AgentOptions>`, `ILogger<CommandExecutor>` | Execute commands against sandboxed workspace |
| `CommandParser` | (none) | Parse raw text into typed command objects |
| `SessionStore` | `IOptions<AgentOptions>`, `ILogger<SessionStore>` | JSON persistence with atomic writes |
| `OutboxBuilder` | `IOptions<AgentOptions>` | Assemble outbox files from session state |

### Models

Data transfer objects in `Models/`:

| Model | Purpose |
|-------|---------|
| `Session` | Mutable session state (ID, task, sequence, results, read requests) |
| `CommandResult` | Result of a single command execution |
| `ExecutionOutcome` | Aggregated results from processing all commands in one inbox file |
| `ParsedCommand` | Abstract base + 8 concrete record types for each command kind |

### Protocol

Static class with version constant, section markers, and the full LLM instruction text.

### PlainConsoleFormatter

Custom `ConsoleFormatter` that strips log level, category, and timestamp from console output. Produces clean CLI output like:

```
New session: a1b2c3d4
Task: Create a calculator app
```

Instead of the default noisy format:

```
info: CodingAgent.Services.Agent[0] New session: a1b2c3d4
```

---

## Dependency Injection

All services are registered as singletons in `Program.cs`:

```csharp
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<OutboxBuilder>();
builder.Services.AddSingleton<CommandParser>();
builder.Services.AddSingleton<CommandExecutor>();
builder.Services.AddSingleton<Agent>();
```

The DI container manages the object graph:

```
Agent
  ├── IOptions<AgentOptions>
  ├── CommandExecutor
  │     ├── IOptions<AgentOptions>
  │     └── ILogger<CommandExecutor>
  ├── CommandParser
  ├── SessionStore
  │     ├── IOptions<AgentOptions>
  │     └── ILogger<SessionStore>
  ├── OutboxBuilder
  │     └── IOptions<AgentOptions>
  └── ILogger<Agent>
```

---

## Configuration

### Options Pattern Flow

```
appsettings.json ──┐
                   ├──> IConfiguration ──> IOptions<AgentOptions> ──> Services
env variables ─────┘
```

`AgentOptions` provides computed properties for full paths:

```csharp
public string WorkspacePath => Path.Combine(BasePath, WorkspaceDir);
public string InboxPath => Path.Combine(BasePath, InboxDir);
```

This allows directory names to be configured independently while `BasePath` is always resolved at startup.

---

## Data Flow

### Round-Trip Data Flow

1. `Agent.Run()` increments sequence number
2. `OutboxBuilder.Build()` assembles content from `Session` state + workspace listing
3. Agent writes outbox file atomically (temp + rename)
4. Agent saves session via `SessionStore.Save()`
5. Agent waits for inbox file via `FileSystemWatcher`
6. Agent reads inbox text
7. `CommandParser.Parse()` produces `List<ParsedCommand>`
8. `CommandExecutor.Execute()` processes each command, produces `ExecutionOutcome`
9. Agent updates `Session.LastResults` and `Session.ReadFileRequests`
10. If `[DONE]` received, session is marked complete
11. Loop back to step 1

---

## Design Patterns

### File Per Command (System.CommandLine)

Each CLI command is defined in its own static class under `Commands/`. Each class has a single `Create(IServiceProvider)` method that returns a configured `Command` instance.

### Options Pattern (Microsoft.Extensions)

Configuration is strongly typed via `AgentOptions`, bound from `IConfiguration`, and injected as `IOptions<AgentOptions>`. This provides compile-time safety and centralized defaults.

### Constructor Injection

All services declare their dependencies in constructors. The DI container resolves the full object graph automatically.

### Atomic File Writes

Both outbox and session files use temp-file-then-rename:

```csharp
File.WriteAllText(path + ".tmp", content);
File.Move(path + ".tmp", path, overwrite: true);
```

### Command Pattern

Parsed commands use a type hierarchy (`ParsedCommand` abstract record with concrete subtypes), enabling pattern matching in the executor.

### Result Aggregation

Every command execution returns a `CommandResult`. These are collected in `ExecutionOutcome` and fed back to the LLM, creating a self-correcting feedback loop.

---

## Diagrams

See the `docs/diagrams/` directory for PlantUML diagrams:

- [`class-diagram.puml`](diagrams/class-diagram.puml) - Full class diagram with DI relationships
- [`sequence-new-session.puml`](diagrams/sequence-new-session.puml) - New session startup via System.CommandLine
- [`sequence-main-loop.puml`](diagrams/sequence-main-loop.puml) - Main processing loop
- [`sequence-command-processing.puml`](diagrams/sequence-command-processing.puml) - Command parsing and execution

Render with [PlantUML](https://plantuml.com/) or [plantuml.com/plantuml](https://www.plantuml.com/plantuml/uml/).
