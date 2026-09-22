using Cadence.Cli.Services;

using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class CheckCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return QualityGateOrchestrator.RunQualityGates();
    }
}
