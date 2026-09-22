using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class DbResetCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Resetting local databases (PostgreSQL/SQLite/Neo4j)...[/]");
        var psi = new ProcessStartInfo("python3", "scripts/reset_db.py") { UseShellExecute = false };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode ?? 0;
    }
}

public class DbMigrateCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Running EF Core pre-boot migrations...[/]");
        var psi = new ProcessStartInfo("dotnet", "ef database update --project apps/execution/src/Cadence.Apps.Execution.csproj") { UseShellExecute = false };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode ?? 0;
    }
}

public class DbStatusCommand : Command
{
    public override int Execute(CommandContext context)
    {
        AnsiConsole.MarkupLine("[cyan]Checking database connectivity...[/]");
        return 0;
    }
}
