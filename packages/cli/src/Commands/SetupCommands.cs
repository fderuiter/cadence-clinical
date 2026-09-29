using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class SetupCredentialsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[red]'setup credentials' is not implemented yet. Copy .env.example to .env and fill in the values described in docs/ENVIRONMENT_VARIABLES.md.[/]");
        return 1;
    }
}
