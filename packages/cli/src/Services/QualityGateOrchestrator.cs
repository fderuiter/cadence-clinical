using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Services;

/// <summary>Runs the gates of <see cref="QualityGates"/> for <c>cadence check</c>.</summary>
public static class QualityGateOrchestrator
{
    public static int RunQualityGates(
        string? gateName = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        string[]? excludedGates = null,
        string? repoRoot = null,
        string? baseRef = null,
        string? pushBefore = null,
        bool? failFast = null,
        string? eventName = null,
        string? gitRef = null,
        bool offline = false,
        string? callerHead = null)
    {
        AnsiConsole.MarkupLine("[bold cyan]Executing Cadence Quality Sentinels & Policy Gates...[/]\n");

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (excludedGates is { Length: > 0 })
        {
            if (!string.IsNullOrEmpty(gateName))
            {
                AnsiConsole.MarkupLine("[red]--exclude applies only when running all quality gates (omit the gate argument).[/]");
                return 1;
            }

            foreach (string raw in excludedGates.SelectMany(g => g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                var found = QualityGates.Find(raw);
                if (found is null)
                {
                    AnsiConsole.MarkupLine($"[red]Unknown quality gate '{Markup.Escape(raw)}'. Gates: {Markup.Escape(QualityGates.NameList)}.[/]");
                    return 1;
                }

                excluded.Add(found.Name);
            }
        }

        QualityGate? singleGate = null;
        if (!string.IsNullOrEmpty(gateName))
        {
            singleGate = QualityGates.Find(gateName);
            if (singleGate is null)
            {
                AnsiConsole.MarkupLine($"[red]Unknown quality gate '{Markup.Escape(gateName)}'. Gates: {Markup.Escape(QualityGates.NameList)}.[/]");
                return 1;
            }
        }

        repoRoot ??= FindRepoRoot();
        if (preCommit)
        {
            targetFiles ??= [.. PreCommitOpenApiService.GetStagedFiles(repoRoot)];
            using var indexReader = GitIndexReader.TryOpen(repoRoot);
            var preCommitContext = new GateContext(repoRoot, targetFiles, PreCommit: true, indexReader, baseRef, pushBefore, eventName, gitRef);
            if (singleGate is not null)
            {
                return singleGate.Run(preCommitContext) ? 0 : 1;
            }

            return RunAll(preCommitContext, excluded, failFast ?? true);
        }

        string executionRoot = repoRoot;
        string? effectiveBaseRef = baseRef;
        string? effectiveCallerHead = callerHead;
        string[]? effectiveTargetFiles = targetFiles;
        IDisposable? workspaceLease = null;

        if (GitRevisionScopeService.HasGitRepository(repoRoot) &&
            GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD") is not null)
        {
            var initialScope = GitRevisionScopeService.ResolveEventScope(repoRoot, effectiveBaseRef, pushBefore, eventName, gitRef);
            bool usesBranchBase = initialScope.Kind == GitEventScopeKind.BranchDiff || !string.IsNullOrWhiteSpace(baseRef);
            if (usesBranchBase)
            {
                effectiveCallerHead = GitRevisionScopeService.ResolveCallerPreMergeHead(repoRoot, callerHead);
                if (!offline)
                {
                    var fetch = GitRevisionScopeService.FetchBaseRef(repoRoot, effectiveBaseRef);
                    if (!fetch.Succeeded)
                    {
                        string normalizedRef = GitRevisionScopeService.NormalizeBaseRef(effectiveBaseRef);
                        Console.WriteLine(
                            $"[MERGE-READINESS] [ERROR] git fetch failed for '{normalizedRef}': {fetch.ErrorMessage} "
                            + "(pass --offline to evaluate against local Git refs only).");
                        return 1;
                    }

                    if (!string.IsNullOrWhiteSpace(fetch.TrackingRef))
                    {
                        effectiveBaseRef = fetch.TrackingRef;
                    }
                }

                int? behindCount = GitRevisionScopeService.TryGetBehindCount(repoRoot, effectiveBaseRef, "HEAD");
                if (behindCount is > 0 &&
                    singleGate?.PostBuild is not true &&
                    !TryPrepareMergedCheckWorktree(
                        repoRoot,
                        effectiveBaseRef,
                        targetFiles,
                        singleGate,
                        out executionRoot,
                        out effectiveTargetFiles,
                        out workspaceLease))
                {
                    return 1;
                }
            }
        }

        using (workspaceLease)
        {
            var context = new GateContext(
                executionRoot,
                effectiveTargetFiles,
                PreCommit: false,
                IndexReader: null,
                BaseRef: effectiveBaseRef,
                PushBefore: pushBefore,
                EventName: eventName,
                GitRef: gitRef,
                Offline: offline,
                CallerHead: effectiveCallerHead);

            if (singleGate is not null)
            {
                return singleGate.Run(context) ? 0 : 1;
            }

            bool effectiveFailFast = failFast ?? false;
            return RunAll(context, excluded, effectiveFailFast);
        }
    }

    private static bool TryPrepareMergedCheckWorktree(
        string callerRepoRoot,
        string? effectiveBaseRef,
        string[]? targetFiles,
        QualityGate? singleGate,
        out string worktreeRoot,
        out string[]? remappedTargetFiles,
        out IDisposable? lease)
    {
        worktreeRoot = callerRepoRoot;
        remappedTargetFiles = targetFiles;
        lease = null;

        string normalizedBase = GitRevisionScopeService.NormalizeBaseRef(effectiveBaseRef);
        string pinnedRef = GitRevisionScopeService.CreateTemporaryRefName();
        string headRevision = "HEAD";
        var dirtyPaths = GitRevisionScopeService.GetWorkingTreeChanges(callerRepoRoot);
        if (dirtyPaths.Count > 0)
        {
            var snapshot = GitRevisionScopeService.SynthesizeWorkingTreeSnapshot(callerRepoRoot, pinnedRef);
            if (!snapshot.Succeeded || string.IsNullOrEmpty(snapshot.SnapshotCommitSha))
            {
                GitRevisionScopeService.DeleteTemporaryRef(callerRepoRoot, pinnedRef);
                Console.WriteLine($"[MERGE-READINESS] [ERROR] Could not synthesize working-tree snapshot against '{normalizedBase}': {snapshot.ErrorMessage}");
                return false;
            }

            headRevision = snapshot.SnapshotCommitSha;
        }

        var synthesized = GitRevisionScopeService.SynthesizeMergedCommit(callerRepoRoot, normalizedBase, headRevision, pinnedRef);
        if (!synthesized.MergeTree.Succeeded || string.IsNullOrEmpty(synthesized.MergeCommitSha))
        {
            GitRevisionScopeService.DeleteTemporaryRef(callerRepoRoot, pinnedRef);
            Console.WriteLine($"[MERGE-READINESS] [ERROR] {synthesized.MergeTree.ErrorMessage}");
            if (synthesized.MergeTree.HasConflicts && synthesized.MergeTree.ConflictingFiles.Count > 0)
            {
                foreach (string conflictPath in synthesized.MergeTree.ConflictingFiles)
                {
                    Console.WriteLine($"  ✖ {conflictPath}");
                }
            }

            if (singleGate?.Name == "branch-freshness")
            {
                Console.WriteLine($"[BRANCH-FRESHNESS] Branch HEAD is behind '{normalizedBase}'. Rebase or merge '{normalizedBase}' before merging.");
            }

            return false;
        }

        if (!GitRevisionScopeService.TryCreateDetachedWorktree(
                callerRepoRoot,
                synthesized.MergeCommitSha,
                out var worktree,
                out string? worktreeError,
                pinnedRef: pinnedRef) ||
            worktree is null)
        {
            GitRevisionScopeService.DeleteTemporaryRef(callerRepoRoot, pinnedRef);
            Console.WriteLine($"[MERGE-READINESS] [ERROR] Could not materialize temporary merged worktree: {worktreeError}");
            return false;
        }

        string resolvedWorktreeRoot = worktree.WorktreePath;
        worktreeRoot = resolvedWorktreeRoot;
        var createdLinks = LinkReadOnlyToolchainsIntoWorktree(callerRepoRoot, resolvedWorktreeRoot);
        lease = new MergedCheckWorktreeLease(worktree, createdLinks);

        if (targetFiles is not null)
        {
            remappedTargetFiles =
            [
                .. targetFiles.Select(path =>
                {
                    if (!Path.IsPathRooted(path))
                    {
                        return path;
                    }

                    string relative = Path.GetRelativePath(callerRepoRoot, path);
                    return relative.StartsWith("..", StringComparison.Ordinal)
                        ? path
                        : Path.Combine(resolvedWorktreeRoot, relative);
                }),
            ];
        }

        string shortSha = synthesized.MergeCommitSha[..Math.Min(12, synthesized.MergeCommitSha.Length)];
        AnsiConsole.MarkupLine(
            $"[grey]Materialized temporary merged worktree {Markup.Escape(shortSha)} "
            + $"({synthesized.MergeTree.BehindCount} commit(s) behind {Markup.Escape(normalizedBase)}).[/]");
        return true;
    }

    private static List<string> LinkReadOnlyToolchainsIntoWorktree(string callerRepoRoot, string worktreeRoot)
    {
        var createdLinks = new List<string>();
        string[] candidateDirs =
        [
            ".cadence",
            ".venv",
            "node_modules",
            "apps/web/node_modules",
            "apps/subject-portal/node_modules",
            "packages/ui/node_modules",
            "docs/node_modules",
        ];

        foreach (string relativeDir in candidateDirs)
        {
            string sourceDir = Path.Combine(callerRepoRoot, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            string targetDir = Path.Combine(worktreeRoot, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(sourceDir) || Path.Exists(targetDir))
            {
                continue;
            }

            string? parentDir = Path.GetDirectoryName(targetDir);
            if (parentDir is null || !Directory.Exists(parentDir))
            {
                continue;
            }

            try
            {
                Directory.CreateSymbolicLink(targetDir, sourceDir);
                createdLinks.Add(targetDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // Best-effort symlink; PATH-installed tools remain usable when symlinks are restricted.
            }
        }

        return createdLinks;
    }

    private sealed class MergedCheckWorktreeLease(DetachedGitWorktree worktree, IReadOnlyList<string> createdLinks) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int i = createdLinks.Count - 1; i >= 0; i--)
            {
                string linkPath = createdLinks[i];
                try
                {
                    if (Path.Exists(linkPath))
                    {
                        Directory.Delete(linkPath, recursive: false);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort removal of symlink entry before worktree cleanup.
                }
            }

            worktree.Dispose();
        }
    }

    private static int RunAll(GateContext context, IReadOnlySet<string> excluded, bool failFast)
    {
        int total = QualityGates.All.Count;
        int failed = 0;
        for (int i = 0; i < total; i++)
        {
            var gate = QualityGates.All[i];
            AnsiConsole.MarkupLine($"\n[bold]{i + 1}/{total} {Markup.Escape(gate.Name)}[/]: {Markup.Escape(gate.Checks)}");
            if (excluded.Contains(gate.Name))
            {
                AnsiConsole.MarkupLine("Skipped: excluded via --exclude.");
                continue;
            }

            if (context.PreCommit && gate.PreCommit == PreCommitMode.Skipped)
            {
                AnsiConsole.MarkupLine($"Skipped: {Markup.Escape(gate.PreCommitNote ?? "")}.");
                continue;
            }

            if (!context.PreCommit && gate.PostBuild && !PublicBundleValidatorService.AnyDefaultBundleExists(context.RepoRoot))
            {
                AnsiConsole.MarkupLine($"Skipped: {Markup.Escape(gate.PostBuildSkipNote ?? "post-build output directories do not exist")}.");
                continue;
            }

            if (!gate.Run(context))
            {
                AnsiConsole.MarkupLine($"[red]Gate {i + 1} ({Markup.Escape(gate.Name)}) failed![/]");
                failed++;
                if (failFast)
                {
                    AnsiConsole.MarkupLine($"\n[bold red]Stopping after first failing quality gate ({Markup.Escape(gate.Name)}) due to fail-fast mode.[/]");
                    return 1;
                }
            }
        }

        if (failed > 0)
        {
            string suffix = excluded.Count > 0 ? $" ({excluded.Count} excluded)" : "";
            AnsiConsole.MarkupLine($"\n[bold red]{failed} of {total - excluded.Count} quality gates failed{suffix}.[/]");
            return 1;
        }

        if (excluded.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n[bold green]All {total - excluded.Count} non-excluded quality gates passed ({excluded.Count} excluded).[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"\n[bold green]All {total} quality gates passed.[/]");
        }

        return 0;
    }

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
