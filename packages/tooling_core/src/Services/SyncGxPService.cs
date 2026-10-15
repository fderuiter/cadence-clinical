using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;
using Cadence.Kernel.Time;

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

        string resultsDir = Path.Combine(repoRoot, RtmGeneratorService.DefaultResultsDir);
        int testExitCode = 0;

        if (!dryRun && !draft)
        {
            // Every test project writes its own TRX file into a clean directory. A fixed LogFileName
            // would make each project overwrite the previous one, leaving only the last project's results.
            if (Directory.Exists(resultsDir)) Directory.Delete(resultsDir, recursive: true);
            Directory.CreateDirectory(resultsDir);

            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false
            };
            // The database fixtures record the server, extensions, migrations and role they used next to the TRX files.
            psi.Environment["CADENCE_TEST_EVIDENCE_DIR"] = resultsDir;
            foreach (var arg in new[] { "test", "Cadence.sln", "--no-restore", "--configuration", "Release", "--results-directory", resultsDir, "--logger", "trx;LogFilePrefix=gxp" })
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            proc?.WaitForExit();
            testExitCode = proc?.ExitCode ?? 1;
            if (testExitCode != 0)
            {
                Console.WriteLine("\n[ERROR] Test suite execution failed. The RTM is still regenerated so the failures are visible, but nothing is staged.");
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

        var evidence = RtmEvidenceContext.Capture(repoRoot, "pnpm cadence gxp sync", SystemClock.Instance);
        int rtmRes = RtmGeneratorService.GenerateRtm(
            draft: draft, validateOnly: dryRun, reportPath: resultsDir, repoRoot: repoRoot, honorDraftEnvironment: false, evidence: evidence);
        if (testExitCode != 0) return testExitCode;
        if (rtmRes != 0) return rtmRes;

        // A run that skipped database tests never exercised them (#5324, #5325): the documents show the skips, but
        // they are not staged as evidence.
        IReadOnlyList<SkippedDatabaseTest> skipped = dryRun || draft ? [] : DatabaseTestEvidence.FindSkipped(resultsDir);
        if (skipped.Count > 0)
        {
            Console.WriteLine($"\n[ERROR] {skipped.Count} database test(s) did not execute, so nothing is staged. Set CADENCE_TEST_POSTGRES=<connection string> or CADENCE_TEST_DOCKER=1 (docs/TESTING.md) and run gxp sync again:");
            foreach (var test in skipped.Take(20))
            {
                Console.WriteLine($"  {test.FullName}: {test.Reason}");
            }

            if (skipped.Count > 20)
            {
                Console.WriteLine($"  ... and {skipped.Count - 20} more");
            }

            return 1;
        }

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
