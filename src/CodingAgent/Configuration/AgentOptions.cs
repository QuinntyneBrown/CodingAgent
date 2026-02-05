namespace CodingAgent.Configuration;

public class AgentOptions
{
    public const string SectionName = "Agent";

    public string BasePath { get; set; } = "";
    public string WorkspaceDir { get; set; } = "workspace";
    public string InboxDir { get; set; } = "inbox";
    public string OutboxDir { get; set; } = "outbox";
    public string SessionsDir { get; set; } = "sessions";
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxOutputLength { get; set; } = 4000;

    public string WorkspacePath => Path.Combine(BasePath, WorkspaceDir);
    public string InboxPath => Path.Combine(BasePath, InboxDir);
    public string OutboxPath => Path.Combine(BasePath, OutboxDir);
    public string SessionsPath => Path.Combine(BasePath, SessionsDir);
    public string ProcessedPath => Path.Combine(InboxPath, "processed");
}
