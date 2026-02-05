# Protocol Specification

## Table of Contents

- [Overview](#overview)
- [Design Goals](#design-goals)
- [Outbox Format (Agent to LLM)](#outbox-format-agent-to-llm)
- [Inbox Format (LLM to Agent)](#inbox-format-llm-to-agent)
- [Command Syntax Reference](#command-syntax-reference)
- [Session Management](#session-management)
- [Security Model](#security-model)
- [Error Handling](#error-handling)
- [Versioning](#versioning)

---

## Overview

The CodingAgent protocol is a text-based, stateless-per-message protocol for communication between a local coding agent and a remote LLM. Each exchange is a complete, self-contained message: the outbox file contains everything the LLM needs (instructions, context, task), and the inbox file contains the LLM's structured command response.

The protocol is designed to work through copy-paste, meaning it requires no network connection between the agent and the LLM.

---

## Design Goals

1. **Self-contained messages**: Every outbox file includes the full protocol instructions so the LLM can operate without prior chat history.
2. **Human-readable**: All messages are plain text, easily inspected and debugged.
3. **Parseable**: Command blocks use unambiguous delimiters that are unlikely to appear in normal code.
4. **Idempotent**: Re-processing a message produces the same results.
5. **Resilient**: Malformed commands produce error results rather than crashes.

---

## Outbox Format (Agent to LLM)

Each outbox file is a plain text document with four sections, delimited by section markers.

### Section Markers

```
=== HEADER ===
=== PROTOCOL ===
=== CONTEXT ===
=== PROMPT ===
```

### HEADER Section

Contains session metadata:

```
=== HEADER ===
Session: a1b2c3d4
Sequence: 1
Task: Create a calculator app in C#
```

| Field | Format | Description |
|-------|--------|-------------|
| Session | 8-character hex string | Unique session identifier |
| Sequence | Integer (1-based) | Message sequence number within the session |
| Task | Free text | Original task description |

### PROTOCOL Section

Contains the full LLM instructions, including:
- Available commands with syntax examples
- Rules for file path handling
- Behavioral guidelines

This section is identical in every outbox file for a given protocol version.

### CONTEXT Section

Contains the current workspace state:

#### Workspace Files

A listing of all files in the workspace with sizes:

```
## Workspace Files
  src/Calculator.cs (245 bytes)
  src/Program.cs (180 bytes)
  tests/CalculatorTests.cs (512 bytes)
```

Or `(empty workspace)` if no files exist.

#### Previous Command Results

Included from the second outbox onward. Shows the outcome of each command from the LLM's last response:

```
## Previous Command Results
[OK] CREATE_FILE: Created 'src/Calculator.cs'
[OK] RUN_COMMAND: Ran 'dotnet build' (exit code 0)
  Output: Build succeeded. 0 Warning(s) 0 Error(s)
[FAILED] EDIT_FILE: File 'src/Missing.cs' not found
```

#### Requested File Contents

If the LLM used `READ_FILE` in its previous response, the requested file contents appear here:

```
## Requested File Contents
--- src/Calculator.cs ---
using System;

public class Calculator
{
    public int Add(int a, int b) => a + b;
}
--- end src/Calculator.cs ---
```

### PROMPT Section

- **Sequence 1**: Contains the original user task.
- **Sequence 2+**: Contains a continuation prompt: `"Continue working on the task based on the results above. If the task is complete, send [DONE] with a summary."`

---

## Inbox Format (LLM to Agent)

The inbox file is the LLM's raw text response. The agent scans it line-by-line for command blocks. Any text outside command blocks is ignored (the LLM may include explanations, thinking, etc.).

### Command Block Structure

Commands use a tag-based syntax:

```
[COMMAND_NAME attribute="value"]
body content
[/COMMAND_NAME]
```

Self-closing commands (no body):

```
[COMMAND_NAME attribute="value"]
```

### Parsing Rules

1. Opening tags are detected by matching `[COMMAND_NAME` at the start of a trimmed line.
2. Attributes are extracted from the opening tag using `key="value"` pattern matching.
3. Body content is collected verbatim (preserving indentation) until the closing tag.
4. Closing tags are detected by exact match of `[/COMMAND_NAME]` on a trimmed line.
5. Multiple commands can appear in a single inbox file.
6. Commands are executed in order of appearance.
7. Text between command blocks is silently ignored.

---

## Command Syntax Reference

### CREATE_FILE

Creates or overwrites a file.

```
[CREATE_FILE path="relative/path"]
file contents
[/CREATE_FILE]
```

| Attribute | Required | Description |
|-----------|----------|-------------|
| `path` | Yes | Relative path from workspace root |

**Behavior**: Creates parent directories if needed. Overwrites existing files.

### EDIT_FILE

Replaces a range of lines in an existing file.

```
[EDIT_FILE path="relative/path" start_line="N" end_line="M"]
replacement content
[/EDIT_FILE]
```

| Attribute | Required | Description |
|-----------|----------|-------------|
| `path` | Yes | Relative path from workspace root |
| `start_line` | Yes | First line to replace (1-indexed, inclusive) |
| `end_line` | Yes | Last line to replace (1-indexed, inclusive) |

**Behavior**: Reads the file, removes lines `start_line` through `end_line`, inserts the replacement content at that position. The replacement can have a different number of lines than the range.

### DELETE_FILE

Deletes a file. Self-closing (no body or closing tag).

```
[DELETE_FILE path="relative/path"]
```

| Attribute | Required | Description |
|-----------|----------|-------------|
| `path` | Yes | Relative path from workspace root |

### READ_FILE

Requests file contents for the next outbox. Self-closing.

```
[READ_FILE path="relative/path"]
```

| Attribute | Required | Description |
|-----------|----------|-------------|
| `path` | Yes | Relative path from workspace root |

**Behavior**: Does not produce immediate output. The file contents will be included in the `## Requested File Contents` section of the next outbox file.

### RUN_COMMAND

Executes a shell command.

```
[RUN_COMMAND]
command text
[/RUN_COMMAND]
```

No attributes. The body is the command to execute.

**Behavior**: Runs via `cmd.exe /c` (Windows) or `/bin/sh -c` (Linux/macOS). Working directory is the workspace root. 30-second timeout. stdout and stderr are captured. Output is truncated to 4000 characters.

### MESSAGE

Displays a message to the user.

```
[MESSAGE]
message text
[/MESSAGE]
```

No attributes. The body is displayed in the console.

### DONE

Signals task completion.

```
[DONE]
summary text
[/DONE]
```

No attributes. The body is a summary of what was accomplished. The agent stops the session loop after processing this command.

---

## Session Management

### Session ID

An 8-character hex string derived from a GUID (e.g., `a1b2c3d4`). Used to name outbox files and session JSON files.

### Sequence Numbering

- Starts at 1 for the first outbox file.
- Increments by 1 for each outbox-inbox cycle.
- Used in outbox filenames: `{sessionId}_seq{sequence:D4}.txt`

### Outbox File Naming

```
{sessionId}_seq{NNNN}.txt
```

Examples: `a1b2c3d4_seq0001.txt`, `a1b2c3d4_seq0002.txt`

### Inbox File Naming

No naming convention required. Any `.txt` file in the `inbox/` directory is processed. Files are processed in order of last-write time (oldest first).

### Session State

Persisted as JSON in `sessions/{sessionId}.json`:

```json
{
  "sessionId": "a1b2c3d4",
  "task": "Create a calculator app",
  "sequenceNumber": 3,
  "isComplete": false,
  "createdAt": "2025-01-15T10:30:00Z",
  "updatedAt": "2025-01-15T10:35:00Z",
  "lastResults": [ ... ],
  "readFileRequests": [ "src/Calculator.cs" ]
}
```

### Atomic Writes

Both outbox files and session files are written atomically:
1. Write content to `{path}.tmp`
2. Rename (move) `{path}.tmp` to `{path}`

This prevents partial reads if the agent crashes mid-write.

---

## Security Model

### Path Sandboxing

All file paths in CREATE_FILE, EDIT_FILE, DELETE_FILE, and READ_FILE are resolved against the workspace root. The agent performs a security check:

1. Join workspace root with the relative path
2. Resolve to an absolute path (canonicalize)
3. Verify the resolved path starts with the workspace root

Paths that escape the workspace (e.g., `../../../etc/passwd`) are **rejected** with a `REJECTED: Path is outside workspace` error.

### Command Execution Sandboxing

- Shell commands run with the workspace as the working directory
- 30-second timeout prevents runaway processes
- Timed-out processes are killed (entire process tree)
- stdout/stderr output is truncated to prevent memory exhaustion

### Trust Model

The agent trusts the **user** (who chooses which LLM responses to save) but does **not** trust the LLM output. The security measures protect against:
- LLM hallucinating paths outside the workspace
- Commands that hang or produce excessive output
- Malformed command syntax

---

## Error Handling

### Parse Errors

If a command block is malformed:
- Missing required attributes: `ErrorCommand` with descriptive message
- Missing closing tag: `ErrorCommand` noting which command is unclosed
- Invalid attribute values (e.g., non-integer line numbers): `ErrorCommand`

Parse errors are included in results and sent to the LLM in the next outbox, allowing it to self-correct.

### Execution Errors

Each command execution produces a `CommandResult` with a `Success` flag:

- **Path outside workspace**: `Success = false`, summary starts with `REJECTED:`
- **File not found**: `Success = false` for EDIT_FILE, DELETE_FILE, READ_FILE
- **Invalid line range**: `Success = false` for EDIT_FILE
- **Command timeout**: `Success = false` for RUN_COMMAND
- **Non-zero exit code**: `Success = false` for RUN_COMMAND
- **I/O exceptions**: `Success = false` with exception message

The LLM receives these results in the next outbox and can adapt its approach.

---

## Versioning

The protocol version is `1.0`, stored in `Protocol.Version`. Future versions may add new commands or modify existing behavior. The version is not currently included in the outbox format but may be added in future revisions.
