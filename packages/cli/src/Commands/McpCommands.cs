using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class McpServerCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[red]'mcp server' is not implemented yet.[/]");
        return 1;
    }
}
