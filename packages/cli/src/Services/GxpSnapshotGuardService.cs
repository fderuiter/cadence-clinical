using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

using Cadence.Compliance;

namespace Cadence.Cli.Services;

/// <summary>
/// The <c>gxp-snapshot</c> gate (ADR-2532, #5151): a feature branch must not change the generated RTM, the IQ/OQ/PQ
/// report or the archived run reports. Only <c>gxp snapshot</c>, run with <c>CADENCE_GXP_SNAPSHOT=1</c> by the
/// integration owner, may.
/// </summary>
public static class GxpSnapshotGuardService
{
    /// <summary>The ref a branch is compared with, as in <c>cadence ci</c>.</summary>
    public const string DefaultBaseRef = "origin/main";

    /// <summary>
    /// Checks the staged files (<paramref name="preCommit"/>) or everything the branch changed since it left
    /// <paramref name="baseRef"/>, including uncommitted changes, and counts only the paths whose content differs from
    /// <paramref name="baseRef"/>, so merging main's snapshot into a branch passes. Returns 0 when no snapshot path changed or snapshot mode
    /// is on, and 1 otherwise. Outside the pre-commit hook, snapshot mode instead compares the regenerated documents with
    /// the snapshot (<see cref="GxpDocsDiffService.Compare"/>).
    /// </summary>
    public static int Check(string repoRoot, bool preCommit, string baseRef = DefaultBaseRef, bool? snapshotMode = null)
    {
        bool snapshot = snapshotMode ?? GxpSnapshotPolicy.IsSnapshotMode();

        // Taking a snapshot: CI compares the documents it regenerated with the ones staged for the snapshot, ignoring
        // the evidence block, and puts the staged copy back when only that block moved.
        if (snapshot && !preCommit)
        {
            Console.WriteLine($"[GXP SNAPSHOT] {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1: comparing the regenerated documents with the snapshot.");
            return GxpDocsDiffService.Compare(repoRoot, restoreWhenEquivalent: true);
        }

        IReadOnlyList<string> changed;
        if (preCommit)
        {
            changed = Git(repoRoot, "diff", "--cached", "--name-only") ?? [];
        }
        else
        {
            string? mergeBase = Git(repoRoot, "merge-base", baseRef, "HEAD")?.FirstOrDefault();
            changed = mergeBase is null
                ? Git(repoRoot, "diff", "HEAD", "--name-only") ?? []
                : Git(repoRoot, "diff", mergeBase, "--name-only") ?? [];
            if (mergeBase is null)
            {
                Console.WriteLine($"[GXP SNAPSHOT] {baseRef} not found; checking uncommitted changes only.");
            }
        }

        // A path whose content is exactly main's is the baseline's snapshot coming in, for example when the branch merges
        // main after the integration owner took a snapshot: only a copy that differs from the base is a branch's own change.
        var differsFromBase = preCommit
            ? Git(repoRoot, "diff", "--cached", "--name-only", baseRef)
            : Git(repoRoot, "diff", "--name-only", baseRef);
        if (differsFromBase is not null)
        {
            var differing = new HashSet<string>(differsFromBase, StringComparer.Ordinal);
            changed = [.. changed.Where(differing.Contains)];
        }

        return Evaluate(changed, snapshot, preCommit ? "staged" : $"changed since {baseRef}");
    }

    /// <summary>The decision, separated from git so it can be tested.</summary>
    public static int Evaluate(IEnumerable<string> changedPaths, bool snapshotMode, string scope)
    {
        var touched = GxpSnapshotPolicy.SnapshotPaths(changedPaths);
        if (touched.Count == 0)
        {
            Console.WriteLine("[GXP SNAPSHOT] No generated GxP snapshot is changed.");
            return 0;
        }

        if (snapshotMode)
        {
            Console.WriteLine($"[GXP SNAPSHOT] {GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1: admitting a new snapshot ({touched.Count} path(s)).");
            return 0;
        }

        Console.WriteLine($"[GXP SNAPSHOT] These generated documents are {scope}, but a feature branch never commits them (ADR-2532):");
        foreach (string path in touched.Take(20))
        {
            Console.WriteLine($"  {path}");
        }

        Console.WriteLine("  Restore them with `git checkout origin/main -- docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md docs/SDLC/runs`.");
        Console.WriteLine("  Validate your tags and results with `pnpm cadence gxp sync`; the integration owner takes the next snapshot with");
        Console.WriteLine($"  `{GxpSnapshotPolicy.SnapshotEnvironmentVariable}=1 pnpm cadence gxp snapshot`.");
        return 1;
    }

    private static List<string>? Git(string repoRoot, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return null;
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0
                ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
