# CodingAgent User Guide

## Table of Contents

- [Introduction](#introduction)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Workflow In Detail](#workflow-in-detail)
- [Sessions](#sessions)
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
- **Zero dependencies**: Pure .NET 8 with no NuGet packages

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

### Optional: Install as a Global Tool

You can also run it directly with `dotnet run`:

```bash
dotnet run --project src/CodingAgent -- "Your task here"
```

---

## Quick Start

### 1. Start a New Session

```bash
CodingAgent "Create a simple calculator app in C#"
```

The agent will:
- Create a new session with a unique 8-character ID
- Write the first outbox file (e.g., `outbox/a1b2c3d4_seq0001.txt`)
- Wait for your response

### 2. Copy Outbox to LLM

Open the outbox file in a text editor, copy its entire contents, and paste it into your LLM chat window.

### 3. Save LLM Response

Copy the LLM's full response and save it as a `.txt` file in the `inbox/` folder. The filename doesn't matter -- any `.txt` file will be detected.

Example: Save as `inbox/response1.txt`

### 4. Watch the Agent Process

The agent will automatically:
- Detect the new file in `inbox/`
- Parse the command blocks from the LLM response
- Execute each command (create files, run commands, etc.)
- Display results in the console
- Move the processed file to `inbox/processed/`
- Write the next outbox file with results

### 5. Repeat

Continue the copy-paste loop until the LLM sends a `[DONE]` command, which signals the task is complete.

---

## Workflow In Detail

### Step-by-Step Flow

```
You                         CodingAgent                    LLM (Web UI)
 |                               |                              |
 |-- run with task ------------->|                              |
 |                               |-- write outbox file -------->|
 |                               |                              |
 |<-- "outbox ready" ------------|                              |
 |                               |                              |
 |-- copy outbox contents ------>|                              |
 |                               |                     paste--->|
 |                               |                              |
 |                               |                   <--respond-|
 |-- save response to inbox/ --->|                              |
 |                               |                              |
 |                               |-- detect inbox file          |
 |                               |-- parse commands             |
 |                               |-- execute commands           |
 |                               |-- display results            |
 |<-- show results --------------|                              |
 |                               |-- write next outbox -------->|
 |                               |                              |
 |           ... repeat until [DONE] ...                        |
```

### What's in an Outbox File

Each outbox file contains four sections:

1. **HEADER**: Session ID, sequence number, and task description
2. **PROTOCOL**: Full instructions for the LLM (command syntax, rules)
3. **CONTEXT**: Workspace file listing, previous command results, and any requested file contents
4. **PROMPT**: The user's task (first round) or continuation instructions (subsequent rounds)

The protocol instructions are included in **every** outbox file so the LLM always has them in context, even if the chat history is lost.

### What's in an Inbox File

The inbox file is the raw LLM response. It should contain one or more command blocks using the protocol syntax (e.g., `[CREATE_FILE]`, `[RUN_COMMAND]`, `[DONE]`). Any text outside command blocks is ignored.

---

## Sessions

### Session Lifecycle

1. **Created**: When you run `CodingAgent "task"`, a new session is created with a unique 8-character hex ID (e.g., `a1b2c3d4`)
2. **Active**: The session loops through outbox-write / inbox-read cycles, incrementing the sequence number each round
3. **Complete**: When the LLM sends `[DONE]`, the session is marked complete

### Session Persistence

Sessions are saved as JSON files in the `sessions/` directory:

```
sessions/
  a1b2c3d4.json
  e5f6a7b8.json
```

Each JSON file contains the session state: ID, task, sequence number, completion status, timestamps, last results, and pending file read requests.

### Resuming a Session

If the agent is interrupted (e.g., Ctrl+C, power loss), resume the latest incomplete session:

```bash
CodingAgent --resume
```

The agent will:
- Find the most recently updated incomplete session
- Check if the current outbox file already exists (skip re-writing if so)
- Resume waiting for inbox files

### Session Idempotency

The agent is designed for safe resume:
- Outbox files are only written if they don't already exist
- Session state is saved atomically (write to temp file, then rename)
- If the agent crashes between writing outbox and processing inbox, the outbox file will still be there on resume

---

## Directory Layout

After running the agent, your project will have these runtime directories:

```
CodingAgent/
  inbox/              # Drop LLM response .txt files here
    processed/        # Processed inbox files are moved here
  outbox/             # Agent writes request files here
  workspace/          # All generated code lives here
  sessions/           # Session state JSON files
  src/
    CodingAgent/      # Source code
```

| Directory | Read/Write | Purpose |
|-----------|-----------|---------|
| `inbox/` | **You** write here | Save LLM responses as `.txt` files |
| `inbox/processed/` | Agent writes here | Processed inbox files are archived here |
| `outbox/` | Agent writes here | Copy these files to paste into LLM |
| `workspace/` | Agent writes here | All created/edited files live here |
| `sessions/` | Agent writes here | Session state persistence |

---

## Working with the LLM

### Tips for Best Results

1. **Paste the entire outbox file** -- don't truncate it. The protocol instructions and context are essential for the LLM to produce correct command blocks.

2. **Use a long-context LLM** -- outbox files can grow large as the workspace grows. Models with 100K+ token context windows work best.

3. **One response per file** -- save each LLM response as a separate `.txt` file. Don't combine multiple responses into one file.

4. **Wait for processing** -- don't drop a new inbox file until the agent has finished processing the current one and written the next outbox file.

5. **Let the LLM iterate** -- complex tasks often take multiple rounds. The LLM might create files, then run a build, then fix errors based on the build output, then run tests, etc.

### Supported LLMs

CodingAgent works with any LLM that can follow structured instructions. Tested with:
- ChatGPT (GPT-4, GPT-4o)
- Claude (Sonnet, Opus)
- Gemini Pro
- Local models via Ollama, LM Studio, etc.

Larger, more capable models produce better results because they follow the command syntax more reliably.

---

## Command Reference

These are the commands the LLM can use in its responses:

### CREATE_FILE

Creates or overwrites a file in the workspace.

```
[CREATE_FILE path="src/Calculator.cs"]
using System;

public class Calculator
{
    public int Add(int a, int b) => a + b;
}
[/CREATE_FILE]
```

- `path`: Relative path from workspace root. Directories are created automatically.
- Overwrites the file if it already exists.

### EDIT_FILE

Replaces a range of lines in an existing file.

```
[EDIT_FILE path="src/Calculator.cs" start_line="5" end_line="5"]
    public int Add(int a, int b) => a + b;
    public int Subtract(int a, int b) => a - b;
[/EDIT_FILE]
```

- `path`: Relative path to an existing file.
- `start_line` / `end_line`: 1-indexed, inclusive range of lines to replace.
- The replacement content can have more or fewer lines than the range being replaced.

### DELETE_FILE

Deletes a file from the workspace. Self-closing tag (no body).

```
[DELETE_FILE path="src/OldFile.cs"]
```

### READ_FILE

Requests a file's contents. The contents will appear in the next outbox file. Self-closing tag.

```
[READ_FILE path="src/Calculator.cs"]
```

Useful when the LLM needs to see a file before editing it.

### RUN_COMMAND

Executes a shell command with the workspace as the working directory.

```
[RUN_COMMAND]
dotnet build
[/RUN_COMMAND]
```

- Runs via `cmd.exe /c` on Windows or `/bin/sh -c` on Linux/macOS.
- 30-second timeout. Commands exceeding this are killed.
- stdout and stderr are captured and included in results.
- Output is truncated to 4000 characters.

### MESSAGE

Displays a message to the user in the console. Does not affect the workspace.

```
[MESSAGE]
I'm going to create the project structure first, then implement the logic.
[/MESSAGE]
```

### DONE

Signals that the task is complete. The agent will stop looping.

```
[DONE]
Created a calculator app with add, subtract, multiply, and divide operations.
All tests pass.
[/DONE]
```

---

## Troubleshooting

### "No incomplete session found to resume"

All existing sessions are either complete or none exist. Start a new session instead:

```bash
CodingAgent "Your task description"
```

### Agent doesn't detect the inbox file

- Make sure the file has a `.txt` extension
- Make sure it's saved directly in `inbox/`, not in a subdirectory
- Make sure the file is fully written before the agent tries to read it (some editors save to a temp file first)
- Try saving the file again or renaming it

### "No commands found in response"

The LLM response didn't contain any valid command blocks. This can happen if:
- The LLM didn't follow the protocol syntax
- The response was conversational rather than structured
- You accidentally saved the wrong content

Paste the outbox file into the LLM again and ask it to respond with command blocks.

### Path traversal rejected

The LLM tried to create/edit/delete a file outside the workspace (e.g., using `../`). This is a security measure. The error is reported in the results and the LLM will see it in the next outbox file, so it should self-correct.

### Command timed out

Shell commands have a 30-second timeout. If a command takes longer (e.g., a long build, downloading packages), it will be killed. The LLM will see the timeout in the results.

### Build errors (CS0017: multiple entry points)

If you see this when building the CodingAgent project itself, ensure the `.csproj` file excludes runtime directories:

```xml
<ItemGroup>
  <Compile Remove="workspace\**" />
  <Compile Remove="inbox\**" />
  <Compile Remove="outbox\**" />
  <Compile Remove="sessions\**" />
</ItemGroup>
```

This prevents `.cs` files created by the LLM in the workspace from being included in the CodingAgent build.
