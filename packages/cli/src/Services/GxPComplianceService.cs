using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class GxPComplianceService
{
    /// <summary><c>gxp sync</c>: runs the tests and validates tags and results on a feature branch; writes only a preview (ADR-2532).</summary>
    public static int SyncCompliance(bool dryRun = false)
    {
        AnsiConsole.MarkupLine("[cyan]Validating GxP traceability on this branch (PRD-SYS-053)...[/]\n");
        return Report(SyncGxPService.SyncCompliance(dryRun: dryRun));
    }

    /// <summary><c>gxp snapshot</c>: the integration owner's command that writes and stages the RTM and IQ/OQ/PQ report.</summary>
    public static int Snapshot(bool commit = false)
    {
        AnsiConsole.MarkupLine("[cyan]Taking the GxP snapshot (PRD-SYS-053)...[/]\n");
        return Report(SyncGxPService.SyncCompliance(commit: commit, snapshot: true));
    }

    public static int ValidateCompliance()
    {
        return SyncCompliance(dryRun: true);
    }

    private static int Report(int result)
    {
        if (result != 0)
        {
            AnsiConsole.MarkupLine("[red]GxP compliance check failed![/]");
            return result;
        }

        AnsiConsole.MarkupLine("\n[bold green]GxP compliance check complete.[/]");
        return 0;
    }
}
