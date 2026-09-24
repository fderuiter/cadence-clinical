using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class GxPComplianceService
{
    private static readonly string[] GxpDocs =
    [
        "docs/SDLC/Requirements_Traceability_Matrix.md",
        "docs/SDLC/IQ_OQ_PQ_Execution_Report.md"
    ];

    public static int SyncCompliance(bool dryRun = false, bool commit = false)
    {
        AnsiConsole.MarkupLine("[cyan]Synchronizing GxP Compliance Documentation (PRD-SYS-053)...[/]\n");

        // 1. Run test suite to generate TRX
        AnsiConsole.MarkupLine("  1. Executing .NET Test Suites & Generating TRX Report...");
        var psi = new ProcessStartInfo("dotnet", "test Cadence.sln --logger \"trx;LogFileName=report_dotnet.trx\" --filter \"Category!=Integration\"")
        {
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                AnsiConsole.MarkupLine("[red]Test execution failed during GxP compliance sync![/]");
                return proc.ExitCode;
            }
        }

        // 2. Generate RTM report via native C# RtmGeneratorService
        AnsiConsole.MarkupLine("\n  2. Regenerating RTM and IQ/OQ/PQ Compliance Documents...");
        int genExit = RtmGeneratorService.GenerateRtm(
            outputDir: "docs/SDLC",
            reportPath: "report_dotnet.trx",
            draft: false,
            validateOnly: dryRun,
            dynamicTimestamp: false
        );

        if (genExit != 0)
        {
            AnsiConsole.MarkupLine("[red]RTM document generation failed![/]");
            return genExit;
        }

        if (commit && !dryRun)
        {
            AnsiConsole.MarkupLine("\n  3. Staging updated GxP compliance documentation...");
            foreach (var doc in GxpDocs.Where(File.Exists))
            {
                var gitPsi = new ProcessStartInfo("git", $"add \"{doc}\"") { UseShellExecute = false };
                using var gitProc = Process.Start(gitPsi);
                gitProc?.WaitForExit();
            }
            AnsiConsole.MarkupLine("  [green][[OK]] GxP compliance documentation staged for commit.[/]");
        }

        AnsiConsole.MarkupLine("\n[bold green]GxP Compliance Synchronization Complete.[/]");
        return 0;
    }

    public static int ValidateCompliance()
    {
        return SyncCompliance(dryRun: true, commit: false);
    }
}
