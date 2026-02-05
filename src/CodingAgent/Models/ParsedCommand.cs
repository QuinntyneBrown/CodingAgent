namespace CodingAgent.Models;

public abstract record ParsedCommand;

public record CreateFileCommand(string Path, string Content) : ParsedCommand;
public record EditFileCommand(string Path, int StartLine, int EndLine, string Content) : ParsedCommand;
public record DeleteFileCommand(string Path) : ParsedCommand;
public record ReadFileCommand(string Path) : ParsedCommand;
public record RunCommand(string Command) : ParsedCommand;
public record MessageCommand(string Text) : ParsedCommand;
public record DoneCommand(string Message) : ParsedCommand;
public record ErrorCommand(string Error) : ParsedCommand;
