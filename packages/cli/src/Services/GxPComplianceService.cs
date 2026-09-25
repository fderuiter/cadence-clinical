using System;

using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class GxPComplianceService
{
    public static int SyncCompliance(bool dryRun = false, bool commit = false)
    {
        AnsiConsole.MarkupLine("[cyan]Synchronizing GxP Compliance Documentation (PRD-SYS-053)...[/]\n");

        int res = SyncGxPService.SyncCompliance(dryRun, commit);
        if (res != 0)
        {
            AnsiConsole.MarkupLine("[red]GxP compliance synchronization failed![/]");
            return res;
        }

        AnsiConsole.MarkupLine("\n[bold green]GxP Compliance Synchronization Complete.[/]");
        return 0;
    }

    public static int ValidateCompliance()
    {
        return SyncCompliance(dryRun: true, commit: false);
    }
}

