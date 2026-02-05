using System.Text;
using CodingAgent.Configuration;
using CodingAgent.Models;
using Microsoft.Extensions.Options;

namespace CodingAgent.Services;

public class OutboxBuilder
{
    private readonly string _workspacePath;

    public OutboxBuilder(IOptions<AgentOptions> options)
    {
        _workspacePath = options.Value.WorkspacePath;
    }

    public string Build(Session session)
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
        if (Directory.Exists(_workspacePath))
        {
            var files = Directory.GetFiles(_workspacePath, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                sb.AppendLine("(empty workspace)");
            }
            else
            {
                foreach (var file in files)
                {
                    var relative = Path.GetRelativePath(_workspacePath, file);
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
                var fullPath = Path.GetFullPath(Path.Combine(_workspacePath, reqPath));
                if (fullPath.StartsWith(Path.GetFullPath(_workspacePath) + Path.DirectorySeparatorChar)
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
