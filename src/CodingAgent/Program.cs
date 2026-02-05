namespace CodingAgent;

public class Program
{
    public static void Main(string[] args)
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;

        // Walk up to find the repo root (parent of src/)
        var repoRoot = FindRepoRoot(basePath);
        if (repoRoot != null)
            basePath = repoRoot;

        if (args.Length == 1 && args[0] == "--help")
        {
            PrintUsage();
            return;
        }

        var agent = new Agent(basePath);

        if (args.Length == 0 || (args.Length == 1 && args[0] == "--resume"))
        {
            if (!agent.ResumeSession())
            {
                Console.WriteLine("Usage: CodingAgent <task description>");
                Console.WriteLine("       CodingAgent --resume");
                return;
            }
        }
        else
        {
            var prompt = string.Join(' ', args);
            agent.StartNewSession(prompt);
        }

        agent.Run();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("CodingAgent - Offline coding agent using text file protocol");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  CodingAgent <task description>   Start a new session with the given task");
        Console.WriteLine("  CodingAgent --resume             Resume the latest incomplete session");
        Console.WriteLine("  CodingAgent --help               Show this help message");
        Console.WriteLine();
        Console.WriteLine("Workflow:");
        Console.WriteLine("  1. Run CodingAgent with a task description");
        Console.WriteLine("  2. Copy the outbox file contents and paste into your LLM");
        Console.WriteLine("  3. Save the LLM response as a .txt file in the inbox/ folder");
        Console.WriteLine("  4. The agent processes commands and writes the next outbox file");
        Console.WriteLine("  5. Repeat until the LLM sends [DONE]");
    }

    private static string? FindRepoRoot(string startDir)
    {
        var dir = startDir;
        for (int i = 0; i < 10; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")))
                return dir;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
