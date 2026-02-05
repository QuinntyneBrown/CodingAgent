namespace CodingAgent;

public class Agent
{
    private readonly string _basePath;
    private readonly string _workspacePath;
    private readonly string _inboxPath;
    private readonly string _outboxPath;
    private readonly string _sessionsPath;
    private readonly string _processedPath;
    private readonly CommandExecutor _executor;

    private Session _session = null!;

    public Agent(string basePath)
    {
        _basePath = Path.GetFullPath(basePath);
        _workspacePath = Path.Combine(_basePath, "workspace");
        _inboxPath = Path.Combine(_basePath, "inbox");
        _outboxPath = Path.Combine(_basePath, "outbox");
        _sessionsPath = Path.Combine(_basePath, "sessions");
        _processedPath = Path.Combine(_inboxPath, "processed");

        Directory.CreateDirectory(_workspacePath);
        Directory.CreateDirectory(_inboxPath);
        Directory.CreateDirectory(_outboxPath);
        Directory.CreateDirectory(_sessionsPath);
        Directory.CreateDirectory(_processedPath);

        _executor = new CommandExecutor(_workspacePath);
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
        SessionStore.Save(_session, _sessionsPath);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"New session: {_session.SessionId}");
        Console.WriteLine($"Task: {_session.Task}");
        Console.ResetColor();
    }

    public bool ResumeSession()
    {
        var session = SessionStore.LoadLatest(_sessionsPath);
        if (session == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No incomplete session found to resume.");
            Console.ResetColor();
            return false;
        }

        _session = session;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Resumed session: {_session.SessionId} (seq {_session.SequenceNumber})");
        Console.WriteLine($"Task: {_session.Task}");
        Console.ResetColor();
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
            var outboxFilePath = Path.Combine(_outboxPath, outboxFileName);

            if (!File.Exists(outboxFilePath))
            {
                var content = OutboxBuilder.Build(_session, _workspacePath);
                var tempPath = outboxFilePath + ".tmp";
                File.WriteAllText(tempPath, content);
                File.Move(tempPath, outboxFilePath, overwrite: true);
            }

            SessionStore.Save(_session, _sessionsPath);

            // Instruct the user
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("========================================");
            Console.WriteLine($"Outbox file ready: {outboxFileName}");
            Console.WriteLine();
            Console.WriteLine("1. Copy the contents of the outbox file");
            Console.WriteLine("2. Paste into your LLM chat");
            Console.WriteLine("3. Save the LLM response as a .txt file in the inbox/ folder");
            Console.WriteLine("========================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Waiting for inbox response... (Ctrl+C to exit)");

            // Wait for inbox file
            var inboxFile = WaitForInboxFile();
            if (inboxFile == null)
            {
                Console.WriteLine("No inbox file received. Exiting.");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Processing: {Path.GetFileName(inboxFile)}");
            Console.ResetColor();

            // Read and parse
            var responseText = File.ReadAllText(inboxFile);
            var commands = CommandParser.Parse(responseText);

            if (commands.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("No commands found in response. The file may not contain valid command blocks.");
                Console.ResetColor();
            }

            // Execute commands
            var outcome = _executor.Execute(commands);

            // Display results
            foreach (var result in outcome.Results)
            {
                var color = result.Success ? ConsoleColor.Green : ConsoleColor.Red;
                Console.ForegroundColor = color;
                Console.WriteLine($"  [{(result.Success ? "OK" : "FAIL")}] {result.CommandType}: {result.Summary}");
                Console.ResetColor();
            }

            // Update session
            _session.LastResults = outcome.Results;
            _session.ReadFileRequests = outcome.ReadFileRequests;

            if (outcome.TaskComplete)
            {
                _session.IsComplete = true;
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("========================================");
                Console.WriteLine("Task complete!");
                if (!string.IsNullOrWhiteSpace(outcome.DoneMessage))
                    Console.WriteLine(outcome.DoneMessage);
                Console.WriteLine("========================================");
                Console.ResetColor();
            }

            SessionStore.Save(_session, _sessionsPath);

            // Move inbox file to processed
            try
            {
                var processedFile = Path.Combine(_processedPath, Path.GetFileName(inboxFile));
                File.Move(inboxFile, processedFile, overwrite: true);
            }
            catch
            {
                // Non-critical, continue
            }
        }
    }

    private string? WaitForInboxFile()
    {
        // First check for pre-existing files
        var existing = FindInboxFile();
        if (existing != null)
            return existing;

        using var resetEvent = new ManualResetEventSlim(false);
        using var watcher = new FileSystemWatcher(_inboxPath, "*.txt")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, _) => resetEvent.Set();
        watcher.Changed += (_, _) => resetEvent.Set();
        watcher.Renamed += (_, _) => resetEvent.Set();

        // Check again after setting up watcher (avoid race condition)
        existing = FindInboxFile();
        if (existing != null)
            return existing;

        while (true)
        {
            resetEvent.Wait();
            resetEvent.Reset();

            // Small delay to let file writing complete
            Thread.Sleep(500);

            var file = FindInboxFile();
            if (file != null)
            {
                // Stability check: ensure file is not still being written
                if (IsFileReady(file))
                    return file;
            }
        }
    }

    private string? FindInboxFile()
    {
        return Directory.GetFiles(_inboxPath, "*.txt")
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
