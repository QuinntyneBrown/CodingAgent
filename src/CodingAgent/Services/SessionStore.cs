using System.Text.Json;
using CodingAgent.Configuration;
using CodingAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodingAgent.Services;

public class SessionStore
{
    private readonly string _sessionsDir;
    private readonly ILogger<SessionStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SessionStore(IOptions<AgentOptions> options, ILogger<SessionStore> logger)
    {
        _sessionsDir = options.Value.SessionsPath;
        _logger = logger;
    }

    public void Save(Session session)
    {
        Directory.CreateDirectory(_sessionsDir);
        var filePath = Path.Combine(_sessionsDir, $"{session.SessionId}.json");
        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
        _logger.LogDebug("Session {SessionId} saved (seq {Sequence})", session.SessionId, session.SequenceNumber);
    }

    public Session? Load(string sessionId)
    {
        var filePath = Path.Combine(_sessionsDir, $"{sessionId}.json");
        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<Session>(json);
    }

    public Session? LoadLatest()
    {
        if (!Directory.Exists(_sessionsDir))
            return null;

        var files = Directory.GetFiles(_sessionsDir, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            var session = JsonSerializer.Deserialize<Session>(json);
            if (session != null && !session.IsComplete)
            {
                _logger.LogDebug("Found incomplete session {SessionId}", session.SessionId);
                return session;
            }
        }

        return null;
    }
}
