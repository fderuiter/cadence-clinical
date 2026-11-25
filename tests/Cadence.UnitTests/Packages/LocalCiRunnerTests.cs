using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;
using Cadence.Compliance;
using Cadence.ToolingCore.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>How <c>cadence ci</c> (#5017) runs a plan: the order and skip rules of GitHub Actions, without its runners.</summary>
public class LocalCiRunnerTests
{
    private static readonly LocalCiOptions Defaults = new(AllowMissingTools: false, AllowDbSkip: false, BaseRef: "origin/main");

    private readonly List<string> _ran = [];
    private readonly Dictionary<string, int> _exitCodes = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _variables = [];
    private readonly Dictionary<string, string?> _files = [];

    private static CiCommand Cmd(string name) => new("tool", [name]);

    private LocalCiEnvironment Environment(
        bool postgres = true,
        bool docker = true,
        bool hasRecordedDatabaseEnvironment = true,
        CiContainerScanScope? containerSecurityScope = null,
        Func<string, bool>? tools = null,
        IReadOnlyList<string>? files = null,
        IReadOnlyList<SkippedDatabaseTest>? skippedDatabaseTests = null,
        Action<string, string?>? onFindSkippedDatabaseTests = null) => new()
        {
            RepoRoot = "/repo",
            Run = (command, extra, variables) =>
            {
                string name = command.Arguments.Count > 0 ? command.Arguments[0] : command.FileName;
                _ran.Add(extra.Count == 0 ? name : $"{name} {string.Join(' ', extra)}");
                _variables[name] = variables;
                if (_files.ContainsKey("/repo/report.md"))
                {
                    _files["/repo/report.md"] = "rewritten by " + name;
                }

                return _exitCodes.GetValueOrDefault(name);
            },
            ReadFile = path => _files.GetValueOrDefault(path),
            RestoreFile = (path, content) => _files[path] = content,
            IsToolOnPath = tools ?? (_ => true),
            ProbePostgres = () => new CiPostgresStatus(postgres, postgres ? "reachable." : "Neither CADENCE_TEST_POSTGRES nor CADENCE_TEST_DOCKER=1 is set."),
            ProbeDocker = () => new CiDockerStatus(docker, docker ? "Docker daemon is reachable." : "Docker daemon is not reachable (Cannot connect to the Docker daemon)."),
            ShouldRunContainerSecurity = _ => containerSecurityScope ?? new CiContainerScanScope(true, ""),
            FindFiles = (_, _) => files ?? [],
            DeleteDirectory = _ => { },
            FindSkippedDatabaseTests = (resultsPath, repoRoot) =>
            {
                onFindSkippedDatabaseTests?.Invoke(resultsPath, repoRoot);
                return skippedDatabaseTests ?? [];
            },
            HasRecordedDatabaseEnvironment = _ => hasRecordedDatabaseEnvironment,
        };

    private static CiJob Job(string key, params CiStep[] steps) => new(key, key, steps);

    [Fact]
    public void AFailedStep_StopsTheLaterStepsOfItsJob_ExceptThoseWithIfAlways_AndNotOtherJobs()
    {
        _exitCodes["build"] = 1;
        var jobs = new[]
        {
            Job("a",
                new CiStep("a", "build") { Commands = [Cmd("build")] },
                new CiStep("a", "pack") { Commands = [Cmd("pack")] },
                new CiStep("a", "lint") { Always = true, Commands = [Cmd("lint")] }),
            Job("b", new CiStep("b", "test") { Commands = [Cmd("test")] }),
        };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment());

        results.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.NotRun, CiOutcome.Passed, CiOutcome.Passed);
        _ran.Should().Equal("build", "lint", "test");
        results[0].Note.Should().Contain("exited 1");
    }

    [Fact]
    public void ACoveredStep_IsNotRunAgain_WhenItsCoveringStepPassed()
    {
        var jobs = new[]
        {
            Job("a",
                new CiStep("a", "all gates") { Commands = [Cmd("check")] },
                new CiStep("a", "one gate") { CoveredBy = "a/all gates", Commands = [Cmd("check-one")] }),
        };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment());

        results[1].Outcome.Should().Be(CiOutcome.Covered);
        results[1].Note.Should().Contain("all gates");
        _ran.Should().Equal("check");
    }

    [Fact]
    public void ACoveredStep_RunsItself_WhenItsCoveringStepFailedOrDidNotRun()
    {
        _exitCodes["check"] = 1;
        var jobs = new[]
        {
            Job("a",
                new CiStep("a", "all gates") { Always = true, Commands = [Cmd("check")] },
                new CiStep("a", "one gate") { Always = true, CoveredBy = "a/all gates", Commands = [Cmd("check-one")] },
                new CiStep("a", "other") { Always = true, CoveredBy = "z/not selected", Commands = [Cmd("other")] }),
        };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment());

        results.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.Passed, CiOutcome.Passed);
        _ran.Should().Equal("check", "check-one", "other");
    }

    [Fact]
    public void AStepWhoseToolIsMissing_FailsBeforeRunning_WithoutAllowMissingTools()
    {
        var jobs = new[]
        {
            Job("a",
                new CiStep("a", "lint docker") { RequiredTool = "hadolint", ToolHint = "install hadolint", Commands = [Cmd("hadolint")] },
                new CiStep("a", "next") { Commands = [Cmd("next")] }),
        };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment(tools: tool => tool != "hadolint"));

        results.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.NotRun);
        results[0].Note.Should().Contain("hadolint is not on PATH; install hadolint").And.Contain("--allow-missing-tools");
        _ran.Should().BeEmpty();
    }

    [Fact]
    public void AStepWhoseToolIsMissing_IsSkippedWithTheInstallHint_WithAllowMissingTools()
    {
        var jobs = new[]
        {
            Job("a",
                new CiStep("a", "lint docker") { RequiredTool = "hadolint", ToolHint = "install hadolint", Commands = [Cmd("hadolint")] },
                new CiStep("a", "next") { Commands = [Cmd("next")] }),
        };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowMissingTools = true }, Environment(tools: tool => tool != "hadolint"));

        results.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Passed);
        results[0].Note.Should().Be("hadolint is not on PATH; install hadolint");
        _ran.Should().Equal("next");
    }

    [Theory]
    [InlineData("actionlint", "actionlint")]
    [InlineData("hadolint", "lint-and-static")]
    [InlineData("shellcheck", "lint-and-static")]
    [InlineData("detect-secrets-hook", "security")]
    [InlineData("docker", "container-security")]
    [InlineData("trivy", "container-security")]
    public void LocalCiCommand_ExitsOneWhenARequiredToolIsMissing_UnlessAllowMissingToolsOrAllowSkipIsPassed(string missingTool, string jobKey)
    {
        var env = Environment(tools: tool => tool != missingTool, files: ["Dockerfile"]);

        int strictExit = LocalCiCommand.Execute([jobKey], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: false, environment: env);
        strictExit.Should().Be(1);

        int dbOnlyExit = LocalCiCommand.Execute([jobKey], allowMissingTools: false, allowDbSkip: true, baseRef: "origin/main", list: false, environment: env);
        dbOnlyExit.Should().Be(1, "--allow-db-skip must not bypass missing tool checks");

        int toolSkipExit = LocalCiCommand.Execute([jobKey], allowMissingTools: true, allowDbSkip: false, baseRef: "origin/main", list: false, environment: env);
        toolSkipExit.Should().Be(0);

        int shorthandExit = LocalCiCommand.Execute([jobKey], allowSkip: true, baseRef: "origin/main", list: false, environment: env);
        shorthandExit.Should().Be(0);

        var skipResults = LocalCiRunner.Run(CiPlan.Select([jobKey]), Defaults with { AllowMissingTools = true, RequestedJobs = [jobKey] }, env);
        skipResults.Should().ContainSingle(r => r.Step.RequiredTool == missingTool)
            .Which.Should().Match<CiStepResult>(r =>
                r.Outcome == CiOutcome.Skipped &&
                r.Note.StartsWith($"{missingTool} is not on PATH; ", StringComparison.Ordinal));
    }

    [Fact]
    public void AStepNotRunLocally_IsSkippedWithItsReason()
    {
        var results = LocalCiRunner.Run([Job("a", new CiStep("a", "docker") { NotRunLocally = "needs Docker" })], Defaults, Environment());

        results.Single().Should().Match<CiStepResult>(r => r.Outcome == CiOutcome.Skipped && r.Note == "needs Docker");
    }

    [Fact]
    public void DatabaseTests_FailBeforeRunning_WhenNoPostgreSQLIsReachable_EvenWithAllowMissingTools()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowMissingTools = true }, Environment(postgres: false));

        results.Single().Outcome.Should().Be(CiOutcome.Failed);
        results.Single().Note.Should().Contain("CADENCE_TEST_POSTGRES").And.Contain("--allow-db-skip");
        _ran.Should().BeEmpty("a run without a database would pass with every database test skipped");
    }

    [Fact]
    public void DatabaseTests_RunAsSkipped_WithAllowDbSkip()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowDbSkip = true }, Environment(postgres: false));

        results.Single().Outcome.Should().Be(CiOutcome.Passed);
        results.Single().Note.Should().StartWith("PostgreSQL tests skipped (--allow-db-skip)");
        _ran.Should().Equal("test");
    }

    /// <summary>
    /// Regression for #5325: with CADENCE_TEST_DOCKER=1 the kernel tests used to skip while the run reported green. A
    /// passing test step whose results show skipped database tests now fails.
    /// </summary>
    [Fact]
    public void DatabaseTests_ThatDidNotExecute_FailAPassingTestStep_EvenWithAllowMissingTools()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };
        var skipped = new[] { new SkippedDatabaseTest("Cadence.IntegrationTests.Kernel.RlsTests.Isolates", "Cadence.IntegrationTests.dll", "no database") };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowMissingTools = true }, Environment(skippedDatabaseTests: skipped));

        results.Single().Outcome.Should().Be(CiOutcome.Failed);
        results.Single().Note.Should().Contain("1 database test(s) did not execute").And.Contain("RlsTests.Isolates").And.Contain("--allow-db-skip");
        _variables["test"].Should().ContainKey("CADENCE_TEST_EVIDENCE_DIR");
    }

    [Fact]
    public void DatabaseTests_ThatDidNotExecute_AreAccepted_WithAllowDbSkip()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };
        var skipped = new[] { new SkippedDatabaseTest("Cadence.IntegrationTests.Kernel.RlsTests.Isolates", "Cadence.IntegrationTests.dll", "no database") };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowDbSkip = true }, Environment(skippedDatabaseTests: skipped));

        results.Single().Outcome.Should().Be(CiOutcome.Passed);
    }

    [Fact]
    public void AJobWhoseNeededStepFailed_DoesNotRun()
    {
        _exitCodes["test"] = 1;
        var jobs = new[]
        {
            Job("tests", new CiStep("tests", "run") { Commands = [Cmd("test")] }),
            new CiJob("report", "report", [new CiStep("report", "rtm") { Commands = [Cmd("rtm")] }])
            {
                Needs = ["tests"],
                NeedsStep = new Dictionary<string, string> { ["tests"] = "tests/run" },
            },
        };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment());

        results[1].Outcome.Should().Be(CiOutcome.NotRun);
        results[1].Note.Should().Contain("tests/run");
        _ran.Should().Equal("test");
    }

    [Fact]
    public void FilesAreAppendedToTheLastCommand_AndAStepWithNoFilesPassesWithItsMessage()
    {
        var step = new CiStep("a", "scan")
        {
            Files = CiFileSource.ChangedFiles,
            NoFilesMessage = "No changed files to scan.",
            Commands = [Cmd("prepare"), Cmd("scan")],
        };

        LocalCiRunner.Run([Job("a", step)], Defaults, Environment(files: ["a.cs", "b.md"]));
        _ran.Should().Equal("prepare", "scan a.cs b.md");

        _ran.Clear();
        var results = LocalCiRunner.Run([Job("a", step)], Defaults, Environment(files: []));
        results.Single().Should().Match<CiStepResult>(r => r.Outcome == CiOutcome.Passed && r.Note == "No changed files to scan.");
        _ran.Should().BeEmpty();
    }

    [Fact]
    public void BaseRef_IsForwardedToCliCheckCommands_AndInProcessSteps()
    {
        string? observedInProcessBaseRef = null;
        var checkCommand = new CiCommand("node", ["scripts/run-cli.js", "check", "deid"], ForwardsBaseRef: true);
        var job = Job("custom",
            new CiStep("custom", "cli-check") { Commands = [checkCommand] },
            new CiStep("custom", "in-process")
            {
                InProcess = (_, options) =>
                {
                    observedInProcessBaseRef = options.BaseRef;
                    return 0;
                },
                InProcessDisplay = "in-process check",
                InProcessCiText = "node scripts/run-cli.js check gxp-snapshot",
            });

        var results = LocalCiRunner.Run([job], new LocalCiOptions(AllowMissingTools: false, AllowDbSkip: false, BaseRef: "origin/release/1.0"), Environment());

        results.Select(r => r.Outcome).Should().Equal(CiOutcome.Passed, CiOutcome.Passed);
        _ran.Should().Equal("scripts/run-cli.js --base origin/release/1.0");
        observedInProcessBaseRef.Should().Be("origin/release/1.0");
    }

    [Fact]
    public void AnEnvironmentVariable_IsPassedOnlyWhenItsStepPassed()
    {
        var docs = new CiStep("a", "docs")
        {
            Always = true,
            EnvironmentIfPassed = [new CiEnvironmentIfPassed("a/all gates", "SKIP_CHECKS", "1")],
            Commands = [Cmd("docs")],
        };

        LocalCiRunner.Run([Job("a", new CiStep("a", "all gates") { Commands = [Cmd("check")] }, docs)], Defaults, Environment());
        _variables["docs"].Should().Equal(new Dictionary<string, string> { ["SKIP_CHECKS"] = "1" });

        _exitCodes["check"] = 1;
        LocalCiRunner.Run([Job("a", new CiStep("a", "all gates") { Commands = [Cmd("check")] }, docs)], Defaults, Environment());
        _variables["docs"].Should().BeEmpty("the docs build must run the checks itself when they did not pass earlier");
    }

    [Fact]
    public void StepEnvironmentVariables_AreAlwaysPassedToCommands()
    {
        var browser = new CiStep("e2e", "browser")
        {
            Environment = new Dictionary<string, string> { ["CADENCE_TEST_BROWSER"] = "1" },
            Commands = [Cmd("browser-test")],
        };

        LocalCiRunner.Run([Job("e2e", browser)], Defaults, Environment());

        _variables["browser-test"].Should().Equal(new Dictionary<string, string> { ["CADENCE_TEST_BROWSER"] = "1" });
    }

    [Fact]
    public void RestoreFiles_ArePutBackAfterTheStep_EvenWhenItFails()
    {
        _files["/repo/report.md"] = "committed";
        var step = new CiStep("a", "docs") { RestoreFiles = ["report.md"], Commands = [Cmd("docs")] };

        LocalCiRunner.Run([Job("a", step)], Defaults, Environment());
        _files["/repo/report.md"].Should().Be("committed");

        _exitCodes["docs"] = 1;
        var results = LocalCiRunner.Run([Job("a", step)], Defaults, Environment());
        results.Single().Outcome.Should().Be(CiOutcome.Failed);
        _files["/repo/report.md"].Should().Be("committed");
    }

    [Fact]
    public void PrepareWorkspace_FailsClosedOnDirtyCheckout_SupportsIncludeUncommitted_AndCleansUpMergedWorktreeAndPinnedRef()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempBase = Path.Combine(Path.GetTempPath(), $"local-ci-merged-{Guid.NewGuid():N}");
        string remoteDir = Path.Combine(tempBase, "origin.git");
        string repoDir = Path.Combine(tempBase, "caller");
        string peerDir = Path.Combine(tempBase, "peer");
        try
        {
            Directory.CreateDirectory(remoteDir);
            RunGit(remoteDir, "init", "-q", "--bare", "-b", "main");

            Directory.CreateDirectory(repoDir);
            RunGit(repoDir, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(repoDir, ".gitignore"), ".cadence/\n");
            File.WriteAllText(Path.Combine(repoDir, "base.txt"), "base\n");
            RunGit(repoDir, "add", "-A");
            RunGit(repoDir, "commit", "-q", "-m", "initial");
            RunGit(repoDir, "remote", "add", "origin", remoteDir);
            RunGit(repoDir, "push", "-q", "-u", "origin", "main");

            // Create a read-only .cadence/bin tool in the caller checkout (gitignored, like production).
            string cadenceBinDir = Path.Combine(repoDir, ".cadence", "bin");
            Directory.CreateDirectory(cadenceBinDir);
            string fakeToolPath = Path.Combine(cadenceBinDir, "cadence-test-linter");
            File.WriteAllText(fakeToolPath, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(fakeToolPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            RunGit(repoDir, "checkout", "-q", "-b", "feature");
            File.WriteAllText(Path.Combine(repoDir, "feature.txt"), "from-feature\n");
            RunGit(repoDir, "add", "feature.txt");
            RunGit(repoDir, "commit", "-q", "-m", "feature commit");

            // Advance origin/main in peer clone so caller's local origin/main is stale until fetched.
            RunGit(tempBase, "clone", "-q", remoteDir, peerDir);
            File.WriteAllText(Path.Combine(peerDir, "upstream.txt"), "from-upstream\n");
            RunGit(peerDir, "add", "upstream.txt");
            RunGit(peerDir, "commit", "-q", "-m", "upstream commit");
            RunGit(peerDir, "push", "-q", "origin", "main");

            int executedCount = 0;
            string? observedWorktreeRoot = null;
            bool sawFeatureAndUpstream = false;
            bool sawDirtyCallerFile = false;
            bool sawPinnedRefDuringRun = false;
            var job = Job("verify",
                new CiStep("verify", "check-merged")
                {
                    RequiredTool = "cadence-test-linter",
                    ToolHint = "provision .cadence/bin",
                    Commands = [new CiCommand("cadence-test-linter", [])],
                },
                new CiStep("verify", "inspect-tree")
                {
                    InProcess = (worktreeRoot, _) =>
                    {
                        executedCount++;
                        observedWorktreeRoot = worktreeRoot;
                        sawFeatureAndUpstream =
                            File.Exists(Path.Combine(worktreeRoot, "feature.txt")) &&
                            File.Exists(Path.Combine(worktreeRoot, "upstream.txt"));
                        sawDirtyCallerFile = File.Exists(Path.Combine(worktreeRoot, "dirty-caller.txt"));
                        sawPinnedRefDuringRun = !string.IsNullOrWhiteSpace(
                            CaptureGit(repoDir, "for-each-ref", "--format=%(refname)", GitRevisionScopeService.TemporaryRefPrefix));
                        string dirtyText = sawDirtyCallerFile
                            ? File.ReadAllText(Path.Combine(worktreeRoot, "dirty-caller.txt"))
                            : string.Empty;
                        if (dirtyText.Contains("BROKEN", StringComparison.Ordinal))
                        {
                            return 1;
                        }

                        File.WriteAllText(Path.Combine(worktreeRoot, "ephemeral-build.out"), "generated\n");
                        return 0;
                    },
                    InProcessDisplay = "inspect synthesized worktree",
                    InProcessCiText = "echo ok",
                });

            var defaultOptions = new LocalCiOptions(AllowSkip: false, BaseRef: "origin/main", Offline: false, IncludeUncommitted: false);

            // 1. Untracked file fails closed on merge-readiness before any job executes.
            File.WriteAllText(Path.Combine(repoDir, "dirty-caller.txt"), "untracked-edit\n");
            var untrackedFail = LocalCiRunner.Run([job], defaultOptions, LocalCiEnvironment.Create(repoDir));
            executedCount.Should().Be(0);
            untrackedFail[0].Step.Job.Should().Be("merge-readiness");
            untrackedFail[0].Outcome.Should().Be(CiOutcome.Failed);
            untrackedFail[0].Note.Should().Contain("dirty-caller.txt")
                .And.Contain("--include-uncommitted");
            LocalCiCommand.Execute(
                    ["actionlint"],
                    allowMissingTools: false,
                    allowDbSkip: false,
                    baseRef: "origin/main",
                    list: false,
                    environment: LocalCiEnvironment.Create(repoDir),
                    offline: false,
                    includeUncommitted: false)
                .Should().Be(1);
            File.Delete(Path.Combine(repoDir, "dirty-caller.txt"));

            // 2. Unstaged modification fails closed on merge-readiness.
            File.WriteAllText(Path.Combine(repoDir, "feature.txt"), "unstaged-edit\n");
            var unstagedFail = LocalCiRunner.Run([job], defaultOptions, LocalCiEnvironment.Create(repoDir));
            executedCount.Should().Be(0);
            unstagedFail[0].Outcome.Should().Be(CiOutcome.Failed);
            unstagedFail[0].Note.Should().Contain("feature.txt");

            // 3. Staged modification fails closed on merge-readiness.
            RunGit(repoDir, "add", "feature.txt");
            var stagedFail = LocalCiRunner.Run([job], defaultOptions, LocalCiEnvironment.Create(repoDir));
            executedCount.Should().Be(0);
            stagedFail[0].Outcome.Should().Be(CiOutcome.Failed);
            stagedFail[0].Note.Should().Contain("feature.txt");

            // 4. --include-uncommitted validates uncommitted edits (including failing when an uncommitted file is broken)
            //    without mutating caller HEAD, .git/index, or working tree.
            File.WriteAllText(Path.Combine(repoDir, "dirty-caller.txt"), "BROKEN uncommitted edit\n");
            string headBefore = CaptureGit(repoDir, "rev-parse", "HEAD").Trim();
            string brokenStatusBefore = CaptureGit(repoDir, "status", "--porcelain=v1");
            byte[] brokenIndexBefore = File.ReadAllBytes(Path.Combine(repoDir, ".git", "index"));

            var includeBrokenOptions = defaultOptions with { IncludeUncommitted = true };
            var brokenResults = LocalCiRunner.Run([job], includeBrokenOptions, LocalCiEnvironment.Create(repoDir));
            brokenResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Passed, CiOutcome.Failed);
            executedCount.Should().Be(1);
            CaptureGit(repoDir, "status", "--porcelain=v1").Should().Be(brokenStatusBefore);
            File.ReadAllBytes(Path.Combine(repoDir, ".git", "index")).Should().Equal(brokenIndexBefore);

            File.WriteAllText(Path.Combine(repoDir, "dirty-caller.txt"), "valid uncommitted edit\n");
            string statusBefore = CaptureGit(repoDir, "status", "--porcelain=v1");
            byte[] indexBefore = File.ReadAllBytes(Path.Combine(repoDir, ".git", "index"));

            var results = LocalCiRunner.Run([job], includeBrokenOptions, LocalCiEnvironment.Create(repoDir));
            results.Select(r => r.Outcome).Should().Equal(CiOutcome.Passed, CiOutcome.Passed);

            observedWorktreeRoot.Should().NotBeNull();
            observedWorktreeRoot.Should().StartWith(Path.GetTempPath());
            observedWorktreeRoot.Should().NotBe(repoDir);
            sawFeatureAndUpstream.Should().BeTrue("merged worktree must contain both feature snapshot and fetched origin/main");
            sawDirtyCallerFile.Should().BeTrue("--include-uncommitted must synthesize uncommitted edits into the merged worktree");
            sawPinnedRefDuringRun.Should().BeTrue("synthesized commit must be pinned under refs/cadence-ci/<runId> while worktree is active");
            Directory.Exists(observedWorktreeRoot!).Should().BeFalse("temporary detached worktree must be removed after run");
            CaptureGit(repoDir, "for-each-ref", "--format=%(refname)", GitRevisionScopeService.TemporaryRefPrefix).Trim()
                .Should().BeEmpty("pinned refs/cadence-ci/<runId> ref must be deleted on worktree disposal");
            Directory.Exists(Path.Combine(repoDir, ".git", "logs", "refs", "cadence-ci"))
                .Should().BeFalse("temporary refs must not leave reflog entries");
            CaptureGit(repoDir, "rev-parse", "HEAD").Trim().Should().Be(headBefore);
            CaptureGit(repoDir, "status", "--porcelain=v1").Should().Be(statusBefore);
            File.ReadAllBytes(Path.Combine(repoDir, ".git", "index")).Should().Equal(indexBefore);
            File.Exists(Path.Combine(repoDir, "ephemeral-build.out")).Should().BeFalse("caller checkout must not be mutated");
            File.Exists(Path.Combine(repoDir, "upstream.txt")).Should().BeFalse("caller branch checkout must remain on feature HEAD");
        }
        finally
        {
            DeleteTempTree(tempBase);
        }
    }

    [Fact]
    public void PrepareWorkspace_PropagatesFetchedTrackingRef_AndResolvesLocalTagAndRelativeRefsWithoutGitFetch()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempBase = Path.Combine(Path.GetTempPath(), $"local-ci-baseref-{Guid.NewGuid():N}");
        string remoteDir = Path.Combine(tempBase, "origin.git");
        string repoDir = Path.Combine(tempBase, "caller");
        string peerDir = Path.Combine(tempBase, "peer");
        string capturedArgsFile = Path.Combine(tempBase, "captured-args.txt");
        try
        {
            Directory.CreateDirectory(remoteDir);
            RunGit(remoteDir, "init", "-q", "--bare", "-b", "main");

            Directory.CreateDirectory(repoDir);
            RunGit(repoDir, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(repoDir, ".gitignore"), ".cadence/\n");
            File.WriteAllText(Path.Combine(repoDir, "base.txt"), "base\n");
            RunGit(repoDir, "add", "-A");
            RunGit(repoDir, "commit", "-q", "-m", "initial");
            RunGit(repoDir, "tag", "v1.0.0");
            RunGit(repoDir, "remote", "add", "origin", remoteDir);
            RunGit(repoDir, "push", "-q", "-u", "origin", "main");

            string cadenceBinDir = Path.Combine(repoDir, ".cadence", "bin");
            Directory.CreateDirectory(cadenceBinDir);
            string fakeCheck = Path.Combine(cadenceBinDir, "cadence-capture-args");
            File.WriteAllText(fakeCheck, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > \"{capturedArgsFile}\"\nexit 0\n");
            File.SetUnixFileMode(fakeCheck, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            RunGit(repoDir, "checkout", "-q", "-b", "feature");
            File.WriteAllText(Path.Combine(repoDir, "docs.md"), "feature docs\n");
            RunGit(repoDir, "add", "docs.md");
            RunGit(repoDir, "commit", "-q", "-m", "feature commit");
            string parentSha = CaptureGit(repoDir, "rev-parse", "HEAD~1").Trim();

            // Advance remote main in peer clone with a Dockerfile edit; local branch 'main' in caller remains stale at initial!
            // Because feature did NOT touch Dockerfile relative to refs/remotes/origin/main, container-security must be skipped.
            RunGit(tempBase, "clone", "-q", remoteDir, peerDir);
            File.WriteAllText(Path.Combine(peerDir, "Dockerfile"), "FROM scratch\n");
            File.WriteAllText(Path.Combine(peerDir, "upstream-remote.txt"), "remote\n");
            RunGit(peerDir, "add", "Dockerfile", "upstream-remote.txt");
            RunGit(peerDir, "commit", "-q", "-m", "remote main advance");
            RunGit(peerDir, "push", "-q", "origin", "main");

            string? observedInProcessBase = null;
            bool sawRemoteUpstreamFile = false;

            var jobs = new[]
            {
                Job("container-security", new CiStep("container-security", "scan")
                {
                    NeedsDocker = true,
                    Commands = [new CiCommand("cadence-capture-args", ["docker"])],
                }),
                Job("verify",
                    new CiStep("verify", "scoped-check")
                    {
                        Files = CiFileSource.ChangedFilesAcmr,
                        Commands = [new CiCommand("cadence-capture-args", [], ForwardsBaseRef: true)],
                    },
                    new CiStep("verify", "in-process")
                    {
                        InProcess = (worktreeRoot, opts) =>
                        {
                            observedInProcessBase = opts.BaseRef;
                            sawRemoteUpstreamFile = File.Exists(Path.Combine(worktreeRoot, "upstream-remote.txt"));
                            return 0;
                        },
                        InProcessDisplay = "inspect active base ref",
                        InProcessCiText = "echo base",
                    }),
            };

            // Passing --base main online must fetch origin/main and propagate refs/remotes/origin/main everywhere.
            var mainBranchResults = LocalCiRunner.Run(
                jobs,
                new LocalCiOptions(AllowSkip: false, BaseRef: "main", Offline: false),
                LocalCiEnvironment.Create(repoDir));
            mainBranchResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Passed, CiOutcome.Passed);
            sawRemoteUpstreamFile.Should().BeTrue("merged worktree must use fetched refs/remotes/origin/main rather than stale local 'main'");
            observedInProcessBase.Should().Be("refs/remotes/origin/main");
            File.ReadAllLines(capturedArgsFile).Should().Equal("--base", "refs/remotes/origin/main", "docs.md");

            // Point origin to a missing path so any accidental git fetch fails, then verify --base HEAD~1 and --base v1.0.0 work online.
            RunGit(repoDir, "remote", "set-url", "origin", Path.Combine(tempBase, "missing.git"));

            var headParentResults = LocalCiRunner.Run(
                jobs,
                new LocalCiOptions(AllowSkip: false, BaseRef: "HEAD~1", Offline: false),
                LocalCiEnvironment.Create(repoDir));
            headParentResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Passed, CiOutcome.Passed);
            observedInProcessBase.Should().Be(parentSha);
            File.ReadAllLines(capturedArgsFile).Should().Equal("--base", parentSha, "docs.md");

            var tagResults = LocalCiRunner.Run(
                jobs,
                new LocalCiOptions(AllowSkip: false, BaseRef: "v1.0.0", Offline: false),
                LocalCiEnvironment.Create(repoDir));
            tagResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Passed, CiOutcome.Passed);
            observedInProcessBase.Should().Be("refs/tags/v1.0.0");
            File.ReadAllLines(capturedArgsFile).Should().Equal("--base", "refs/tags/v1.0.0", "docs.md");
        }
        finally
        {
            DeleteTempTree(tempBase);
        }
    }

    [Fact]
    public void NativeAotBuild_PublishesWorktreeBinAndReusesBinaryAcrossNodeCliStepsWithoutRebuild()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tempWorktree = Path.Combine(Path.GetTempPath(), $"local-ci-aot-reuse-{Guid.NewGuid():N}");
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        try
        {
            Directory.CreateDirectory(Path.Combine(tempWorktree, "scripts"));
            File.Copy(
                Path.Combine(repoRoot, "scripts", "run-cli.js"),
                Path.Combine(tempWorktree, "scripts", "run-cli.js"));
            File.Copy(
                Path.Combine(repoRoot, "global.json"),
                Path.Combine(tempWorktree, "global.json"));

            string invocationsLog = Path.Combine(tempWorktree, "cli-invocations.log");
            var downstreamStep = CiPlan.Jobs.Single(j => j.Key == "lint-and-static")
                .Steps.Single(s => s.Name == "Run Markdown validation");

            // Simulate native-aot-build publishing ./bin/cadence inside the worktree, followed by real node scripts/run-cli.js execution.
            var simAotStep = new CiStep("native-aot-build", "Publish the CLI as Native AOT")
            {
                InProcess = (wt, _) =>
                {
                    string binDir = Path.Combine(wt, "bin");
                    Directory.CreateDirectory(binDir);
                    string cadenceBin = Path.Combine(binDir, "cadence");
                    File.WriteAllText(cadenceBin, $"#!/bin/sh\necho \"$*\" >> \"{invocationsLog}\"\nexit 0\n");
                    File.SetUnixFileMode(cadenceBin, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    // Make source tree newer than bin/cadence so run-cli.js would rebuild without CADENCE_CLI_CACHE_HIT=true!
                    string cliSrcDir = Path.Combine(wt, "packages", "cli", "src");
                    Directory.CreateDirectory(cliSrcDir);
                    string newerSource = Path.Combine(cliSrcDir, "Program.cs");
                    File.WriteAllText(newerSource, "// newer source\n");
                    DateTime future = File.GetLastWriteTimeUtc(cadenceBin).AddMinutes(5);
                    File.SetLastWriteTimeUtc(newerSource, future);
                    return 0;
                },
                InProcessDisplay = "simulate native aot publish to ./bin",
                InProcessCiText = "dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64 -o ./bin",
            };

            string capturedStdout = string.Empty;
            IReadOnlyDictionary<string, string>? observedEnv = null;
            var env = new LocalCiEnvironment
            {
                RepoRoot = tempWorktree,
                IsToolOnPath = _ => true,
                ProbePostgres = () => new CiPostgresStatus(true, "reachable."),
                FindFiles = (_, _) => [],
                Run = (command, extra, variables) =>
                {
                    observedEnv = variables;
                    var psi = new ProcessStartInfo(command.FileName)
                    {
                        WorkingDirectory = tempWorktree,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    psi.Environment.Remove("GITHUB_ACTIONS");
                    foreach (string arg in command.Arguments.Concat(extra))
                    {
                        psi.ArgumentList.Add(arg);
                    }

                    foreach (var (key, value) in variables)
                    {
                        psi.Environment[key] = value;
                    }

                    using var proc = Process.Start(psi)!;
                    capturedStdout += proc.StandardOutput.ReadToEnd();
                    proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    return proc.ExitCode;
                },
            };

            var jobs = new[]
            {
                Job("native-aot-build", simAotStep),
                Job("lint-and-static", downstreamStep),
            };

            var results = LocalCiRunner.Run(jobs, Defaults, env);
            results.Select(r => r.Outcome).Should().Equal(CiOutcome.Passed, CiOutcome.Passed);
            observedEnv.Should().NotBeNull();
            observedEnv!.Should().Contain(new KeyValuePair<string, string>("CADENCE_CLI_CACHE_HIT", "true"));
            capturedStdout.Should().NotContain("[run-cli] Compiling Cadence CLI binary...");
            File.ReadAllLines(invocationsLog).Should().Equal("check markdown");
        }
        finally
        {
            DeleteTempTree(tempWorktree);
        }
    }

    [Fact]
    public void PrepareWorkspace_FailsClosedWithConflictedFilePaths_AndSupportsOfflineFlag()
    {
        string tempBase = Path.Combine(Path.GetTempPath(), $"local-ci-conflict-{Guid.NewGuid():N}");
        string remoteDir = Path.Combine(tempBase, "origin.git");
        string repoDir = Path.Combine(tempBase, "caller");
        string peerDir = Path.Combine(tempBase, "peer");
        try
        {
            Directory.CreateDirectory(remoteDir);
            RunGit(remoteDir, "init", "-q", "--bare", "-b", "main");

            Directory.CreateDirectory(repoDir);
            RunGit(repoDir, "init", "-q", "-b", "main");
            File.WriteAllText(Path.Combine(repoDir, "shared.cs"), "class Shared { int X = 1; }\n");
            RunGit(repoDir, "add", "-A");
            RunGit(repoDir, "commit", "-q", "-m", "base");
            RunGit(repoDir, "remote", "add", "origin", remoteDir);
            RunGit(repoDir, "push", "-q", "-u", "origin", "main");

            RunGit(repoDir, "checkout", "-q", "-b", "feature");
            File.WriteAllText(Path.Combine(repoDir, "shared.cs"), "class Shared { int X = 2; }\n");
            RunGit(repoDir, "commit", "-q", "-am", "feature edit");

            RunGit(tempBase, "clone", "-q", remoteDir, peerDir);
            File.WriteAllText(Path.Combine(peerDir, "shared.cs"), "class Shared { int X = 3; }\n");
            RunGit(peerDir, "commit", "-q", "-am", "upstream conflicting edit");
            RunGit(peerDir, "push", "-q", "origin", "main");

            bool jobExecuted = false;
            var job = Job("verify", new CiStep("verify", "step")
            {
                InProcess = (_, _) =>
                {
                    jobExecuted = true;
                    return 0;
                },
                InProcessDisplay = "run step",
                InProcessCiText = "echo step",
            });

            // With --offline, git fetch is skipped so local origin/main (still at base) merges cleanly.
            var offlineOptions = new LocalCiOptions(AllowSkip: false, BaseRef: "origin/main", Offline: true);
            var offlinePrep = LocalCiEnvironment.PrepareMergedWorktree(repoDir, offlineOptions);
            using (offlinePrep.Lease)
            {
                offlinePrep.Succeeded.Should().BeTrue(offlinePrep.Message);
            }

            // Without --offline, git fetch pulls conflicting origin/main and fails closed before any job executes.
            var onlineOptions = new LocalCiOptions(AllowSkip: false, BaseRef: "origin/main", Offline: false);
            var conflictResults = LocalCiRunner.Run([job], onlineOptions, LocalCiEnvironment.Create(repoDir));
            jobExecuted.Should().BeFalse("no job step may run when merge-tree detects conflicts");
            conflictResults[0].Outcome.Should().Be(CiOutcome.Failed);
            conflictResults[0].Note.Should().Contain("shared.cs");
            conflictResults[1].Outcome.Should().Be(CiOutcome.NotRun);

            // Missing remote fails closed when online.
            RunGit(repoDir, "remote", "set-url", "origin", Path.Combine(tempBase, "does-not-exist.git"));
            var fetchFailResults = LocalCiRunner.Run([job], onlineOptions, LocalCiEnvironment.Create(repoDir));
            jobExecuted.Should().BeFalse("no job step may run when git fetch fails");
            fetchFailResults[0].Outcome.Should().Be(CiOutcome.Failed);
            fetchFailResults[0].Note.Should().Contain("git fetch failed");
        }
        finally
        {
            DeleteTempTree(tempBase);
        }
    }

    [Fact]
    public void ContainerSecurity_FailsWhenDockerDaemonIsUnreachable_AndSkipsWithAllowMissingToolsOrAllowSkip()
    {
        var jobs = CiPlan.Select(["container-security"]);
        var env = Environment(docker: false);

        var strictResults = LocalCiRunner.Run(jobs, Defaults with { RequestedJobs = ["container-security"] }, env);
        strictResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.NotRun, CiOutcome.NotRun, CiOutcome.Skipped);
        strictResults[0].Note.Should().Contain("Docker daemon is not reachable").And.Contain("--allow-missing-tools");
        _ran.Should().BeEmpty();

        var dbSkipOnlyResults = LocalCiRunner.Run(jobs, Defaults with { AllowDbSkip = true, RequestedJobs = ["container-security"] }, env);
        dbSkipOnlyResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.NotRun, CiOutcome.NotRun, CiOutcome.Skipped);

        var skipResults = LocalCiRunner.Run(jobs, Defaults with { AllowMissingTools = true, RequestedJobs = ["container-security"] }, env);
        skipResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Skipped, CiOutcome.Skipped, CiOutcome.Skipped);
        skipResults[0].Note.Should().Contain("Docker daemon unavailable (--allow-missing-tools)");
        skipResults[1].Note.Should().Contain("Docker daemon unavailable (--allow-missing-tools)");
        skipResults[2].Note.Should().Contain("Docker daemon unavailable (--allow-missing-tools)");
        _ran.Should().BeEmpty();
    }

    [Fact]
    public void ContainerSecurity_ValidatesComposeAndBuildsAndScansAllTwentyImages_AndRespectsDiffScopeUnlessExplicitlyRequested()
    {
        var jobs = CiPlan.Select(["container-security"]);
        var skippedScope = new CiContainerScanScope(false, "no container or dependency changes against origin/main (pass --job container-security to force)");
        var envWhenNoDiffAndMissingTools = Environment(
            docker: false,
            tools: _ => false,
            containerSecurityScope: skippedScope);

        var defaultRunResults = LocalCiRunner.Run(jobs, Defaults, envWhenNoDiffAndMissingTools);
        defaultRunResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Skipped, CiOutcome.Skipped, CiOutcome.Skipped);
        defaultRunResults[0].Note.Should().Contain("no container or dependency changes");

        var capturedCommands = new List<string>();
        var readyEnv = new LocalCiEnvironment
        {
            RepoRoot = "/repo",
            Run = (command, extra, _) =>
            {
                capturedCommands.Add(extra.Count == 0 ? command.Display : $"{command.Display} {string.Join(' ', extra)}");
                return 0;
            },
            IsToolOnPath = _ => true,
            ProbePostgres = () => new CiPostgresStatus(true, "ok"),
            ProbeDocker = () => new CiDockerStatus(true, "ok"),
            ShouldRunContainerSecurity = _ => skippedScope,
            FindFiles = (_, _) => [],
            DeleteDirectory = _ => { },
        };

        var forcedResults = LocalCiRunner.Run(
            jobs,
            Defaults with { RequestedJobs = ["container-security"] },
            readyEnv);
        forcedResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Passed, CiOutcome.Passed, CiOutcome.Passed, CiOutcome.Skipped);
        capturedCommands.Should().HaveCount(41);
        capturedCommands[0].Should().Be("docker compose -f docker/docker-compose.yml config -q");
        capturedCommands.Skip(1).Take(20).Should().OnlyContain(c => c.StartsWith("docker build ", StringComparison.Ordinal));
        capturedCommands.Skip(21).Should().OnlyContain(c => c.StartsWith("trivy image ", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolveContainerSecurityScope_SkipsNonContainerBranchDiffs_AndRunsOnTriggerEditsOrTrackedFallback()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), $"cadence-ci-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRepo);
        try
        {
            RunGit(tempRepo, "init", "-q", "-b", "main");
            RunGit(tempRepo, "config", "user.email", "ci-test@cadence.example");
            RunGit(tempRepo, "config", "user.name", "CI Test");
            File.WriteAllText(Path.Combine(tempRepo, "README.md"), "# baseline\n");
            File.WriteAllText(Path.Combine(tempRepo, "Dockerfile"), "FROM scratch\n");
            RunGit(tempRepo, "add", ".");
            RunGit(tempRepo, "commit", "-q", "-m", "initial");
            RunGit(tempRepo, "update-ref", "refs/remotes/origin/main", "HEAD");

            // HEAD == origin/main triggers tracked-files fallback -> runs unconditionally.
            LocalCiEnvironment.ResolveContainerSecurityScope(tempRepo, "origin/main").ShouldRun.Should().BeTrue();

            // Docs and frontend manifest commits -> skipped on default run.
            RunGit(tempRepo, "checkout", "-q", "-b", "feature/docs-and-frontend");
            File.WriteAllText(Path.Combine(tempRepo, "README.md"), "# updated docs\n");
            File.WriteAllText(Path.Combine(tempRepo, "package.json"), "{}\n");
            File.WriteAllText(Path.Combine(tempRepo, "pnpm-lock.yaml"), "lockfileVersion: '9.0'\n");
            RunGit(tempRepo, "add", "README.md", "package.json", "pnpm-lock.yaml");
            RunGit(tempRepo, "commit", "-q", "-m", "docs and frontend update");
            var nonContainerScope = LocalCiEnvironment.ResolveContainerSecurityScope(tempRepo, "origin/main");
            nonContainerScope.ShouldRun.Should().BeFalse();
            nonContainerScope.Note.Should().Contain("no container or dependency changes");

            // Touching a backend C# file under apps/ or packages/ -> runs container-security.
            Directory.CreateDirectory(Path.Combine(tempRepo, "apps", "gateway", "src"));
            File.WriteAllText(Path.Combine(tempRepo, "apps", "gateway", "src", "Endpoint.cs"), "namespace Sample;\n");
            RunGit(tempRepo, "add", "apps/gateway/src/Endpoint.cs");
            RunGit(tempRepo, "commit", "-q", "-m", "touch backend cs");
            LocalCiEnvironment.ResolveContainerSecurityScope(tempRepo, "origin/main").ShouldRun.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempRepo))
            {
                Directory.Delete(tempRepo, recursive: true);
            }
        }
    }

    [Fact]
    public void PrepareMergedWorktree_RecordsCallerPreMergeHead_AndForwardsCallerHeadAndOfflineOnlyToCadenceCheckCommands()
    {
        string tempBase = Path.Combine(Path.GetTempPath(), $"cadence-ci-caller-head-{Guid.NewGuid():N}");
        string remoteDir = Path.Combine(tempBase, "remote.git");
        string repoDir = Path.Combine(tempBase, "repo");
        string upstreamDir = Path.Combine(tempBase, "upstream");
        Directory.CreateDirectory(remoteDir);
        Directory.CreateDirectory(repoDir);
        try
        {
            RunGit(remoteDir, "init", "--bare", "-q", "-b", "main");
            RunGit(repoDir, "init", "-q", "-b", "main");
            RunGit(repoDir, "remote", "add", "origin", remoteDir);
            File.WriteAllText(Path.Combine(repoDir, "README.md"), "# base\n");
            RunGit(repoDir, "add", "README.md");
            RunGit(repoDir, "commit", "-q", "-m", "base");
            RunGit(repoDir, "push", "-q", "-u", "origin", "main");

            RunGit(repoDir, "checkout", "-q", "-b", "feature/stale");
            File.WriteAllText(Path.Combine(repoDir, "feature.txt"), "feature\n");
            RunGit(repoDir, "add", "feature.txt");
            RunGit(repoDir, "commit", "-q", "-m", "feature");
            string callerSha = CaptureGit(repoDir, "rev-parse", "HEAD").Trim();

            RunGit(tempBase, "clone", "-q", remoteDir, upstreamDir);
            File.WriteAllText(Path.Combine(upstreamDir, "upstream.txt"), "upstream\n");
            RunGit(upstreamDir, "add", "upstream.txt");
            RunGit(upstreamDir, "commit", "-q", "-m", "advance main");
            RunGit(upstreamDir, "push", "-q", "origin", "main");

            var prep = LocalCiEnvironment.PrepareMergedWorktree(repoDir, new LocalCiOptions(AllowSkip: false, BaseRef: "origin/main"));
            using (prep.Lease)
            {
                prep.Succeeded.Should().BeTrue(prep.Message);
                prep.CallerHeadSha.Should().Be(callerSha);
                prep.Environment.Should().NotBeNull();

                // Even inside the synthesized two-parent merged worktree, branch-freshness fails when caller HEAD lags behind origin/main!
                int staleExit = QualityGateOrchestrator.RunQualityGates(
                    gateName: "branch-freshness",
                    baseRef: prep.EffectiveBaseRef,
                    offline: true,
                    callerHead: prep.CallerHeadSha,
                    repoRoot: prep.Environment!.RepoRoot);
                staleExit.Should().Be(1);
            }

            // Forwarding verification: check commands receive --caller-head (when ForwardsBaseRef) and --offline (when Offline); non-check commands do not.
            var captured = new List<(string Display, IReadOnlyList<string> Extra)>();
            var checkStep = new CiStep("check-forwarding", "Scoped check")
            {
                Commands = [new CiCommand("node", ["scripts/run-cli.js", "check", "deid"], ForwardsBaseRef: true)],
            };
            var unforwardedCheckStep = new CiStep("check-forwarding", "Unscoped check")
            {
                Commands = [new CiCommand("node", ["scripts/run-cli.js", "check", "eslint"])],
            };
            var nonCheckStep = new CiStep("check-forwarding", "Non-check")
            {
                Commands = [new CiCommand("custom-tool", ["scan"], ForwardsBaseRef: true)],
            };
            var job = Job("check-forwarding", checkStep, unforwardedCheckStep, nonCheckStep);
            var env = new LocalCiEnvironment
            {
                RepoRoot = repoDir,
                Run = (cmd, extra, _) =>
                {
                    captured.Add((cmd.Display, extra));
                    return 0;
                },
                IsToolOnPath = _ => true,
                ProbePostgres = () => new CiPostgresStatus(true, "ok"),
                ProbeDocker = () => new CiDockerStatus(true, "ok"),
                ShouldRunContainerSecurity = _ => new CiContainerScanScope(false, "skip"),
                FindFiles = (_, _) => [],
                DeleteDirectory = _ => { },
            };

            LocalCiRunner.Run(
                [job],
                new LocalCiOptions(AllowSkip: false, BaseRef: "refs/remotes/origin/main", Offline: true, CallerHead: callerSha),
                env).Should().OnlyContain(r => r.Outcome == CiOutcome.Passed);

            captured[0].Extra.Should().Equal("--base", "refs/remotes/origin/main", "--offline", "--caller-head", callerSha);
            captured[1].Extra.Should().Equal("--offline");
            captured[2].Extra.Should().Equal("--base", "refs/remotes/origin/main");
        }
        finally
        {
            DeleteTempTree(tempBase);
        }
    }

    [Fact]
    public void Execute_WithAttest_PublishesCadenceLocalCiStatusOnlyAfterFullCleanOnlineRun_AndRejectsDegradedOrFailedRuns()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), $"cadence-ci-attest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRepo);
        try
        {
            WriteMinimalDatabaseRepoFixture(tempRepo, includeIntegrationTrx: true, includeDatabaseMetadata: true);
            var published = new List<(string Repo, string Sha, string Context)>();
            const string validHeadSha = "0123456789abcdef0123456789abcdef01234567"; // pragma: allowlist secret

            LocalCiEnvironment MakeFullPassEnv(
                IReadOnlyList<string>? dirtyPaths = null,
                bool failFirstStep = false,
                int publishExitCode = 0)
            {
                bool failedOnce = false;
                LocalCiEnvironment innerWorktreeEnv = new()
                {
                    RepoRoot = tempRepo,
                    Run = (_, _, _) =>
                    {
                        if (failFirstStep && !failedOnce)
                        {
                            failedOnce = true;
                            return 1;
                        }

                        return 0;
                    },
                    IsToolOnPath = _ => true,
                    ProbePostgres = () => new CiPostgresStatus(true, "ok"),
                    ProbeDocker = () => new CiDockerStatus(true, "ok"),
                    ShouldRunContainerSecurity = _ => new CiContainerScanScope(false, "no container changes"),
                    FindSkippedDatabaseTests = (_, _) => [],
                    HasRecordedDatabaseEnvironment = _ => true,
                    FindFiles = (_, _) => ["dummy.trx"],
                    DeleteDirectory = _ => { },
                };

                return new LocalCiEnvironment
                {
                    RepoRoot = tempRepo,
                    PrepareWorkspace = _ => new CiWorkspacePreparation(
                        true,
                        innerWorktreeEnv,
                        null,
                        "merged cleanly",
                        EffectiveBaseRef: "refs/remotes/origin/main",
                        CallerHeadSha: validHeadSha),
                    GetWorkingTreeChanges = () => dirtyPaths ?? [],
                    ResolveAttestationHeadSha = () => validHeadSha,
                    ResolveAttestationRepository = () => "fderuiter/cadence-clinical",
                    PublishCommitStatus = (repo, sha, context, _) =>
                    {
                        published.Add((repo, sha, context));
                        return new RulesetService.ProcessOutcome(
                            publishExitCode,
                            "{}",
                            publishExitCode == 0 ? "" : "gh: HTTP 403 Forbidden");
                    },
                    Run = innerWorktreeEnv.Run,
                    IsToolOnPath = _ => true,
                    ProbePostgres = () => new CiPostgresStatus(true, "ok"),
                    ProbeDocker = () => new CiDockerStatus(true, "ok"),
                    ShouldRunContainerSecurity = _ => new CiContainerScanScope(false, "no container changes"),
                    FindSkippedDatabaseTests = (_, _) => [],
                    HasRecordedDatabaseEnvironment = _ => true,
                    FindFiles = (_, _) => ["dummy.trx"],
                    DeleteDirectory = _ => { },
                };
            }

            // 1. Clean online full run publishes Cadence Local CI on caller HEAD.
            int okExit = LocalCiCommand.Execute(
                jobKeys: [],
                allowMissingTools: false,
                allowDbSkip: false,
                baseRef: "origin/main",
                list: false,
                environment: MakeFullPassEnv(),
                offline: false,
                includeUncommitted: false,
                attest: true);
            okExit.Should().Be(0);
            published.Should().Equal(("fderuiter/cadence-clinical", validHeadSha, RulesetService.LocalCiStatusContext));

            // 2. Degraded flags, partial job selection, --list, dirty tree, failed step, and failed gh api all refuse or fail closed.
            published.Clear();
            LocalCiCommand.Execute([], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(), offline: true, attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute([], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(), includeUncommitted: true, attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute([], allowMissingTools: true, allowDbSkip: false, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(), attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute([], allowMissingTools: false, allowDbSkip: true, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(), attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute([], allowSkip: true, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(), attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute(["actionlint"], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(), attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute([], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: true, environment: MakeFullPassEnv(), attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute([], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(dirtyPaths: [" M README.md"]), attest: true)
                .Should().Be(1);
            LocalCiCommand.Execute([], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(failFirstStep: true), attest: true)
                .Should().Be(1);
            published.Should().BeEmpty("no commit status may be published when --attest is rejected or a CI step fails");

            LocalCiCommand.Execute([], allowMissingTools: false, allowDbSkip: false, baseRef: "origin/main", list: false, environment: MakeFullPassEnv(publishExitCode: 1), attest: true)
                .Should().Be(1, "gh api status publishing failures must fail closed");
        }
        finally
        {
            DeleteTempTree(tempRepo);
        }
    }

    [Fact]
    public void DotnetTestsAndCompliance_FailOnUnitOnlyTrxOrMissingDatabaseMetadata_UnlessAllowDbSkipIsSet()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), $"cadence-ci-db-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRepo);
        try
        {
            WriteMinimalDatabaseRepoFixture(tempRepo, includeIntegrationTrx: false, includeDatabaseMetadata: true);
            var envUnitOnly = CreateInProcessTestEnvironment(tempRepo);

            var dotnetJobs = CiPlan.Select(["dotnet-tests"]);
            var strictUnitOnlyResults = LocalCiRunner.Run(dotnetJobs, Defaults with { AllowMissingTools = true }, envUnitOnly);
            strictUnitOnlyResults.Single(r => r.Step.Name == "Execute .NET 10 xUnit Test Suites").Outcome.Should().Be(CiOutcome.Failed);
            strictUnitOnlyResults.Single(r => r.Step.Name == "Execute .NET 10 xUnit Test Suites").Note
                .Should().Contain("1 database test(s) did not execute").And.Contain("RlsTests.Isolates");
            strictUnitOnlyResults.Single(r => r.Step.Name == "Fail the run when a PostgreSQL test did not execute").Outcome.Should().Be(CiOutcome.NotRun);

            var gateStepOnly = new CiJob("dotnet-tests", ".NET", [dotnetJobs.Single().Steps.Single(s => s.Name == "Fail the run when a PostgreSQL test did not execute")]);
            LocalCiRunner.Run([gateStepOnly], Defaults with { AllowMissingTools = true }, envUnitOnly).Single().Outcome.Should().Be(CiOutcome.Failed);
            LocalCiRunner.Run([gateStepOnly], Defaults with { AllowDbSkip = true }, envUnitOnly).Single().Outcome.Should().Be(CiOutcome.Passed);

            var complianceOnly = new CiJob("compliance", "Compliance", [CiPlan.Jobs.Single(j => j.Key == "compliance").Steps.Single(s => s.Name == "Generate Compliance Reports")]);
            LocalCiRunner.Run([complianceOnly], Defaults with { AllowMissingTools = true }, envUnitOnly).Single().Outcome.Should().Be(CiOutcome.Failed);
            LocalCiRunner.Run([complianceOnly], Defaults with { AllowDbSkip = true }, envUnitOnly).Single().Outcome.Should().Be(CiOutcome.Passed);

            // Now write passing TRX for the database test, but omit *.database.txt fixture metadata.
            WriteMinimalDatabaseRepoFixture(tempRepo, includeIntegrationTrx: true, includeDatabaseMetadata: false);
            var envMissingMetadata = CreateInProcessTestEnvironment(tempRepo);

            var strictMissingMetadataResults = LocalCiRunner.Run(dotnetJobs, Defaults with { AllowMissingTools = true }, envMissingMetadata);
            strictMissingMetadataResults.Single(r => r.Step.Name == "Execute .NET 10 xUnit Test Suites").Outcome.Should().Be(CiOutcome.Failed);
            strictMissingMetadataResults.Single(r => r.Step.Name == "Execute .NET 10 xUnit Test Suites").Note
                .Should().Contain("*.database.txt").And.Contain("--allow-db-skip");
            LocalCiRunner.Run([gateStepOnly], Defaults with { AllowMissingTools = true }, envMissingMetadata).Single().Outcome.Should().Be(CiOutcome.Failed);
            LocalCiRunner.Run([complianceOnly], Defaults with { AllowMissingTools = true }, envMissingMetadata).Single().Outcome.Should().Be(CiOutcome.Failed);

            LocalCiRunner.Run(dotnetJobs, Defaults with { AllowDbSkip = true }, envMissingMetadata)
                .Should().OnlyContain(r => r.Outcome == CiOutcome.Passed);
            LocalCiRunner.Run([complianceOnly], Defaults with { AllowDbSkip = true }, envMissingMetadata).Single().Outcome.Should().Be(CiOutcome.Passed);

            // With both full TRX and *.database.txt for every database assembly, strict mode passes both dotnet-tests and compliance.
            WriteMinimalDatabaseRepoFixture(tempRepo, includeIntegrationTrx: true, includeDatabaseMetadata: true);
            var envComplete = CreateInProcessTestEnvironment(tempRepo);
            LocalCiRunner.Run(dotnetJobs, Defaults, envComplete).Should().OnlyContain(r => r.Outcome == CiOutcome.Passed);
            LocalCiRunner.Run([complianceOnly], Defaults, envComplete).Single().Outcome.Should().Be(CiOutcome.Passed);
        }
        finally
        {
            if (Directory.Exists(tempRepo))
            {
                Directory.Delete(tempRepo, recursive: true);
            }
        }
    }

    [Fact]
    public void AccessibilityE2E_StandaloneRun_DoesNotRequireFullRepositoryDatabaseEvidence()
    {
        string? observedDiscoveryRoot = "not-called";
        var env = Environment(
            hasRecordedDatabaseEnvironment: false,
            onFindSkippedDatabaseTests: (_, repoRoot) => observedDiscoveryRoot = repoRoot);

        var e2eJobs = CiPlan.Select(["accessibility-e2e"]);
        var results = LocalCiRunner.Run(
            e2eJobs,
            Defaults with { RequestedJobs = ["accessibility-e2e"] },
            env);

        results.Where(r => r.Step.NotRunLocally is null).Should().OnlyContain(r => r.Outcome == CiOutcome.Passed);
        observedDiscoveryRoot.Should().BeNull("standalone accessibility-e2e runs one filtered integration test rather than the full solution TRX suite");
    }

    [Fact]
    public void DiscoverFiles_ShellScripts_FindsShellScriptsAndGitHooks_WhileExcludingToolchainsAndGitInternals()
    {
        string tempRepo = Path.Combine(Path.GetTempPath(), $"cadence-ci-shell-discover-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRepo);
        try
        {
            Directory.CreateDirectory(Path.Combine(tempRepo, "scripts"));
            Directory.CreateDirectory(Path.Combine(tempRepo, ".githooks"));
            Directory.CreateDirectory(Path.Combine(tempRepo, ".git", "hooks"));
            Directory.CreateDirectory(Path.Combine(tempRepo, ".cadence", "bin"));
            Directory.CreateDirectory(Path.Combine(tempRepo, ".venv", "bin"));
            Directory.CreateDirectory(Path.Combine(tempRepo, "node_modules", "pkg"));

            File.WriteAllText(Path.Combine(tempRepo, "scripts", "setup_wizard.sh"), "#!/usr/bin/env bash\n");
            File.WriteAllText(Path.Combine(tempRepo, ".githooks", "pre-commit"), "#!/bin/sh\n");
            File.WriteAllText(Path.Combine(tempRepo, ".git", "hooks", "pre-commit.sample"), "#!/bin/sh\n");
            File.WriteAllText(Path.Combine(tempRepo, ".cadence", "bin", "helper.sh"), "#!/bin/sh\n");
            File.WriteAllText(Path.Combine(tempRepo, ".venv", "bin", "activate.sh"), "#!/bin/sh\n");
            File.WriteAllText(Path.Combine(tempRepo, "node_modules", "pkg", "postinstall.sh"), "#!/bin/sh\n");

            var step = new CiStep("lint-and-static", "Run ShellCheck") { Files = CiFileSource.ShellScripts };
            var discovered = LocalCiEnvironment.DiscoverFiles(tempRepo, step, "origin/main");

            discovered.Should().Equal("./.githooks/pre-commit", "./scripts/setup_wizard.sh");
        }
        finally
        {
            DeleteTempTree(tempRepo);
        }
    }

    private static LocalCiEnvironment CreateInProcessTestEnvironment(string repoRoot) => new()
    {
        RepoRoot = repoRoot,
        Run = (_, _, _) => 0,
        IsToolOnPath = _ => true,
        ProbePostgres = () => new CiPostgresStatus(true, "PostgreSQL 18 reachable."),
        FindFiles = (_, _) => [],
        DeleteDirectory = _ => { },
    };

    private static void WriteMinimalDatabaseRepoFixture(string repoRoot, bool includeIntegrationTrx, bool includeDatabaseMetadata)
    {
        string docsSdlc = Path.Combine(repoRoot, "docs", "SDLC");
        string unitTestDir = Path.Combine(repoRoot, "tests", "Cadence.UnitTests");
        string integrationTestDir = Path.Combine(repoRoot, "tests", "Cadence.IntegrationTests");
        string resultsDir = Path.Combine(repoRoot, RtmGeneratorService.DefaultResultsDir);
        if (Directory.Exists(resultsDir))
        {
            Directory.Delete(resultsDir, recursive: true);
        }

        Directory.CreateDirectory(docsSdlc);
        Directory.CreateDirectory(unitTestDir);
        Directory.CreateDirectory(integrationTestDir);
        Directory.CreateDirectory(resultsDir);

        File.WriteAllText(Path.Combine(docsSdlc, "prd.md"), "#### PRD-AAA-001: Sample Requirement\n");
        string baselineStamp = GxPSigner.StampGxpMarkdown(
            "# Baseline\n\n" + new RtmEvidenceContext("`0123abc` (clean working tree)", "2026-10-20 00:00:00", "PostgreSQL 18", "test").Render());
        File.WriteAllText(Path.Combine(docsSdlc, "Requirements_Traceability_Matrix.md"), baselineStamp);
        File.WriteAllText(Path.Combine(docsSdlc, "IQ_OQ_PQ_Execution_Report.md"), baselineStamp);

        // Build sample tagged test line-by-line so RequirementTagScanner does not treat LocalCiRunnerTests.cs as tagged.
        string gxpAttr = "GxP" + "Requirement";
        File.WriteAllText(
            Path.Combine(unitTestDir, "SampleUnitTests.cs"),
            string.Join("\n",
                "using Xunit;",
                "namespace Cadence.UnitTests;",
                "public class SampleUnitTests",
                "{",
                "    [Fact]",
                $"    [{gxpAttr}(\"PRD-AAA-001\")]",
                "    public void UnitPasses() { }",
                "}"));
        File.WriteAllText(
            Path.Combine(integrationTestDir, "RlsTests.cs"),
            string.Join("\n",
                "using Xunit;",
                "namespace Cadence.IntegrationTests.Kernel;",
                "public class RlsTests",
                "{",
                "    [Fact]",
                "    public void Isolates() { }",
                "}"));

        File.WriteAllText(
            Path.Combine(resultsDir, "gxp_unit.trx"),
            BuildTrxXml("Cadence.UnitTests.dll", ("Cadence.UnitTests.SampleUnitTests", "UnitPasses", "Passed")));
        if (includeIntegrationTrx)
        {
            File.WriteAllText(
                Path.Combine(resultsDir, "gxp_integration.trx"),
                BuildTrxXml("Cadence.IntegrationTests.dll", ("Cadence.IntegrationTests.Kernel.RlsTests", "Isolates", "Passed")));
        }

        if (includeDatabaseMetadata)
        {
            foreach (string assembly in DatabaseTestEvidence.DatabaseAssemblies)
            {
                File.WriteAllText(
                    Path.Combine(resultsDir, $"{assembly}.database.txt"),
                    string.Join("\n",
                        "fixture=kernel",
                        "server_version=18.3",
                        "source=CADENCE_TEST_POSTGRES",
                        "extensions=pgcrypto",
                        "latest_migration=20261001_Initial",
                        "app_role=cadence"));
            }
        }
    }

    private static string BuildTrxXml(string storage, params (string ClassName, string MethodName, string Outcome)[] results)
    {
        const string ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"; // deid-ignore: XML namespace, not PHI
        var unitTestResults = string.Join(
            '\n',
            results.Select((r, idx) =>
                $"""<UnitTestResult executionId="e{idx}" testId="t{idx}" testName="{r.ClassName}.{r.MethodName}" outcome="{r.Outcome}" duration="00:00:00.010" />"""));
        var unitTests = string.Join(
            '\n',
            results.Select((r, idx) =>
                $"""
                <UnitTest name="{r.ClassName}.{r.MethodName}" storage="{storage}" id="t{idx}">
                  <Execution id="e{idx}" />
                  <TestMethod codeBase="/bin/{storage}" className="{r.ClassName}" name="{r.MethodName}" />
                </UnitTest>
                """));
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="{ns}">
              <Results>
                {unitTestResults}
              </Results>
              <TestDefinitions>
                {unitTests}
              </TestDefinitions>
            </TestRun>
            """;
    }

    private static void RunGit(string workingDir, params string[] args) =>
        _ = CaptureGit(workingDir, args);

    private static string CaptureGit(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in new List<string> { "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false" })
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {error}");
        return output;
    }

    private static void DeleteTempTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        Directory.Delete(path, recursive: true);
    }
}
