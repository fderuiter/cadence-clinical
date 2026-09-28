using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class CdiscExportCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Exporting USDM / CDISC compliance artifacts...[/]");
        return 0;
    }
}
