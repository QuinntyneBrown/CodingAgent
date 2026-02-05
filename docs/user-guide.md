# CodingAgent User Guide

## Table of Contents

- [Introduction](#introduction)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [CLI Reference](#cli-reference)
- [Workflow In Detail](#workflow-in-detail)
- [Sessions](#sessions)
- [Configuration](#configuration)
- [Directory Layout](#directory-layout)
- [Working with the LLM](#working-with-the-llm)
- [Command Reference](#command-reference)
- [Troubleshooting](#troubleshooting)

---

## Introduction

CodingAgent is an offline coding agent that bridges the gap between a web-based LLM (like ChatGPT, Claude, Gemini, etc.) and your local filesystem. It uses a simple text file protocol: the agent writes request files to an **outbox** folder, you paste those into your LLM, and save the LLM's response into an **inbox** folder. The agent then parses the response, executes the commands, and prepares the next request.

This approach works with **any LLM** that accepts text input -- no API keys, no internet connection from the agent, and no vendor lock-in.

### Key Features

- **LLM-agnostic**: Works with any text-based LLM (web UI, API, or local)
- **Offline execution**: The agent itself never connects to the internet
- **Sandboxed**: All file operations are confined to a `workspace/` directory
- **Resumable sessions**: Interrupt and resume at any point
- **Configurable**: Options pattern with `appsettings.json` and environment variable overrides
- **Structured logging**: Microsoft.Extensions.Logging with clean console output
- **Dependency injection**: All services wired via Microsoft.Extensions.DependencyInjection

---

## Installation

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Build

```bash
git clone <repository-url>
cd CodingAgent
dotnet build
```

The compiled binary will be at `src/CodingAgent/bin/Debug/net8.0/CodingAgent.exe` (Windows) or `CodingAgent` (Linux/macOS).

### Run via dotnet

```bash
dotnet run --project src/CodingAgent -- new "Your task here"
```

---

## Quick Start

### 1. Start a New Session

```bash
CodingAgent new "Create a simple calculator app in C#"
```

The agent will:
- Create a new session with a unique 8-character ID
- Write the first outbox file (e.g., `outbox/a1b2c3d4_seq0001.txt`)
- Wait for your response

### 2. Copy Outbox to LLM

Open the outbox file in a text editor, copy its entire contents, and paste it into your LLM chat window.

### 3. Save LLM Response

Copy the LLM's full response and save it as a `.txt` file in the `inbox/` folder. The filename doesn't matter -- any `.txt` file will be detected.

### 4. Watch the Agent Process

The agent will automatically:
- Detect the new file in `inbox/`
- Parse the command blocks from the LLM response
- Execute each command (create files, run commands, etc.)
- Display results in the console
- Move the processed file to `inbox/processed/`
- Write the next outbox file with results

### 5. Repeat

Continue the copy-paste loop until the LLM sends a `[DONE]` command.

---

## CLI Reference

CodingAgent uses [System.CommandLine](https://github.com/dotnet/command-line-api) with the file-per-command pattern.

### Commands

```
CodingAgent [command] [options]

Commands:
  new <task>    Start a new coding session
  resume        Resume the latest incomplete session

Options:
  --version       Show version information
  -?, -h, --help  Show help and usage information
```

### `new` Command

```bash
# Single quoted argument
CodingAgent new "Create a REST API with authentication"

# Multiple words (joined automatically)
CodingAgent new Create a REST API with authentication
```

### `resume` Command

```bash
CodingAgent resume
```

Finds the most recently updated incomplete session and resumes from where it left off.

---

## Workflow In Detail

### Step-by-Step Flow

```
You                         CodingAgent                    LLM (Web UI)
 |                               |                              |
 |-- CodingAgent new "task" ---->|                              |
 |                               |-- write outbox file          |
 |<-- "outbox ready" ------------|                              |
 |                               |                              |
 |-- copy outbox contents ------>|                              |
 |                               |                     paste--->|
 |                               |                   <--respond-|
 |-- save response to inbox/ --->|                              |
 |                               |                              |
 |                               |-- detect inbox file          |
 |                               |-- parse commands             |
 |                               |-- execute commands           |
 |<-- show results --------------|                              |
 |                               |-- write next outbox          |
 |                               |                              |
 |           ... repeat until [DONE] ...                        |
```

### What's in an Outbox File

Each outbox file contains four sections:

1. **HEADER**: Session ID, sequence number, and task description
2. **PROTOCOL**: Full instructions for the LLM (command syntax, rules)
3. **CONTEXT**: Workspace file listing, previous command results, and any requested file contents
4. **PROMPT**: The user's task (first round) or continuation instructions (subsequent rounds)

### What's in an Inbox File

The inbox file is the raw LLM response containing one or more command blocks using the protocol syntax. Any text outside command blocks is ignored.

---

## Sessions

### Session Lifecycle

1. **Created**: `CodingAgent new "task"` creates a session with a unique 8-character hex ID
2. **Active**: The session loops through outbox-write / inbox-read cycles
3. **Complete**: When the LLM sends `[DONE]`, the session is marked complete

### Session Persistence

Sessions are saved as JSON in `sessions/{sessionId}.json`. Session writes are atomic (temp file + rename).

### Resuming a Session

```bash
CodingAgent resume
```

The agent will find the most recently updated incomplete session, check if the current outbox file already exists (skip re-writing if so), and resume waiting for inbox files.

---

## Configuration

CodingAgent uses the Microsoft.Extensions.Options pattern. Configuration is loaded from multiple sources (later sources override earlier):

1. Default values in `AgentOptions`
2. `appsettings.json` at the repo root
3. Environment variables (prefix: `Agent__`)

### appsettings.json

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

### Available Options

| Option | Default | Description |
|--------|---------|-------------|
| `WorkspaceDir` | `workspace` | Directory name for generated code |
| `InboxDir` | `inbox` | Directory name for LLM responses |
| `OutboxDir` | `outbox` | Directory name for agent requests |
| `SessionsDir` | `sessions` | Directory name for session state |
| `CommandTimeoutSeconds` | `30` | Shell command timeout in seconds |
| `MaxOutputLength` | `4000` | Maximum characters of command output to capture |

### Environment Variable Overrides

Use double-underscore (`__`) as the section separator:

```bash
# Set command timeout to 60 seconds
export Agent__CommandTimeoutSeconds=60

# Change workspace directory
export Agent__WorkspaceDir=my-workspace
```

---

## Directory Layout

```
CodingAgent/
  appsettings.json    Configuration file
  inbox/              Drop LLM response .txt files here
    processed/        Processed inbox files are moved here
  outbox/             Agent writes request files here
  workspace/          All generated code lives here
  sessions/           Session state JSON files
  src/
    CodingAgent/      Source code
```

| Directory | Read/Write | Purpose |
|-----------|-----------|---------|
| `inbox/` | **You** write here | Save LLM responses as `.txt` files |
| `inbox/processed/` | Agent writes here | Processed inbox files are archived |
| `outbox/` | Agent writes here | Copy these files to paste into LLM |
| `workspace/` | Agent writes here | All created/edited files live here |
| `sessions/` | Agent writes here | Session state persistence |

---

## Working with the LLM

### Tips for Best Results

1. **Paste the entire outbox file** -- don't truncate it
2. **Use a long-context LLM** -- outbox files can grow large
3. **One response per file** -- save each LLM response as a separate `.txt` file
4. **Wait for processing** -- don't drop a new inbox file until the agent writes the next outbox
5. **Let the LLM iterate** -- complex tasks take multiple rounds

### Supported LLMs

CodingAgent works with any LLM that can follow structured instructions: ChatGPT, Claude, Gemini, local models via Ollama/LM Studio, etc.

---

## Command Reference

### CREATE_FILE

```
[CREATE_FILE path="src/Calculator.cs"]
using System;
public class Calculator { ... }
[/CREATE_FILE]
```

### EDIT_FILE

```
[EDIT_FILE path="src/Calculator.cs" start_line="5" end_line="5"]
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
[/EDIT_FILE]
```

Lines are 1-indexed and inclusive.

### DELETE_FILE

```
[DELETE_FILE path="src/OldFile.cs"]
```

### READ_FILE

```
[READ_FILE path="src/Calculator.cs"]
```

Contents appear in the next outbox file.

### RUN_COMMAND

```
[RUN_COMMAND]
dotnet build
[/RUN_COMMAND]
```

Runs with configurable timeout. stdout/stderr captured.

### MESSAGE

```
[MESSAGE]
Explanation text for the user.
[/MESSAGE]
```

### DONE

```
[DONE]
Summary of completed work.
[/DONE]
```

---

## Troubleshooting

### "No incomplete session found to resume"

All sessions are complete or none exist. Start a new session with `CodingAgent new "task"`.

### Agent doesn't detect the inbox file

- Ensure the file has a `.txt` extension
- Ensure it's in `inbox/` directly, not a subdirectory
- Ensure the file is fully written before the agent reads it

### "No commands found in response"

The LLM response didn't contain valid command blocks. Paste the outbox file again and ask the LLM to respond with commands.

### Path traversal rejected

The LLM tried to access a file outside the workspace. The error is reported to the LLM in the next outbox so it can self-correct.

### Command timed out

Configurable via `Agent:CommandTimeoutSeconds` in appsettings.json (default: 30s).
