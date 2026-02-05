# CodingAgent

An offline coding agent that communicates with a web-based LLM through text files. You paste outbox files into your LLM chat, save the response back to the inbox folder, and the agent executes the commands automatically.

## How It Works

1. Run `CodingAgent new "Create a calculator app"`
2. The agent writes an outbox file with the task and protocol instructions
3. Copy the outbox file contents into your LLM chat
4. Save the LLM response as a `.txt` file in the `inbox/` folder
5. The agent detects the file, parses command blocks, and executes them against `workspace/`
6. The agent writes the next outbox file with execution results
7. Repeat until the LLM sends `[DONE]`

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

```
dotnet build
```

## Usage

```
# Start a new session
CodingAgent new "Build a REST API with user authentication"

# Resume the latest incomplete session
CodingAgent resume

# Show help
CodingAgent --help
```

## Configuration

Settings are loaded from `appsettings.json` at the repo root and can be overridden with environment variables.

```json
{
  "Agent": {
    "WorkspaceDir": "workspace",
    "InboxDir": "inbox",
    "OutboxDir": "outbox",
    "SessionsDir": "sessions",
    "CommandTimeoutSeconds": 30,
    "MaxOutputLength": 4000
  }
}
```

Environment variable override example: `Agent__CommandTimeoutSeconds=60`

## Project Structure

```
src/CodingAgent/
  Program.cs                    Entry point, host builder, CLI setup
  Protocol.cs                   Protocol constants and LLM instructions
  PlainConsoleFormatter.cs      Clean console log formatter
  Commands/
    NewSessionCommand.cs        "new" subcommand (System.CommandLine)
    ResumeCommand.cs            "resume" subcommand (System.CommandLine)
  Configuration/
    AgentOptions.cs             Options pattern configuration
  Models/
    Session.cs                  Session state model
    CommandResult.cs            Command execution result
    ExecutionOutcome.cs         Aggregated execution results
    ParsedCommand.cs            Parsed command type hierarchy
  Services/
    Agent.cs                    Main orchestration loop
    CommandExecutor.cs          Execute commands against workspace
    CommandParser.cs            Parse LLM responses into commands
    SessionStore.cs             Session JSON persistence
    OutboxBuilder.cs            Outbox message assembly
```

The following directories are created at runtime:

| Directory | Purpose |
|-----------|---------|
| `inbox/` | Drop LLM response `.txt` files here |
| `outbox/` | Agent writes request files here |
| `workspace/` | Generated code lives here |
| `sessions/` | Session state (JSON) |

## Technology Stack

- **.NET 8** console application
- **System.CommandLine** for CLI parsing (file-per-command pattern)
- **Microsoft.Extensions.Hosting** for dependency injection, logging, and configuration
- **Microsoft.Extensions.Options** for strongly-typed configuration
- **Microsoft.Extensions.Logging** with custom plain console formatter

## Protocol Commands

The LLM responds with command blocks that the agent parses and executes:

| Command | Description |
|---------|-------------|
| `[CREATE_FILE path="..."]...[/CREATE_FILE]` | Create or overwrite a file |
| `[EDIT_FILE path="..." start_line="N" end_line="M"]...[/EDIT_FILE]` | Replace a range of lines |
| `[DELETE_FILE path="..."]` | Delete a file |
| `[READ_FILE path="..."]` | Request file contents in the next outbox |
| `[RUN_COMMAND]...[/RUN_COMMAND]` | Execute a shell command |
| `[MESSAGE]...[/MESSAGE]` | Display a message to the user |
| `[DONE]...[/DONE]` | Signal task completion |

## Security

- All file operations are sandboxed to the `workspace/` directory
- Path traversal attempts (e.g., `../`) are rejected
- Shell commands run with `workspace/` as the working directory
- Process execution has a configurable timeout (default 30s)
