using System;

using Cadence.Compliance;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class GxPComplianceService
{
    public static int SyncCompliance(bool dryRun = false, bool commit = false)
    {
        AnsiConsole.MarkupLine("[cyan]Synchronizing GxP Compliance Documentation (PRD-SYS-053)...[/]\n");

        int exitCode = RtmGeneratorService.GenerateRtm(
            outputDir: "docs/SDLC",
            reportPath: "report_dotnet.trx",
            draft: dryRun,
            validateOnly: dryRun
        );

        if (exitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]GxP compliance synchronization failed![/]");
            return exitCode;
        }

        AnsiConsole.MarkupLine("\n[bold green]GxP Compliance Synchronization Complete.[/]");
        return 0;
    }

    public static int ValidateCompliance()
    {
        return SyncCompliance(dryRun: true, commit: false);
    }
}

