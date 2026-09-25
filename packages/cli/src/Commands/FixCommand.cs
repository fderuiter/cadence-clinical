using System.Diagnostics;
using System.IO;

using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class FixCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return SelfHealService.ExecuteSelfHealing();
    }
}

