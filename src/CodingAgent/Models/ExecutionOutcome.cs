namespace CodingAgent.Models;

public class ExecutionOutcome
{
    public List<CommandResult> Results { get; } = new();
    public List<string> ReadFileRequests { get; } = new();
    public bool TaskComplete { get; set; }
    public string? DoneMessage { get; set; }
}
