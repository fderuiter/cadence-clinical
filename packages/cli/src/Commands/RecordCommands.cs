using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class RecordSetupGDriveCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[red]'record setup-gdrive' is not implemented yet.[/]");
        return 1;
    }
}

public class RecordListCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[red]'record list' is not implemented yet.[/]");
        return 1;
    }
}

public class RecordRunCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[red]'record run' is not implemented yet.[/]");
        return 1;
    }
}
