using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class SetupCredentialsCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Configuring local development credentials and secrets...[/]");
        return 0;
    }
}
