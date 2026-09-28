using Cadence.ToolingCore.Services;

using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class FixCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return SelfHealService.ExecuteSelfHealing();
    }
}

