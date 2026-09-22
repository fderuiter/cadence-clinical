using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class RecordSetupGDriveCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Setting up Google Drive integration for video recordings...[/]");
        return 0;
    }
}

public class RecordListCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Listing recorded E2E session videos...[/]");
        return 0;
    }
}

public class RecordRunCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Running Playwright E2E recording session...[/]");
        return 0;
    }
}
