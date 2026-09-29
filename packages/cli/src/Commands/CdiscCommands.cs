using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class CdiscExportCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[red]'cdisc export' is not implemented yet. The USDM JSON schemas in packages/usdm-schemas/src are maintained by hand until the kernel's USDM export lands.[/]");
        return 1;
    }
}
