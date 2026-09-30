using System.Diagnostics;

using Cadence.Cli.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class DbCommands
{
    public static int Reset()
    {
        return DatabaseResetService.ResetDatabasesAsync(allowOffline: true).GetAwaiter().GetResult();
    }

    public static int Migrate()
    {
        AnsiConsole.MarkupLine("[cyan]Running EF Core pre-boot migrations...[/]");
        var psi = new ProcessStartInfo("dotnet", "ef database update --project apps/execution/src/Cadence.Apps.Execution.csproj") { UseShellExecute = false };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode ?? 0;
    }
}
