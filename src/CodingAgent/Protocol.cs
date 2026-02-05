using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodingAgent;

public static class Protocol
{
    public const string Version = "1.0";

    public const string SectionHeader = "=== HEADER ===";
    public const string SectionProtocol = "=== PROTOCOL ===";
    public const string SectionContext = "=== CONTEXT ===";
    public const string SectionPrompt = "=== PROMPT ===";

    public static string Instructions => """
        You are a coding agent. You receive tasks and respond with commands to create, edit, and manage code files.

        ## Available Commands

        ### CREATE_FILE - Create or overwrite a file
        [CREATE_FILE path="relative/path/to/file"]
        file contents here
        [/CREATE_FILE]

        ### EDIT_FILE - Replace a range of lines in an existing file
        [EDIT_FILE path="relative/path/to/file" start_line="N" end_line="M"]
        replacement content
        [/EDIT_FILE]
        Note: Lines are 1-indexed. The range is inclusive on both ends.

        ### DELETE_FILE - Delete a file
        [DELETE_FILE path="relative/path/to/file"]

        ### READ_FILE - Request file contents (will appear in next message)
        [READ_FILE path="relative/path/to/file"]

        ### RUN_COMMAND - Execute a shell command
        [RUN_COMMAND]
        dotnet build
        [/RUN_COMMAND]

        ### MESSAGE - Display a message to the user
        [MESSAGE]
        Your message text here
        [/MESSAGE]

        ### DONE - Signal that the task is complete
        [DONE]
        Summary of what was accomplished
        [/DONE]

        ## Rules
        1. All file paths are relative to the workspace root.
        2. Do NOT use absolute paths or path traversal (e.g., ../). They will be rejected.
        3. You may issue multiple commands in a single response.
        4. After each response, you will receive the results of your commands and any requested file contents.
        5. When the task is fully complete, you MUST send a [DONE] command.
        6. If you need to see a file's contents before editing, use READ_FILE first, then edit in the next round.
        """;
}

public class Session
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("task")]
    public string Task { get; set; } = string.Empty;

    [JsonPropertyName("sequenceNumber")]
    public int SequenceNumber { get; set; }

    [JsonPropertyName("isComplete")]
    public bool IsComplete { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("lastResults")]
    public List<CommandResult> LastResults { get; set; } = new();

    [JsonPropertyName("readFileRequests")]
    public List<string> ReadFileRequests { get; set; } = new();
}

public class CommandResult
{
    [JsonPropertyName("commandType")]
    public string CommandType { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;
}

public static class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Save(Session session, string sessionsDir)
    {
        Directory.CreateDirectory(sessionsDir);
        var filePath = Path.Combine(sessionsDir, $"{session.SessionId}.json");
        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }

    public static Session? Load(string sessionsDir, string sessionId)
    {
        var filePath = Path.Combine(sessionsDir, $"{sessionId}.json");
        if (!File.Exists(filePath))
            return null;
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<Session>(json);
    }

    public static Session? LoadLatest(string sessionsDir)
    {
        if (!Directory.Exists(sessionsDir))
            return null;

        var files = Directory.GetFiles(sessionsDir, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var session = JsonSerializer.Deserialize<Session>(json);
            if (session != null && !session.IsComplete)
                return session;
        }

        return null;
    }
}

public static class OutboxBuilder
{
    public static string Build(Session session, string workspacePath)
    {
        var sb = new StringBuilder();

        // Header section
        sb.AppendLine(Protocol.SectionHeader);
        sb.AppendLine($"Session: {session.SessionId}");
        sb.AppendLine($"Sequence: {session.SequenceNumber}");
        sb.AppendLine($"Task: {session.Task}");
        sb.AppendLine();

        // Protocol section
        sb.AppendLine(Protocol.SectionProtocol);
        sb.AppendLine(Protocol.Instructions);
        sb.AppendLine();

        // Context section
        sb.AppendLine(Protocol.SectionContext);

        // Workspace file listing
        sb.AppendLine("## Workspace Files");
        if (Directory.Exists(workspacePath))
        {
            var files = Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                sb.AppendLine("(empty workspace)");
            }
            else
            {
                foreach (var file in files)
                {
                    var relative = Path.GetRelativePath(workspacePath, file);
                    var info = new FileInfo(file);
                    sb.AppendLine($"  {relative} ({info.Length} bytes)");
                }
            }
        }
        else
        {
            sb.AppendLine("(empty workspace)");
        }
        sb.AppendLine();

        // Previous results
        if (session.LastResults.Count > 0)
        {
            sb.AppendLine("## Previous Command Results");
            foreach (var result in session.LastResults)
            {
                var status = result.Success ? "OK" : "FAILED";
                sb.AppendLine($"[{status}] {result.CommandType}: {result.Summary}");
                if (!string.IsNullOrWhiteSpace(result.Output))
                {
                    sb.AppendLine($"  Output: {result.Output}");
                }
            }
            sb.AppendLine();
        }

        // Requested file contents
        if (session.ReadFileRequests.Count > 0)
        {
            sb.AppendLine("## Requested File Contents");
            foreach (var reqPath in session.ReadFileRequests)
            {
                var fullPath = Path.GetFullPath(Path.Combine(workspacePath, reqPath));
                if (fullPath.StartsWith(Path.GetFullPath(workspacePath) + Path.DirectorySeparatorChar)
                    && File.Exists(fullPath))
                {
                    sb.AppendLine($"--- {reqPath} ---");
                    sb.AppendLine(File.ReadAllText(fullPath));
                    sb.AppendLine($"--- end {reqPath} ---");
                }
                else
                {
                    sb.AppendLine($"--- {reqPath} --- (file not found or access denied)");
                }
            }
            sb.AppendLine();
            session.ReadFileRequests.Clear();
        }

        // Prompt section
        sb.AppendLine(Protocol.SectionPrompt);
        if (session.SequenceNumber == 1)
        {
            sb.AppendLine(session.Task);
        }
        else
        {
            sb.AppendLine("Continue working on the task based on the results above.");
            sb.AppendLine("If the task is complete, send [DONE] with a summary.");
        }

        return sb.ToString();
    }
}
