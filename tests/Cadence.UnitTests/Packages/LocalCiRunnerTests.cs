using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;
using Cadence.Compliance;

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
        Func<string, bool>? tools = null,
        IReadOnlyList<string>? files = null,
        IReadOnlyList<SkippedDatabaseTest>? skippedDatabaseTests = null) => new()
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
            FindFiles = (_, _) => files ?? [],
            DeleteDirectory = _ => { },
            FindSkippedDatabaseTests = _ => skippedDatabaseTests ?? [],
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

        var skipResults = LocalCiRunner.Run(CiPlan.Select([jobKey]), Defaults with { AllowMissingTools = true }, env);
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
    public void PrepareWorkspace_FetchesOriginMain_RunsInDetachedMergedWorktree_ReusesCadenceToolchain_AndCleansUp()
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
            File.WriteAllText(Path.Combine(repoDir, "base.txt"), "base\n");
            RunGit(repoDir, "add", "-A");
            RunGit(repoDir, "commit", "-q", "-m", "initial");
            RunGit(repoDir, "remote", "add", "origin", remoteDir);
            RunGit(repoDir, "push", "-q", "-u", "origin", "main");

            // Create a read-only .cadence/bin tool in the caller checkout (untracked).
            string cadenceBinDir = Path.Combine(repoDir, ".cadence", "bin");
            Directory.CreateDirectory(cadenceBinDir);
            string fakeToolPath = Path.Combine(cadenceBinDir, "cadence-test-linter");
            File.WriteAllText(fakeToolPath, "#!/bin/sh\nexit 0\n");
            File.SetUnixFileMode(fakeToolPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            // Feature branch commit + dirty untracked file in caller checkout.
            RunGit(repoDir, "checkout", "-q", "-b", "feature");
            File.WriteAllText(Path.Combine(repoDir, "feature.txt"), "from-feature\n");
            RunGit(repoDir, "add", "feature.txt");
            RunGit(repoDir, "commit", "-q", "-m", "feature commit");
            File.WriteAllText(Path.Combine(repoDir, "dirty-caller.txt"), "uncommitted\n");

            // Advance origin/main in peer clone so caller's local origin/main is stale until fetched.
            RunGit(tempBase, "clone", "-q", remoteDir, peerDir);
            File.WriteAllText(Path.Combine(peerDir, "upstream.txt"), "from-upstream\n");
            RunGit(peerDir, "add", "upstream.txt");
            RunGit(peerDir, "commit", "-q", "-m", "upstream commit");
            RunGit(peerDir, "push", "-q", "origin", "main");

            string? observedWorktreeRoot = null;
            bool sawFeatureAndUpstream = false;
            bool sawDirtyCallerFile = true;
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
                        observedWorktreeRoot = worktreeRoot;
                        sawFeatureAndUpstream =
                            File.Exists(Path.Combine(worktreeRoot, "feature.txt")) &&
                            File.Exists(Path.Combine(worktreeRoot, "upstream.txt"));
                        sawDirtyCallerFile = File.Exists(Path.Combine(worktreeRoot, "dirty-caller.txt"));
                        File.WriteAllText(Path.Combine(worktreeRoot, "ephemeral-build.out"), "generated\n");
                        return 0;
                    },
                    InProcessDisplay = "inspect synthesized worktree",
                    InProcessCiText = "echo ok",
                });

            var options = new LocalCiOptions(AllowSkip: false, BaseRef: "origin/main", Offline: false);
            var results = LocalCiRunner.Run([job], options, LocalCiEnvironment.Create(repoDir));
            results.Select(r => r.Outcome).Should().Equal(CiOutcome.Passed, CiOutcome.Passed);

            observedWorktreeRoot.Should().NotBeNull();
            observedWorktreeRoot.Should().StartWith(Path.GetTempPath());
            observedWorktreeRoot.Should().NotBe(repoDir);
            sawFeatureAndUpstream.Should().BeTrue("merged worktree must contain both feature HEAD and fetched origin/main");
            sawDirtyCallerFile.Should().BeFalse("caller untracked files must not leak into detached merged worktree");
            Directory.Exists(observedWorktreeRoot!).Should().BeFalse("temporary detached worktree must be removed after run");
            File.Exists(Path.Combine(repoDir, "ephemeral-build.out")).Should().BeFalse("caller checkout must not be mutated");
            File.Exists(Path.Combine(repoDir, "upstream.txt")).Should().BeFalse("caller branch checkout must remain on feature HEAD");
        }
        finally
        {
            DeleteTempTree(tempBase);
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

    private static void RunGit(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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
        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, $"git {string.Join(' ', args)} failed: {error}");
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
