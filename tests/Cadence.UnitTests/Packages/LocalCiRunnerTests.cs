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
    public void ContainerSecurity_FailsWhenDockerDaemonIsUnreachable_AndSkipsWithAllowMissingToolsOrAllowSkip()
    {
        var jobs = CiPlan.Select(["container-security"]);
        var env = Environment(docker: false);

        var strictResults = LocalCiRunner.Run(jobs, Defaults with { RequestedJobs = ["container-security"] }, env);
        strictResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.NotRun, CiOutcome.Skipped);
        strictResults[0].Note.Should().Contain("Docker daemon is not reachable").And.Contain("--allow-missing-tools");
        _ran.Should().BeEmpty();

        var dbSkipOnlyResults = LocalCiRunner.Run(jobs, Defaults with { AllowDbSkip = true, RequestedJobs = ["container-security"] }, env);
        dbSkipOnlyResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.NotRun, CiOutcome.Skipped);

        var skipResults = LocalCiRunner.Run(jobs, Defaults with { AllowMissingTools = true, RequestedJobs = ["container-security"] }, env);
        skipResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Skipped, CiOutcome.Skipped);
        skipResults[0].Note.Should().Contain("Docker daemon unavailable (--allow-missing-tools)");
        skipResults[1].Note.Should().Contain("Docker daemon unavailable (--allow-missing-tools)");
        _ran.Should().BeEmpty();
    }

    [Fact]
    public void ContainerSecurity_BuildsAndScansAllFourImages_AndRespectsDiffScopeUnlessExplicitlyRequested()
    {
        var jobs = CiPlan.Select(["container-security"]);
        var skippedScope = new CiContainerScanScope(false, "no container or dependency changes against origin/main (pass --job container-security to force)");
        var envWhenNoDiffAndMissingTools = Environment(
            docker: false,
            tools: _ => false,
            containerSecurityScope: skippedScope);

        var defaultRunResults = LocalCiRunner.Run(jobs, Defaults, envWhenNoDiffAndMissingTools);
        defaultRunResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Skipped, CiOutcome.Skipped);
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
        forcedResults.Select(r => r.Outcome).Should().Equal(CiOutcome.Passed, CiOutcome.Passed, CiOutcome.Skipped);
        capturedCommands.Should().HaveCount(8);
        capturedCommands.Take(4).Should().OnlyContain(c => c.StartsWith("docker build ", StringComparison.Ordinal));
        capturedCommands.Skip(4).Should().OnlyContain(c => c.StartsWith("trivy image ", StringComparison.Ordinal));
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

            // Docs-only branch commit -> skipped on default run.
            RunGit(tempRepo, "checkout", "-q", "-b", "feature/docs-only");
            File.WriteAllText(Path.Combine(tempRepo, "README.md"), "# updated docs\n");
            RunGit(tempRepo, "add", "README.md");
            RunGit(tempRepo, "commit", "-q", "-m", "docs update");
            var docsOnlyScope = LocalCiEnvironment.ResolveContainerSecurityScope(tempRepo, "origin/main");
            docsOnlyScope.ShouldRun.Should().BeFalse();
            docsOnlyScope.Note.Should().Contain("no container or dependency changes");

            // Touching a csproj or Dockerfile on the branch -> runs container-security.
            Directory.CreateDirectory(Path.Combine(tempRepo, "packages", "sample"));
            File.WriteAllText(Path.Combine(tempRepo, "packages", "sample", "Sample.csproj"), "<Project />\n");
            RunGit(tempRepo, "add", "packages/sample/Sample.csproj");
            RunGit(tempRepo, "commit", "-q", "-m", "touch csproj");
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

    private static void RunGit(string workingDir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        proc.ExitCode.Should().Be(0);
    }
}
