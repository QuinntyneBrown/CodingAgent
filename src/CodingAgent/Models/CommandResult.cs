using System.Text.Json.Serialization;

namespace CodingAgent.Models;

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
