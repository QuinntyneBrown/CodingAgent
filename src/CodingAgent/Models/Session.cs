using System.Text.Json.Serialization;

namespace CodingAgent.Models;

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
