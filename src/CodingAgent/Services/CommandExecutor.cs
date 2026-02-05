using System.Diagnostics;
using CodingAgent.Configuration;
using CodingAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodingAgent.Services;

public class CommandExecutor
{
    private readonly string _workspacePath;
    private readonly int _timeoutMs;
    private readonly int _maxOutputLength;
    private readonly ILogger<CommandExecutor> _logger;

    public CommandExecutor(IOptions<AgentOptions> options, ILogger<CommandExecutor> logger)
    {
        _workspacePath = Path.GetFullPath(options.Value.WorkspacePath);
        _timeoutMs = options.Value.CommandTimeoutSeconds * 1000;
        _maxOutputLength = options.Value.MaxOutputLength;
        _logger = logger;
        Directory.CreateDirectory(_workspacePath);
    }

    public ExecutionOutcome Execute(List<ParsedCommand> commands)
    {
        var outcome = new ExecutionOutcome();

        foreach (var cmd in commands)
        {
            switch (cmd)
            {
                case CreateFileCommand c:
                    outcome.Results.Add(ExecuteCreateFile(c));
                    break;
                case EditFileCommand c:
                    outcome.Results.Add(ExecuteEditFile(c));
                    break;
                case DeleteFileCommand c:
                    outcome.Results.Add(ExecuteDeleteFile(c));
                    break;
                case ReadFileCommand c:
                    outcome.Results.Add(ExecuteReadFile(c, outcome));
                    break;
                case RunCommand c:
                    outcome.Results.Add(ExecuteRunCommand(c));
                    break;
                case MessageCommand c:
                    outcome.Results.Add(ExecuteMessage(c));
                    break;
                case DoneCommand c:
                    outcome.TaskComplete = true;
                    outcome.DoneMessage = c.Message;
                    outcome.Results.Add(new CommandResult
                    {
                        CommandType = "DONE",
                        Summary = c.Message,
                        Success = true
                    });
                    break;
                case ErrorCommand c:
                    outcome.Results.Add(new CommandResult
                    {
                        CommandType = "PARSE_ERROR",
                        Summary = c.Error,
                        Success = false
                    });
                    break;
            }
        }

        return outcome;
    }

    private string? ResolveSafePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, normalized));
        var workspaceWithSep = _workspacePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(workspaceWithSep) && fullPath != _workspacePath)
            return null;

        return fullPath;
    }

    private CommandResult ExecuteCreateFile(CreateFileCommand cmd)
    {
        var safePath = ResolveSafePath(cmd.Path);
        if (safePath == null)
        {
            _logger.LogWarning("Path traversal rejected: {Path}", cmd.Path);
            return new CommandResult
            {
                CommandType = "CREATE_FILE",
                Summary = $"REJECTED: Path '{cmd.Path}' is outside workspace",
                Success = false
            };
        }

        try
        {
            var dir = Path.GetDirectoryName(safePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(safePath, cmd.Content);
            _logger.LogInformation("Created '{Path}'", cmd.Path);
            return new CommandResult
            {
                CommandType = "CREATE_FILE",
                Summary = $"Created '{cmd.Path}'",
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create '{Path}'", cmd.Path);
            return new CommandResult
            {
                CommandType = "CREATE_FILE",
                Summary = $"Failed to create '{cmd.Path}': {ex.Message}",
                Success = false
            };
        }
    }

    private CommandResult ExecuteEditFile(EditFileCommand cmd)
    {
        var safePath = ResolveSafePath(cmd.Path);
        if (safePath == null)
        {
            _logger.LogWarning("Path traversal rejected: {Path}", cmd.Path);
            return new CommandResult
            {
                CommandType = "EDIT_FILE",
                Summary = $"REJECTED: Path '{cmd.Path}' is outside workspace",
                Success = false
            };
        }

        if (!File.Exists(safePath))
        {
            return new CommandResult
            {
                CommandType = "EDIT_FILE",
                Summary = $"File '{cmd.Path}' not found",
                Success = false
            };
        }

        try
        {
            var lines = File.ReadAllLines(safePath).ToList();

            int start = cmd.StartLine - 1;
            int end = cmd.EndLine - 1;

            if (start < 0 || end < start || start >= lines.Count)
            {
                return new CommandResult
                {
                    CommandType = "EDIT_FILE",
                    Summary = $"Invalid line range {cmd.StartLine}-{cmd.EndLine} for file with {lines.Count} lines",
                    Success = false
                };
            }

            end = Math.Min(end, lines.Count - 1);

            var newLines = cmd.Content.Split('\n');
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, newLines);

            File.WriteAllLines(safePath, lines);
            _logger.LogInformation("Edited '{Path}' lines {Start}-{End}", cmd.Path, cmd.StartLine, cmd.EndLine);

            return new CommandResult
            {
                CommandType = "EDIT_FILE",
                Summary = $"Edited '{cmd.Path}' lines {cmd.StartLine}-{cmd.EndLine}",
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit '{Path}'", cmd.Path);
            return new CommandResult
            {
                CommandType = "EDIT_FILE",
                Summary = $"Failed to edit '{cmd.Path}': {ex.Message}",
                Success = false
            };
        }
    }

    private CommandResult ExecuteDeleteFile(DeleteFileCommand cmd)
    {
        var safePath = ResolveSafePath(cmd.Path);
        if (safePath == null)
        {
            _logger.LogWarning("Path traversal rejected: {Path}", cmd.Path);
            return new CommandResult
            {
                CommandType = "DELETE_FILE",
                Summary = $"REJECTED: Path '{cmd.Path}' is outside workspace",
                Success = false
            };
        }

        if (!File.Exists(safePath))
        {
            return new CommandResult
            {
                CommandType = "DELETE_FILE",
                Summary = $"File '{cmd.Path}' not found",
                Success = false
            };
        }

        try
        {
            File.Delete(safePath);
            _logger.LogInformation("Deleted '{Path}'", cmd.Path);
            return new CommandResult
            {
                CommandType = "DELETE_FILE",
                Summary = $"Deleted '{cmd.Path}'",
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete '{Path}'", cmd.Path);
            return new CommandResult
            {
                CommandType = "DELETE_FILE",
                Summary = $"Failed to delete '{cmd.Path}': {ex.Message}",
                Success = false
            };
        }
    }

    private CommandResult ExecuteReadFile(ReadFileCommand cmd, ExecutionOutcome outcome)
    {
        var safePath = ResolveSafePath(cmd.Path);
        if (safePath == null)
        {
            _logger.LogWarning("Path traversal rejected: {Path}", cmd.Path);
            return new CommandResult
            {
                CommandType = "READ_FILE",
                Summary = $"REJECTED: Path '{cmd.Path}' is outside workspace",
                Success = false
            };
        }

        if (!File.Exists(safePath))
        {
            return new CommandResult
            {
                CommandType = "READ_FILE",
                Summary = $"File '{cmd.Path}' not found",
                Success = false
            };
        }

        outcome.ReadFileRequests.Add(cmd.Path);
        return new CommandResult
        {
            CommandType = "READ_FILE",
            Summary = $"Queued '{cmd.Path}' for next outbox",
            Success = true
        };
    }

    private CommandResult ExecuteRunCommand(RunCommand cmd)
    {
        try
        {
            _logger.LogInformation("Running command: {Command}", cmd.Command);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows() ? $"/c {cmd.Command}" : $"-c \"{cmd.Command.Replace("\"", "\\\"")}\"",
                WorkingDirectory = _workspacePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            var exited = process.WaitForExit(_timeoutMs);
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                _logger.LogWarning("Command timed out ({Timeout}s): {Command}", _timeoutMs / 1000, cmd.Command);
                return new CommandResult
                {
                    CommandType = "RUN_COMMAND",
                    Summary = $"Command timed out ({_timeoutMs / 1000}s): {cmd.Command}",
                    Success = false,
                    Output = TruncateOutput(stdout + "\n" + stderr)
                };
            }

            var output = stdout;
            if (!string.IsNullOrWhiteSpace(stderr))
                output += (string.IsNullOrWhiteSpace(output) ? "" : "\n") + stderr;

            return new CommandResult
            {
                CommandType = "RUN_COMMAND",
                Summary = $"Ran '{cmd.Command}' (exit code {process.ExitCode})",
                Success = process.ExitCode == 0,
                Output = TruncateOutput(output)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run command: {Command}", cmd.Command);
            return new CommandResult
            {
                CommandType = "RUN_COMMAND",
                Summary = $"Failed to run '{cmd.Command}': {ex.Message}",
                Success = false
            };
        }
    }

    private CommandResult ExecuteMessage(MessageCommand cmd)
    {
        _logger.LogInformation("[LLM] {Message}", cmd.Text);

        return new CommandResult
        {
            CommandType = "MESSAGE",
            Summary = cmd.Text,
            Success = true
        };
    }

    private string TruncateOutput(string output)
    {
        if (output.Length <= _maxOutputLength)
            return output;
        return output[.._maxOutputLength] + "\n... (output truncated)";
    }
}
