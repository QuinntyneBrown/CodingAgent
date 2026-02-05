using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace CodingAgent;

/// <summary>
/// Minimal console formatter that outputs only the log message,
/// without level prefix, category, or timestamp noise.
/// </summary>
public sealed class PlainConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "plain";

    public PlainConsoleFormatter(IOptions<ConsoleFormatterOptions> options)
        : base(FormatterName)
    {
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message))
            return;

        textWriter.WriteLine(message);

        if (logEntry.Exception != null)
            textWriter.WriteLine(logEntry.Exception.ToString());
    }
}
