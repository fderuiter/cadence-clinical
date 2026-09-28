using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Cadence.Compliance;

namespace Cadence.ToolingCore.Services;

public static class SyncGxPService
{
    private static readonly string[] GxpDocs =
    [
        "docs/SDLC/Requirements_Traceability_Matrix.md",
        "docs/SDLC/IQ_OQ_PQ_Execution_Report.md",
        "docs/SDLC/runs"
    ];

    public static int SyncCompliance(bool dryRun = false, bool commit = false, bool draft = false, bool full = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Cadence Clinical — Native GxP Compliance Sync");
        Console.WriteLine(new string('=', 60));

        if (dryRun) Console.WriteLine("Mode: DRY RUN (no files modified or staged)");
        else if (commit) Console.WriteLine("Mode: FULL SYNC + AUTO COMMIT");
        else Console.WriteLine("Mode: SYNC (staging docs in git)");

        // Step 1: Tests
        Console.WriteLine("\n============================================================");
        Console.WriteLine("STEP 1 / 3 — Running test suite");
        Console.WriteLine("============================================================");

        if (!dryRun && !draft)
        {
            var psi = new ProcessStartInfo("dotnet", "test Cadence.sln --no-restore --configuration Release --results-directory . --logger trx;LogFileName=report_dotnet.trx")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            if (proc != null && proc.ExitCode != 0 && !draft)
            {
                Console.WriteLine("\n[ERROR] Test suite execution failed.");
                return proc.ExitCode;
            }
        }
        else
        {
            Console.WriteLine("⏭ [draft/dry-run] Skipping live test execution.");
        }

        // Step 2: Generate RTM
        Console.WriteLine("\n============================================================");
        Console.WriteLine("STEP 2 / 3 — Generating GxP compliance docs");
        Console.WriteLine("============================================================");

        int rtmRes = RtmGeneratorService.GenerateRtm(draft: draft, validateOnly: dryRun, reportPath: "report_dotnet.trx", repoRoot: repoRoot);
        if (rtmRes != 0) return rtmRes;

        // Step 3: Stage
        Console.WriteLine("\n============================================================");
        Console.WriteLine("STEP 3 / 3 — Staging compliance docs");
        Console.WriteLine("============================================================");

        if (!dryRun)
        {
            var gitArgs = "add -- " + string.Join(" ", GxpDocs);
            var psiGit = new ProcessStartInfo("git", gitArgs)
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false
            };
            using var pGit = Process.Start(psiGit);
            pGit?.WaitForExit();
            Console.WriteLine("✔ Files staged.");

            if (commit)
            {
                var psiCommit = new ProcessStartInfo("git", "commit -m \"docs(rtm): sync GxP compliance docs with current test state\"")
                {
                    WorkingDirectory = repoRoot,
                    UseShellExecute = false
                };
                using var pCommit = Process.Start(psiCommit);
                pCommit?.WaitForExit();
                Console.WriteLine("✔ Committed.");
            }
        }

        Console.WriteLine("\n============================================================");
        Console.WriteLine("✔ GxP sync complete.");
        Console.WriteLine("============================================================");
        return 0;
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
