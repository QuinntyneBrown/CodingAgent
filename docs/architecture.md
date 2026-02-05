# Architecture Overview

## Table of Contents

- [System Architecture](#system-architecture)
- [Component Design](#component-design)
- [Data Flow](#data-flow)
- [Design Patterns](#design-patterns)
- [Diagrams](#diagrams)

---

## System Architecture

CodingAgent follows a **pipeline architecture** where data flows through a series of processing stages:

```
User Input -> Session Setup -> Outbox Generation -> [User Copy/Paste] -> Inbox Detection -> Command Parsing -> Command Execution -> Result Collection -> Loop
```

The application is organized into five source files, each with a clear responsibility:

| File | Responsibility |
|------|---------------|
| `Program.cs` | Entry point, CLI argument parsing, repo root detection |
| `Agent.cs` | Orchestration loop, session lifecycle, file I/O coordination |
| `Protocol.cs` | Data models (Session, CommandResult), protocol constants, outbox construction, session persistence |
| `CommandParser.cs` | Parse raw LLM text into typed command objects |
| `CommandExecutor.cs` | Execute commands against the sandboxed workspace |

---

## Component Design

### Program (Entry Point)

Handles three modes:
- **`--help`**: Print usage and exit
- **`--resume`** or no args: Load and resume the latest incomplete session
- **Task args**: Join arguments into a prompt and start a new session

Locates the repo root by walking up from the binary's base directory to find the `src/` folder.

### Agent (Orchestrator)

The central coordinating component. Manages:
- **Directory setup**: Creates inbox, outbox, workspace, sessions, and processed directories
- **Session lifecycle**: Create new sessions, resume existing ones
- **Main loop**: The outbox-wait-inbox-process cycle
- **File watching**: Uses `FileSystemWatcher` + `ManualResetEventSlim` for efficient inbox monitoring
- **File stability**: Retries exclusive file open to ensure complete writes before reading

### Protocol Layer

Contains all data models and protocol logic:

- **`Protocol`** (static): Version constant, section markers, LLM instruction text
- **`Session`**: Mutable model tracking session state (ID, task, sequence, results, read requests)
- **`CommandResult`**: Immutable result of a single command execution
- **`SessionStore`** (static): JSON serialization/deserialization with atomic writes
- **`OutboxBuilder`** (static): Assembles outbox files from session state and workspace contents

### CommandParser

A stateless line-by-line scanner that converts raw text into typed command objects:
- Uses regex for attribute extraction (`key="value"` patterns)
- Handles both block commands (opening + body + closing) and self-closing commands
- Produces `ErrorCommand` objects for malformed input rather than throwing exceptions

### CommandExecutor

Executes commands against the filesystem and processes:
- **Path sandboxing**: `ResolveSafePath` prevents directory traversal
- **File operations**: CREATE, EDIT, DELETE with proper error handling
- **READ queuing**: Adds paths to the outcome's read request list
- **Process execution**: Spawns shell processes with timeout and output capture
- **Message display**: Writes to console with color formatting
- Returns an `ExecutionOutcome` containing all results, read requests, and completion status

---

## Data Flow

### Round-Trip Data Flow

1. `Agent.Run()` increments sequence number
2. `OutboxBuilder.Build()` assembles outbox content from `Session` state + workspace directory listing
3. Agent writes outbox file atomically (temp + rename)
4. Agent saves session state via `SessionStore.Save()`
5. Agent waits for inbox file via `FileSystemWatcher`
6. Agent reads inbox file text
7. `CommandParser.Parse()` produces `List<ParsedCommand>`
8. `CommandExecutor.Execute()` processes each command, produces `ExecutionOutcome`
9. Agent updates `Session.LastResults` and `Session.ReadFileRequests`
10. If `[DONE]` was received, session is marked complete
11. Agent saves session state and moves inbox file to `processed/`
12. Loop back to step 1

### File System Data Flow

```
outbox/{id}_seq{N}.txt  -->  (user copies to LLM)
                              (LLM responds)
inbox/*.txt              <--  (user saves response)
inbox/processed/*.txt    <--  (agent archives after processing)
workspace/**             <->  (agent reads/writes per commands)
sessions/{id}.json       <->  (agent reads/writes session state)
```

---

## Design Patterns

### Atomic File Writes

Both outbox and session files use the temp-file-then-rename pattern:

```csharp
File.WriteAllText(path + ".tmp", content);
File.Move(path + ".tmp", path, overwrite: true);
```

This ensures readers never see partially-written files, which is critical for crash resilience.

### Command Pattern

The parsed commands use a type hierarchy (`ParsedCommand` abstract record with concrete subtypes), enabling pattern matching in the executor:

```csharp
switch (cmd)
{
    case CreateFileCommand c: ...
    case EditFileCommand c: ...
    case DoneCommand c: ...
}
```

### Result Aggregation

Rather than throwing exceptions, every command execution returns a `CommandResult` with a success/failure flag and descriptive summary. These are collected in `ExecutionOutcome` and fed back to the LLM via the next outbox, creating a self-correcting feedback loop.

### File Watcher + Reset Event

The inbox detection uses `FileSystemWatcher` for event-driven notification combined with `ManualResetEventSlim` for efficient blocking. A double-check pattern avoids race conditions:

1. Check for existing files
2. Set up watcher
3. Check again (file may have arrived between step 1 and 2)
4. Block until watcher fires

---

## Diagrams

See the `docs/diagrams/` directory for PlantUML diagrams:

- [`class-diagram.puml`](diagrams/class-diagram.puml) - Full class diagram showing all types and relationships
- [`sequence-new-session.puml`](diagrams/sequence-new-session.puml) - Sequence diagram for starting a new session
- [`sequence-main-loop.puml`](diagrams/sequence-main-loop.puml) - Sequence diagram for the main processing loop
- [`sequence-command-processing.puml`](diagrams/sequence-command-processing.puml) - Sequence diagram for command parsing and execution

To render the diagrams, use the [PlantUML](https://plantuml.com/) tool or an online renderer like [plantuml.com/plantuml](https://www.plantuml.com/plantuml/uml/).
