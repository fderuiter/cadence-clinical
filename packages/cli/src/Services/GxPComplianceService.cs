using System;
using System.Diagnostics;
using System.IO;

using Cadence.Compliance;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class GxPComplianceService
{
    public static int SyncCompliance(bool dryRun = false, bool commit = false)
    {
        AnsiConsole.MarkupLine("[cyan]Synchronizing GxP Compliance Documentation (PRD-SYS-053)...[/]\n");

        int result = RtmGeneratorService.GenerateRtm(
            outputDir: "docs/SDLC",
            reportPath: "report_dotnet.trx",
            draft: dryRun,
            validateOnly: dryRun,
            dynamicTimestamp: false);

        if (result != 0)
        {
            AnsiConsole.MarkupLine("[red]GxP compliance synchronization failed![/]");
            return result;
        }

        if (!dryRun)
        {
            string repoRoot = QualityGateOrchestrator.FindRepoRoot();
            int addCode = RunProcess("git", "add -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md", repoRoot);
            if (addCode != 0) return addCode;

            if (commit)
            {
                int commitCode = RunProcess("git", "commit -m \"docs(rtm): sync GxP compliance docs with current test state\"", repoRoot);
                if (commitCode != 0) return commitCode;
            }
        }

        AnsiConsole.MarkupLine("\n[bold green]GxP Compliance Synchronization Complete.[/]");
        return 0;
    }

    public static int ValidateCompliance()
    {
        return SyncCompliance(dryRun: true, commit: false);
    }

    private static int RunProcess(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null) return 1;
        process.WaitForExit();
        return process.ExitCode;
    }
}

