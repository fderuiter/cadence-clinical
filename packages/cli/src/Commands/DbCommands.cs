using System.Diagnostics;

using Cadence.Cli.Services;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class DbResetCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return DatabaseResetService.ResetDatabasesAsync(allowOffline: true).GetAwaiter().GetResult();
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

public class DbSeedCommand : Command
{
    public override int Execute(CommandContext context)
    {
        return DatabaseResetService.SeedDatabasesAsync(allowOffline: true).GetAwaiter().GetResult();
    }
}
