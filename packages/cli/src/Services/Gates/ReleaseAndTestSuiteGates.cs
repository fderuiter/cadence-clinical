using System;
using System.Collections.Generic;
using System.IO;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services.Gates;

/// <summary>GxP RTM, baseline ratchets, merge/branch readiness, public bundle, and .NET test suite gates.</summary>
internal static class ReleaseAndTestSuiteGates
{
    public static IReadOnlyList<QualityGate> Entries { get; } =
    [
        new("rtm", ["gxp-rtm-validate"],
            "Requirement tags name known requirements and sit on test methods, and the generated documents match their integrity stamps and are not drafts",
            ctx => GxpRtmValidatorService.ValidateGxpRtm(ctx.RepoRoot)),
        new("gxp-snapshot", ["gxp-snapshot-guard"],
            "The branch or push range does not change the generated RTM, IQ/OQ/PQ report or `docs/SDLC/runs/` unless `CADENCE_GXP_SNAPSHOT=1` (ADR-2532)",
            ctx => GxpSnapshotGuardService.Check(ctx.RepoRoot, ctx.PreCommit, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks the staged files instead of the branch",
        },
        new("baseline-ratchet", ["baselines"],
            "No ratchet baseline gate (`*.baseline.txt`, `baselines/deid/*.baseline.txt`, `baselines/secrets/*.secrets.baseline.json`) gained an entry without a `Baseline-Growth: <path> - <reason>` trailer on the commit that grew it",
            ctx => BaselineRatchetService.Check(ctx.RepoRoot, ctx.PreCommit, baseRef: ctx.BaseRef, pushBefore: ctx.PushBefore) == 0)
        {
            PreCommit = PreCommitMode.Quick,
            PreCommitNote = "checks staged files against `HEAD`; `CADENCE_BASELINE_GROWTH=\"<reason>\"` admits growth",
        },
        new("merge-readiness", ["check-merge-readiness"],
            "Verifies `origin/main` (or `--base`) is present, shares history with `HEAD`, and merges cleanly via `git merge-tree --write-tree` without conflicts",
            CheckMergeReadiness)
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "skipped so pre-commit stays offline and fast; runs in `cadence check`, `cadence ci`, pull requests, and `merge_group`",
        },
        new("public-bundle", ["validate-public-bundle"],
            "Zero-exemption post-build scan of `docs/.vitepress/dist`, `apps/web/dist`, and `apps/subject-portal/dist` for national IDs, unapproved emails, secrets, external assets, executable local URLs, CSP meta headers, `axe-core`/`keycloak-js` demo contamination, forbidden modulepreloads, and staged demo byte budgets (skipped in unscoped `cadence check` only when all three `dist/` folders are absent; fails closed on partial `dist/` or direct `check public-bundle`)",
            ctx => PublicBundleValidatorService.ValidatePublicBundles(ctx.RepoRoot) == 0)
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "post-build gate; skipped in unscoped `check` only when all three `dist/` folders are absent, and enforced fail-closed in `docs-build` and `deploy-docs`",
            PostBuild = true,
            PostBuildSkipNote = "post-build gate; none of docs/.vitepress/dist, apps/web/dist, or apps/subject-portal/dist exist yet (build bundles or run 'pnpm cadence check public-bundle')",
        },
        new("branch-freshness", ["check-branch-freshness", "validate-branch-freshness"],
            "Branch `HEAD` includes `origin/main` (or `--base <ref>`) so local and PR runs enforce strict branch-freshness policy",
            ctx => CheckBranchFreshness(ctx))
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "skipped on commit so local work-in-progress commits stay offline; run by `cadence check` before push",
        },
        new("architecture-tests", ["arch-tests"],
            "Hexagonal layering and kernel boundary invariants (`tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj`)",
            ctx => CheckDotnetTestSuite(ctx.RepoRoot, "tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj"))
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "full xUnit architecture suite; run by `cadence check` and CI's `dotnet-quality` job",
        },
        new("unit-tests", ["dotnet-unit-tests"],
            ".NET unit test suite (`tests/Cadence.UnitTests/Cadence.UnitTests.csproj`)",
            ctx => CheckDotnetTestSuite(ctx.RepoRoot, "tests/Cadence.UnitTests/Cadence.UnitTests.csproj"))
        {
            PreCommit = PreCommitMode.Skipped,
            PreCommitNote = "full xUnit unit test suite; run by `cadence check` and CI's `dotnet-unit-tests` job",
        },
    ];

    private static bool CheckMergeReadiness(GateContext ctx)
    {
        if (ctx.PreCommit)
        {
            return true;
        }

        string effectiveBaseRef = GitRevisionScopeService.NormalizeBaseRef(ctx.BaseRef);
        if (!string.IsNullOrWhiteSpace(ctx.PushBefore) &&
            string.IsNullOrWhiteSpace(ctx.BaseRef) &&
            GitRevisionScopeService.TryResolveCommitSha(ctx.RepoRoot, "HEAD") is not null)
        {
            Console.WriteLine($"[MERGE-READINESS] Push scope (--push-before) verified on HEAD; skipping branch merge-tree check against '{effectiveBaseRef}'.");
            return true;
        }

        var preview = GitRevisionScopeService.PreviewMergeTree(ctx.RepoRoot, effectiveBaseRef, "HEAD");
        if (!preview.Succeeded)
        {
            Console.WriteLine($"[MERGE-READINESS] [ERROR] {preview.ErrorMessage}");
            if (preview.HasConflicts && preview.ConflictingFiles.Count > 0)
            {
                foreach (string conflictPath in preview.ConflictingFiles)
                {
                    Console.WriteLine($"  ✖ {conflictPath}");
                }
            }

            return false;
        }

        Console.WriteLine(
            $"[MERGE-READINESS] ✔ HEAD merges cleanly with '{preview.EffectiveBaseRef}' (behind by {preview.BehindCount} commit(s); tree {preview.TreeOid}).");
        return true;
    }

    private static bool CheckBranchFreshness(GateContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(ctx.PushBefore))
        {
            Console.WriteLine("Push-scoped run (--push-before): skipping branch-freshness ancestor check.");
            return true;
        }

        string baseRef = GitRevisionScopeService.NormalizeBaseRef(ctx.BaseRef);
        if (GateExecutionHelpers.RunQuietProcess(ctx.RepoRoot, "git", "rev-parse", "--verify", "--quiet", baseRef) != 0)
        {
            Console.WriteLine($"[BRANCH-FRESHNESS ERROR] '{baseRef}' was not found. Fetch the base branch so branch ancestry can be verified.");
            return false;
        }

        if (GateExecutionHelpers.RunQuietProcess(ctx.RepoRoot, "git", "merge-base", "--is-ancestor", baseRef, "HEAD") != 0)
        {
            Console.WriteLine($"[BRANCH-FRESHNESS] Branch HEAD is behind '{baseRef}'. Rebase or merge '{baseRef}' before merging (enforces strict status check policy locally).");
            return false;
        }

        Console.WriteLine($"✔ Branch HEAD includes {baseRef}.");
        return true;
    }

    private static bool CheckDotnetTestSuite(string repoRoot, string relativeProjectPath)
    {
        if (!GateExecutionHelpers.IsDotnetAvailable())
        {
            Console.WriteLine("[ERROR] .NET SDK ('dotnet') was not found on PATH.");
            return false;
        }

        string fullProjectPath = Path.Combine(repoRoot, relativeProjectPath);
        if (!File.Exists(fullProjectPath))
        {
            Console.WriteLine($"[ERROR] Required test project '{relativeProjectPath}' was not found.");
            return false;
        }

        string? projectDir = Path.GetDirectoryName(fullProjectPath);
        bool hasAssets = projectDir is not null && File.Exists(Path.Combine(projectDir, "obj", "project.assets.json"));
        string trxDir = Path.Combine(Path.GetTempPath(), $"cadence-gate-trx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(trxDir);
        try
        {
            int exitCode = hasAssets
                ? GateExecutionHelpers.RunProcess(repoRoot, "dotnet", "test", relativeProjectPath, "--no-restore", "--nologo", "--results-directory", trxDir, "--logger", "trx;LogFileName=gate.trx")
                : GateExecutionHelpers.RunProcess(repoRoot, "dotnet", "test", relativeProjectPath, "--nologo", "--results-directory", trxDir, "--logger", "trx;LogFileName=gate.trx");
            if (exitCode != 0)
            {
                return false;
            }

            if (!File.Exists(Path.Combine(trxDir, "gate.trx")))
            {
                Console.WriteLine($"[ERROR] Test suite '{relativeProjectPath}' produced no TRX test report (ensure IsTestProject is enabled).");
                return false;
            }

            return true;
        }
        finally
        {
            try
            {
                if (Directory.Exists(trxDir))
                {
                    Directory.Delete(trxDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup of temporary TRX directory.
            }
        }
    }
}
