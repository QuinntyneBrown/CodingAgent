using CodingAgent.Configuration;
using CodingAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodingAgent.Services;

public class Agent
{
    private readonly AgentOptions _options;
    private readonly CommandExecutor _executor;
    private readonly CommandParser _parser;
    private readonly SessionStore _sessionStore;
    private readonly OutboxBuilder _outboxBuilder;
    private readonly ILogger<Agent> _logger;

    private Session _session = null!;

    public Agent(
        IOptions<AgentOptions> options,
        CommandExecutor executor,
        CommandParser parser,
        SessionStore sessionStore,
        OutboxBuilder outboxBuilder,
        ILogger<Agent> logger)
    {
        _options = options.Value;
        _executor = executor;
        _parser = parser;
        _sessionStore = sessionStore;
        _outboxBuilder = outboxBuilder;
        _logger = logger;

        Directory.CreateDirectory(_options.WorkspacePath);
        Directory.CreateDirectory(_options.InboxPath);
        Directory.CreateDirectory(_options.OutboxPath);
        Directory.CreateDirectory(_options.SessionsPath);
        Directory.CreateDirectory(_options.ProcessedPath);
    }

    public void StartNewSession(string prompt)
    {
        _session = new Session
        {
            SessionId = Guid.NewGuid().ToString("N")[..8],
            Task = prompt,
            SequenceNumber = 0,
            IsComplete = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _sessionStore.Save(_session);

        _logger.LogInformation("New session: {SessionId}", _session.SessionId);
        _logger.LogInformation("Task: {Task}", _session.Task);
    }

    public bool ResumeSession()
    {
        var session = _sessionStore.LoadLatest();
        if (session == null)
        {
            _logger.LogWarning("No incomplete session found to resume.");
            return false;
        }

        _session = session;
        _logger.LogInformation("Resumed session: {SessionId} (seq {Sequence})", _session.SessionId, _session.SequenceNumber);
        _logger.LogInformation("Task: {Task}", _session.Task);
        return true;
    }

    public void Run()
    {
        while (!_session.IsComplete)
        {
            _session.SequenceNumber++;
            _session.UpdatedAt = DateTime.UtcNow;

            // Build and write outbox file
            var outboxFileName = $"{_session.SessionId}_seq{_session.SequenceNumber:D4}.txt";
            var outboxFilePath = Path.Combine(_options.OutboxPath, outboxFileName);

            if (!File.Exists(outboxFilePath))
            {
                var content = _outboxBuilder.Build(_session);
                var tempPath = outboxFilePath + ".tmp";
                File.WriteAllText(tempPath, content);
                File.Move(tempPath, outboxFilePath, overwrite: true);
            }

            _sessionStore.Save(_session);

            // Instruct the user
            _logger.LogInformation("========================================");
            _logger.LogInformation("Outbox file ready: {FileName}", outboxFileName);
            _logger.LogInformation("");
            _logger.LogInformation("1. Copy the contents of the outbox file");
            _logger.LogInformation("2. Paste into your LLM chat");
            _logger.LogInformation("3. Save the LLM response as a .txt file in the inbox/ folder");
            _logger.LogInformation("========================================");
            _logger.LogInformation("Waiting for inbox response... (Ctrl+C to exit)");

            // Wait for inbox file
            var inboxFile = WaitForInboxFile();
            if (inboxFile == null)
            {
                _logger.LogWarning("No inbox file received. Exiting.");
                return;
            }

            _logger.LogInformation("Processing: {FileName}", Path.GetFileName(inboxFile));

            // Read and parse
            var responseText = File.ReadAllText(inboxFile);
            var commands = _parser.Parse(responseText);

            if (commands.Count == 0)
            {
                _logger.LogWarning("No commands found in response. The file may not contain valid command blocks.");
            }

            // Execute commands
            var outcome = _executor.Execute(commands);

            // Display results
            foreach (var result in outcome.Results)
            {
                if (result.Success)
                    _logger.LogInformation("  [OK] {CommandType}: {Summary}", result.CommandType, result.Summary);
                else
                    _logger.LogWarning("  [FAIL] {CommandType}: {Summary}", result.CommandType, result.Summary);
            }

            // Update session
            _session.LastResults = outcome.Results;
            _session.ReadFileRequests = outcome.ReadFileRequests;

            if (outcome.TaskComplete)
            {
                _session.IsComplete = true;
                _logger.LogInformation("========================================");
                _logger.LogInformation("Task complete!");
                if (!string.IsNullOrWhiteSpace(outcome.DoneMessage))
                    _logger.LogInformation("{Message}", outcome.DoneMessage);
                _logger.LogInformation("========================================");
            }

            _sessionStore.Save(_session);

            // Move inbox file to processed
            try
            {
                var processedFile = Path.Combine(_options.ProcessedPath, Path.GetFileName(inboxFile));
                File.Move(inboxFile, processedFile, overwrite: true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to move inbox file to processed");
            }
        }
    }

    private string? WaitForInboxFile()
    {
        var existing = FindInboxFile();
        if (existing != null)
            return existing;

        using var resetEvent = new ManualResetEventSlim(false);
        using var watcher = new FileSystemWatcher(_options.InboxPath, "*.txt")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, _) => resetEvent.Set();
        watcher.Changed += (_, _) => resetEvent.Set();
        watcher.Renamed += (_, _) => resetEvent.Set();

        // Check again after watcher setup to avoid race condition
        existing = FindInboxFile();
        if (existing != null)
            return existing;

        while (true)
        {
            resetEvent.Wait();
            resetEvent.Reset();

            Thread.Sleep(500);

            var file = FindInboxFile();
            if (file != null)
            {
                if (IsFileReady(file))
                    return file;
            }
        }
    }

    private string? FindInboxFile()
    {
        return Directory.GetFiles(_options.InboxPath, "*.txt")
            .OrderBy(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool IsFileReady(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
        }
        return false;
    }
}
