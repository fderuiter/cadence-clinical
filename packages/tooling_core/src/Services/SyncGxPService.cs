using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;
using Cadence.Kernel.Time;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// <c>gxp sync</c>, <c>gxp snapshot</c> and <c>gxp validate</c> (ADR-2532, #5151). A feature branch runs <c>gxp sync</c>:
/// the tests run, the documents are generated into <see cref="GxpSnapshotPolicy.PreviewDir"/> for review, and unknown
/// requirement IDs, failing tagged tests and skipped database tests fail it, but nothing under <c>docs/SDLC</c> changes.
/// The integration owner runs <c>gxp snapshot</c> on the integrated candidate, which writes and stages the snapshot.
/// </summary>
public static class SyncGxPService
{
    private static readonly string[] SnapshotPaths =
    [
        .. GxpSnapshotPolicy.Documents,
        "docs/SDLC/runs",
    ];

    /// <summary>
    /// Runs the tests and checks the results. With <paramref name="snapshot"/> the documents are written to
    /// <c>docs/SDLC</c> and staged (and committed with <paramref name="commit"/>); without it they go to the preview
    /// directory and nothing is staged. <paramref name="dryRun"/> runs no tests and writes nothing: it validates the tags,
    /// the integrity stamps and, in snapshot mode, that the committed snapshot's mapping is current.
    /// </summary>
    public static int SyncCompliance(bool dryRun = false, bool commit = false, bool draft = false, bool snapshot = false, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string command = snapshot ? "pnpm cadence gxp snapshot" : "pnpm cadence gxp sync";
        Console.WriteLine("Cadence Clinical — Native GxP Compliance Sync");
        Console.WriteLine(new string('=', 60));

        if (dryRun) Console.WriteLine("Mode: VALIDATE (no tests run, no files written)");
        else if (snapshot) Console.WriteLine(commit ? "Mode: SNAPSHOT + COMMIT (writes and commits docs/SDLC)" : "Mode: SNAPSHOT (writes and stages docs/SDLC)");
        else Console.WriteLine($"Mode: BRANCH (writes a preview to {GxpSnapshotPolicy.PreviewDir}; docs/SDLC is the baseline's snapshot and is left alone)");

        if (dryRun)
        {
            return RtmGeneratorService.GenerateRtm(
                draft: true, validateOnly: true, repoRoot: repoRoot, requireCurrentSnapshot: snapshot || GxpSnapshotPolicy.IsSnapshotMode());
        }

        // Step 1: Tests
        Console.WriteLine("\n============================================================");
        Console.WriteLine("STEP 1 / 3 — Running test suite");
        Console.WriteLine("============================================================");

        string resultsDir = Path.Combine(repoRoot, RtmGeneratorService.DefaultResultsDir);
        int testExitCode = 0;

        if (!draft)
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
                Console.WriteLine("\n[ERROR] Test suite execution failed. The documents are still generated so the failures are visible, but nothing is staged.");
            }
        }
        else
        {
            Console.WriteLine("⏭ [draft] Skipping live test execution.");
        }

        // Step 2: Generate
        Console.WriteLine("\n============================================================");
        Console.WriteLine("STEP 2 / 3 — Generating GxP compliance docs");
        Console.WriteLine("============================================================");

        string outputDir = snapshot ? "docs/SDLC" : GxpSnapshotPolicy.PreviewDir;
        var evidence = RtmEvidenceContext.Capture(repoRoot, command, SystemClock.Instance);
        int rtmRes = RtmGeneratorService.GenerateRtm(
            outputDir: outputDir, draft: draft, reportPath: resultsDir, repoRoot: repoRoot, honorDraftEnvironment: false, evidence: evidence);
        if (testExitCode != 0) return testExitCode;
        if (rtmRes != 0) return rtmRes;

        // Both modes fail on unknown requirement IDs, failing tagged tests and a broken stamp on the committed documents.
        // Only snapshot mode also requires the committed mapping to be current.
        int validation = RtmGeneratorService.GenerateRtm(
            outputDir: "docs/SDLC", draft: draft, validateOnly: true, reportPath: resultsDir, repoRoot: repoRoot, honorDraftEnvironment: false,
            requireCurrentSnapshot: snapshot);
        if (validation != 0) return validation;

        // A run that skipped database tests never exercised them (#5324, #5325): the documents show the skips, but
        // they are not evidence.
        IReadOnlyList<SkippedDatabaseTest> skipped = draft ? [] : DatabaseTestEvidence.FindSkipped(resultsDir);
        if (skipped.Count > 0)
        {
            Console.WriteLine($"\n[ERROR] {skipped.Count} database test(s) did not execute. Set CADENCE_TEST_POSTGRES=<connection string> or CADENCE_TEST_DOCKER=1 (docs/TESTING.md) and run again:");
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

        // Step 3: Stage (snapshot only)
        Console.WriteLine("\n============================================================");
        Console.WriteLine("STEP 3 / 3 — Staging compliance docs");
        Console.WriteLine("============================================================");

        if (!snapshot)
        {
            Console.WriteLine($"✔ Tags and results are valid. Preview: {GxpSnapshotPolicy.PreviewDir}/. Nothing is staged: the integration owner takes the next snapshot (ADR-2532).");
            return 0;
        }

        var psiGit = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, UseShellExecute = false };
        foreach (string arg in new[] { "add", "--" }.Concat(SnapshotPaths))
        {
            psiGit.ArgumentList.Add(arg);
        }

        using (var pGit = Process.Start(psiGit))
        {
            pGit?.WaitForExit();
        }

        Console.WriteLine("✔ Files staged.");

        if (commit)
        {
            var psiCommit = new ProcessStartInfo("git", "commit -m \"docs(rtm): take the GxP snapshot from the integrated candidate\"")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false
            };
            // The pre-commit snapshot guard admits the documents only in snapshot mode.
            psiCommit.Environment[GxpSnapshotPolicy.SnapshotEnvironmentVariable] = "1";
            using var pCommit = Process.Start(psiCommit);
            pCommit?.WaitForExit();
            Console.WriteLine("✔ Committed.");
        }
        else
        {
            Console.WriteLine($"Commit with {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1 so the pre-commit snapshot guard admits the documents.");
        }

        Console.WriteLine("\n============================================================");
        Console.WriteLine("✔ GxP snapshot complete.");
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
