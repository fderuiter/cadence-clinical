using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class DevCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[bold cyan]Launching Cadence Developer Dashboard & Services...[/]");
        AnsiConsole.MarkupLine("Services: [green]Gateway (8000)[/], [green]Execution (8001)[/], [green]Designer (8002)[/]");
        return 0;
    }
}
