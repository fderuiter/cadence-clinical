using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.ToolingCore.Primitives;

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
    /// Runs the tests and checks the results, or reuses verified TRX and database evidence under <paramref name="resultsDir"/>
    /// when provided. With <paramref name="snapshot"/> the documents are written to <c>docs/SDLC</c> and staged (and
    /// committed with <paramref name="commit"/>); without it they go to the preview directory (unless
    /// <see cref="GxpSnapshotPolicy.SnapshotEnvironmentVariable"/> is set) and nothing is staged. <paramref name="dryRun"/>
    /// runs no tests and writes nothing: it validates the tags, the integrity stamps and, in snapshot mode, that the
    /// committed snapshot's mapping is current.
    /// </summary>
    public static int SyncCompliance(
        bool dryRun = false,
        bool commit = false,
        bool draft = false,
        bool snapshot = false,
        string? repoRoot = null,
        string? resultsDir = null,
        Func<string, string?>? environment = null,
        TextWriter? output = null)
    {
        TextWriter writer = output ?? Console.Out;
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        string command = snapshot ? "pnpm cadence gxp snapshot" : "pnpm cadence gxp sync";
        writer.WriteLine("Cadence Clinical — Native GxP Compliance Sync");
        writer.WriteLine(new string('=', 60));

        if (dryRun) writer.WriteLine("Mode: VALIDATE (no tests run, no files written)");
        else if (snapshot) writer.WriteLine(commit ? "Mode: SNAPSHOT + COMMIT (writes and commits docs/SDLC)" : "Mode: SNAPSHOT (writes and stages docs/SDLC)");
        else writer.WriteLine($"Mode: BRANCH (writes a preview to {GxpSnapshotPolicy.PreviewDir}; docs/SDLC is the baseline's snapshot and is left alone)");

        // A snapshot is validation evidence: it always comes from a full test run, never from a draft (#5324).
        if (snapshot && draft)
        {
            writer.WriteLine("[ERROR] A snapshot can't be a draft: it runs every test against PostgreSQL. Run `pnpm cadence gxp snapshot` without --draft.");
            return 1;
        }

        if (dryRun)
        {
            return RtmGeneratorService.GenerateRtm(
                draft: true, validateOnly: true, repoRoot: repoRoot, requireCurrentSnapshot: snapshot || GxpSnapshotPolicy.IsSnapshotMode(environment),
                requireRecordedEvidence: true);
        }

        if (resultsDir is not null && string.IsNullOrWhiteSpace(resultsDir))
        {
            writer.WriteLine("[ERROR] --results-dir requires a non-empty directory path.");
            return 1;
        }

        // Step 1: Tests
        writer.WriteLine("\n============================================================");
        writer.WriteLine("STEP 1 / 3 — Running test suite");
        writer.WriteLine("============================================================");

        string relativeOrFullResultsDir = string.IsNullOrWhiteSpace(resultsDir) ? RtmGeneratorService.DefaultResultsDir : resultsDir;
        string resolvedResultsDir = Path.GetFullPath(relativeOrFullResultsDir, repoRoot);
        bool writeToSdlc = snapshot || GxpSnapshotPolicy.IsSnapshotMode(environment);
        int testExitCode = 0;

        if (draft)
        {
            writer.WriteLine("⏭ [draft] Skipping live test execution.");
        }
        else if (!string.IsNullOrWhiteSpace(resultsDir))
        {
            if (!Directory.Exists(resolvedResultsDir))
            {
                writer.WriteLine($"[ERROR] Test results directory not found: {resolvedResultsDir}");
                return 1;
            }

            writer.WriteLine($"Using existing test results and database evidence from {resultsDir}.");
            if (writeToSdlc && !VerifyDatabaseEvidence(resolvedResultsDir, relativeOrFullResultsDir, repoRoot, requireBrowserEvidence: true))
            {
                return 1;
            }
        }
        else
        {
            // Every test project writes its own TRX file into a clean directory. A fixed LogFileName
            // would make each project overwrite the previous one, leaving only the last project's results.
            if (Directory.Exists(resolvedResultsDir)) Directory.Delete(resolvedResultsDir, recursive: true);
            Directory.CreateDirectory(resolvedResultsDir);

            var baseEnv = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                // Database fixtures record server, extensions, migrations, and role metadata next to TRX and JUnit XML files.
                ["CADENCE_TEST_EVIDENCE_DIR"] = resolvedResultsDir,
            };
            if (writeToSdlc && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CADENCE_TEST_BROWSER")))
            {
                baseEnv["CADENCE_TEST_BROWSER"] = "1";
            }

            testExitCode = RunProcess(
                repoRoot,
                "dotnet",
                ["test", "Cadence.sln", "--no-restore", "--configuration", "Release", "--results-directory", resolvedResultsDir, "--logger", "trx;LogFilePrefix=gxp"],
                baseEnv);

            int frontendUnitExit = RunProcess(repoRoot, "pnpm", ["-r", "test"], baseEnv);
            if (testExitCode == 0 && frontendUnitExit != 0) testExitCode = frontendUnitExit;

            int docsVitestExit = RunProcess(repoRoot, "pnpm", ["exec", "vitest", "run", "--config", "vitest.config.mjs"], baseEnv);
            if (testExitCode == 0 && docsVitestExit != 0) testExitCode = docsVitestExit;

            bool runBrowserSuites = writeToSdlc
                || string.Equals(Environment.GetEnvironmentVariable("CADENCE_TEST_BROWSER"), "1", StringComparison.Ordinal);
            if (runBrowserSuites)
            {
                var browserEnv = new Dictionary<string, string>(baseEnv, StringComparer.Ordinal)
                {
                    ["CADENCE_TEST_BROWSER"] = "1",
                };
                var workflowEnv = new Dictionary<string, string>(browserEnv, StringComparer.Ordinal)
                {
                    ["DEMO_MODE"] = "false",
                };

                (string[] Args, IReadOnlyDictionary<string, string> Env)[] e2eCommands =
                [
                    (["--filter", "ui", "build"], browserEnv),
                    (["docs:build"], browserEnv),
                    (["--filter", "subject-portal", "test:e2e:docs-portal"], browserEnv),
                    (["--filter", "subject-portal", "test:e2e"], browserEnv),
                    (["--filter", "web", "test:e2e"], browserEnv),
                    (["--filter", "web", "test:e2e:workflows"], workflowEnv),
                ];

                foreach (var (args, env) in e2eCommands)
                {
                    int stepExit = RunProcess(repoRoot, "pnpm", args, env);
                    if (testExitCode == 0 && stepExit != 0)
                    {
                        testExitCode = stepExit;
                    }
                }
            }

            if (testExitCode != 0)
            {
                writer.WriteLine("\n[ERROR] Test suite execution failed. The documents are still generated so the failures are visible, but nothing is staged.");
            }
            else if (writeToSdlc && !VerifyDatabaseEvidence(resolvedResultsDir, relativeOrFullResultsDir, repoRoot, requireBrowserEvidence: true))
            {
                return 1;
            }
        }

        // Step 2: Generate
        writer.WriteLine("\n============================================================");
        writer.WriteLine("STEP 2 / 3 — Generating GxP compliance docs");
        writer.WriteLine("============================================================");

        string outputDir = writeToSdlc ? "docs/SDLC" : GxpSnapshotPolicy.PreviewDir;
        var evidence = RtmEvidenceContext.Capture(repoRoot, command, SystemClock.Instance);
        int rtmRes = RtmGeneratorService.GenerateRtm(
            outputDir: outputDir, draft: draft, reportPath: resolvedResultsDir, repoRoot: repoRoot, honorDraftEnvironment: false, evidence: evidence);
        if (testExitCode != 0) return testExitCode;
        if (rtmRes != 0) return rtmRes;

        // Both modes fail on unknown requirement IDs, failing tagged tests and a broken stamp on the committed documents.
        // Only snapshot mode also requires the committed mapping to be current.
        int validation = RtmGeneratorService.GenerateRtm(
            outputDir: "docs/SDLC", draft: draft, validateOnly: true, reportPath: resolvedResultsDir, repoRoot: repoRoot, honorDraftEnvironment: false,
            requireCurrentSnapshot: writeToSdlc, requireRecordedEvidence: true);
        if (validation != 0) return validation;

        // A run that skipped database tests or recorded no PostgreSQL fixture metadata never exercised them (#5324, #5325):
        // the preview documents show the skips, but they are not evidence.
        if (!draft && !VerifyDatabaseEvidence(resolvedResultsDir, relativeOrFullResultsDir, repoRoot, requireBrowserEvidence: writeToSdlc))
        {
            return 1;
        }

        // Step 3: Stage (snapshot only)
        writer.WriteLine("\n============================================================");
        writer.WriteLine("STEP 3 / 3 — Staging compliance docs");
        writer.WriteLine("============================================================");

        if (!snapshot)
        {
            writer.WriteLine($"✔ Tags and results are valid. Preview: {GxpSnapshotPolicy.PreviewDir}/. Nothing is staged: the integration owner takes the next snapshot (ADR-2532).");
            return 0;
        }

        string[] existingSnapshotPaths = [.. SnapshotPaths.Where(p => File.Exists(Path.Combine(repoRoot, p)) || Directory.Exists(Path.Combine(repoRoot, p)))];
        if (Directory.Exists(Path.Combine(repoRoot, ".git")) && existingSnapshotPaths.Length > 0)
        {
            var psiGit = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, UseShellExecute = false };
            foreach (string arg in new[] { "add", "--" }.Concat(existingSnapshotPaths))
            {
                psiGit.ArgumentList.Add(arg);
            }

            using var pGit = Process.Start(psiGit);
            pGit?.WaitForExit();
            if ((pGit?.ExitCode ?? 1) != 0)
            {
                writer.WriteLine("[ERROR] Failed to stage GxP snapshot files with git add.");
                return 1;
            }
        }

        writer.WriteLine("✔ Files staged.");

        if (commit)
        {
            var psiCommit = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false
            };
            psiCommit.ArgumentList.Add("commit");
            psiCommit.ArgumentList.Add("-m");
            psiCommit.ArgumentList.Add(GxpSnapshotPolicy.SnapshotCommitSubject);
            psiCommit.ArgumentList.Add("-m");
            psiCommit.ArgumentList.Add(GxpSnapshotPolicy.SnapshotTrailer);
            // The pre-commit snapshot guard admits the documents only in snapshot mode.
            psiCommit.Environment[GxpSnapshotPolicy.SnapshotEnvironmentVariable] = "1";
            using var pCommit = Process.Start(psiCommit);
            pCommit?.WaitForExit();
            if ((pCommit?.ExitCode ?? 1) != 0)
            {
                writer.WriteLine("[ERROR] Failed to commit GxP snapshot.");
                return 1;
            }

            writer.WriteLine($"✔ Committed with '{GxpSnapshotPolicy.SnapshotTrailer}' trailer.");
        }
        else
        {
            writer.WriteLine(
                $"Commit with {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1 and a '{GxpSnapshotPolicy.SnapshotTrailer}' trailer " +
                $"(for example: git commit -m \"{GxpSnapshotPolicy.SnapshotCommitSubject}\" -m \"{GxpSnapshotPolicy.SnapshotTrailer}\") " +
                "so the pre-commit snapshot guard and CI snapshot provenance gate admit the documents.");
        }

        writer.WriteLine("\n============================================================");
        writer.WriteLine("✔ GxP snapshot complete.");
        writer.WriteLine("============================================================");
        return 0;
    }

    /// <summary>
    /// Verifies that every mandatory database test under <paramref name="repoRoot"/> executed and passed in the TRX files
    /// under <paramref name="resolvedResultsDir"/> (reporting <paramref name="displayResultsDir"/> on missing metadata)
    /// and that every database assembly recorded <c>*.database.txt</c> fixture metadata. When
    /// <paramref name="requireBrowserEvidence"/> is true, the browser-to-database correction test is also mandatory.
    /// </summary>
    public static bool VerifyDatabaseEvidence(string resolvedResultsDir, string displayResultsDir, string repoRoot, bool requireBrowserEvidence = false)
    {
        IReadOnlyList<SkippedDatabaseTest> skipped = DatabaseTestEvidence.FindSkipped(resolvedResultsDir, repoRoot, requireBrowserEvidence);
        if (skipped.Count > 0)
        {
            string browserHint = requireBrowserEvidence ? " (plus CADENCE_TEST_BROWSER=1 for browser-to-database evidence in snapshots)" : "";
            Console.WriteLine($"\n[ERROR] {skipped.Count} database test(s) did not execute. Set CADENCE_TEST_POSTGRES=<connection string> or CADENCE_TEST_DOCKER=1{browserHint} (docs/TESTING.md) and run again:");
            foreach (var test in skipped.Take(20))
            {
                Console.WriteLine($"  {test.FullName}: {test.Reason}");
            }

            if (skipped.Count > 20)
            {
                Console.WriteLine($"  ... and {skipped.Count - 20} more");
            }

            return false;
        }

        if (!DatabaseTestEvidence.HasRecordedEnvironment(resolvedResultsDir))
        {
            Console.WriteLine($"\n[ERROR] No PostgreSQL fixture evidence (*.database.txt) found under {displayResultsDir}. Ensure PostgreSQL 18 tests executed with {DatabaseTestEvidence.DatabaseAssemblies.Count} database assemblies and CADENCE_TEST_EVIDENCE_DIR set.");
            return false;
        }

        return true;
    }

    private static int RunProcess(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 1;
    }
}
