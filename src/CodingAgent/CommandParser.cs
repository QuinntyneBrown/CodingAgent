using System.Text.RegularExpressions;

namespace CodingAgent;

public abstract record ParsedCommand;

public record CreateFileCommand(string Path, string Content) : ParsedCommand;
public record EditFileCommand(string Path, int StartLine, int EndLine, string Content) : ParsedCommand;
public record DeleteFileCommand(string Path) : ParsedCommand;
public record ReadFileCommand(string Path) : ParsedCommand;
public record RunCommand(string Command) : ParsedCommand;
public record MessageCommand(string Text) : ParsedCommand;
public record DoneCommand(string Message) : ParsedCommand;
public record ErrorCommand(string Error) : ParsedCommand;

public static class CommandParser
{
    private static readonly Regex AttributeRegex = new(
        """(\w+)\s*=\s*"([^"]*)" """.TrimEnd(),
        RegexOptions.Compiled);

    public static List<ParsedCommand> Parse(string text)
    {
        var commands = new List<ParsedCommand>();
        var lines = text.Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');
            var trimmed = line.Trim();

            // Self-closing DELETE_FILE
            if (trimmed.StartsWith("[DELETE_FILE ") && trimmed.EndsWith("]") && !trimmed.StartsWith("[/"))
            {
                var attrs = ParseAttributes(trimmed);
                if (attrs.TryGetValue("path", out var path))
                    commands.Add(new DeleteFileCommand(path));
                else
                    commands.Add(new ErrorCommand("DELETE_FILE missing path attribute"));
                i++;
                continue;
            }

            // Self-closing READ_FILE
            if (trimmed.StartsWith("[READ_FILE ") && trimmed.EndsWith("]") && !trimmed.StartsWith("[/"))
            {
                var attrs = ParseAttributes(trimmed);
                if (attrs.TryGetValue("path", out var path))
                    commands.Add(new ReadFileCommand(path));
                else
                    commands.Add(new ErrorCommand("READ_FILE missing path attribute"));
                i++;
                continue;
            }

            // CREATE_FILE block
            if (trimmed.StartsWith("[CREATE_FILE ") && trimmed.EndsWith("]"))
            {
                var attrs = ParseAttributes(trimmed);
                if (!attrs.TryGetValue("path", out var path))
                {
                    commands.Add(new ErrorCommand("CREATE_FILE missing path attribute"));
                    i++;
                    continue;
                }
                var (body, endIdx) = CollectBody(lines, i + 1, "[/CREATE_FILE]");
                if (endIdx < 0)
                {
                    commands.Add(new ErrorCommand($"CREATE_FILE for '{path}' missing closing tag"));
                    i++;
                    continue;
                }
                commands.Add(new CreateFileCommand(path, body));
                i = endIdx + 1;
                continue;
            }

            // EDIT_FILE block
            if (trimmed.StartsWith("[EDIT_FILE ") && trimmed.EndsWith("]"))
            {
                var attrs = ParseAttributes(trimmed);
                if (!attrs.TryGetValue("path", out var path) ||
                    !attrs.TryGetValue("start_line", out var startStr) ||
                    !attrs.TryGetValue("end_line", out var endStr))
                {
                    commands.Add(new ErrorCommand("EDIT_FILE missing required attributes (path, start_line, end_line)"));
                    i++;
                    continue;
                }
                if (!int.TryParse(startStr, out var startLine) || !int.TryParse(endStr, out var endLine))
                {
                    commands.Add(new ErrorCommand("EDIT_FILE start_line/end_line must be integers"));
                    i++;
                    continue;
                }
                var (body, endIdx) = CollectBody(lines, i + 1, "[/EDIT_FILE]");
                if (endIdx < 0)
                {
                    commands.Add(new ErrorCommand($"EDIT_FILE for '{path}' missing closing tag"));
                    i++;
                    continue;
                }
                commands.Add(new EditFileCommand(path, startLine, endLine, body));
                i = endIdx + 1;
                continue;
            }

            // RUN_COMMAND block
            if (trimmed == "[RUN_COMMAND]")
            {
                var (body, endIdx) = CollectBody(lines, i + 1, "[/RUN_COMMAND]");
                if (endIdx < 0)
                {
                    commands.Add(new ErrorCommand("RUN_COMMAND missing closing tag"));
                    i++;
                    continue;
                }
                commands.Add(new RunCommand(body.Trim()));
                i = endIdx + 1;
                continue;
            }

            // MESSAGE block
            if (trimmed == "[MESSAGE]")
            {
                var (body, endIdx) = CollectBody(lines, i + 1, "[/MESSAGE]");
                if (endIdx < 0)
                {
                    commands.Add(new ErrorCommand("MESSAGE missing closing tag"));
                    i++;
                    continue;
                }
                commands.Add(new MessageCommand(body.Trim()));
                i = endIdx + 1;
                continue;
            }

            // DONE block
            if (trimmed == "[DONE]")
            {
                var (body, endIdx) = CollectBody(lines, i + 1, "[/DONE]");
                if (endIdx < 0)
                {
                    commands.Add(new ErrorCommand("DONE missing closing tag"));
                    i++;
                    continue;
                }
                commands.Add(new DoneCommand(body.Trim()));
                i = endIdx + 1;
                continue;
            }

            i++;
        }

        return commands;
    }

    private static Dictionary<string, string> ParseAttributes(string tag)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex.Matches(tag))
        {
            attrs[match.Groups[1].Value] = match.Groups[2].Value;
        }
        return attrs;
    }

    private static (string Body, int EndIndex) CollectBody(string[] lines, int startIdx, string closingTag)
    {
        var bodyLines = new List<string>();
        for (int j = startIdx; j < lines.Length; j++)
        {
            var trimmed = lines[j].TrimEnd('\r').Trim();
            if (trimmed == closingTag)
            {
                return (string.Join('\n', bodyLines), j);
            }
            bodyLines.Add(lines[j].TrimEnd('\r'));
        }
        return ("", -1);
    }
}
