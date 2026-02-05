using System.CommandLine;
using CodingAgent.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CodingAgent.Commands;

public static class ResumeCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("resume", "Resume the latest incomplete session");

        command.SetHandler(() =>
        {
            var agent = services.GetRequiredService<Agent>();
            if (!agent.ResumeSession())
                return;
            agent.Run();
        });

        return command;
    }
}
