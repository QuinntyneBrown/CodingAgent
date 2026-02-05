using System.CommandLine;
using CodingAgent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodingAgent.Commands;

public static class NewSessionCommand
{
    public static Command Create(IServiceProvider services)
    {
        var taskArgument = new Argument<string[]>("task", "The task description for the coding agent")
        {
            Arity = ArgumentArity.OneOrMore
        };

        var command = new Command("new", "Start a new coding session")
        {
            taskArgument
        };

        command.SetHandler((string[] taskParts) =>
        {
            var agent = services.GetRequiredService<Agent>();
            var task = string.Join(' ', taskParts);
            agent.StartNewSession(task);
            agent.Run();
        }, taskArgument);

        return command;
    }
}
