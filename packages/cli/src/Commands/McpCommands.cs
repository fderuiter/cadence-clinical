using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class McpServerCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Starting Model Context Protocol (MCP) stdio JSON-RPC server...[/]");
        return 0;
    }
}
