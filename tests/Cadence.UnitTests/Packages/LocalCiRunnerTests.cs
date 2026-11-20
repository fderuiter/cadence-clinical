using System;
using System.Collections.Generic;
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
    private static readonly LocalCiOptions Defaults = new(AllowSkip: false, BaseRef: "origin/main");

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
    public void AStepWhoseToolIsMissing_FailsBeforeRunning_WithoutAllowSkip()
    {
        var jobs = new[]
        {
            Job("a",
                new CiStep("a", "lint docker") { RequiredTool = "hadolint", ToolHint = "install hadolint", Commands = [Cmd("hadolint")] },
                new CiStep("a", "next") { Commands = [Cmd("next")] }),
        };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment(tools: tool => tool != "hadolint"));

        results.Select(r => r.Outcome).Should().Equal(CiOutcome.Failed, CiOutcome.NotRun);
        results[0].Note.Should().Contain("hadolint is not on PATH; install hadolint").And.Contain("--allow-skip");
        _ran.Should().BeEmpty();
    }

    [Fact]
    public void AStepWhoseToolIsMissing_IsSkippedWithTheInstallHint_WithAllowSkip()
    {
        var jobs = new[]
        {
            Job("a",
                new CiStep("a", "lint docker") { RequiredTool = "hadolint", ToolHint = "install hadolint", Commands = [Cmd("hadolint")] },
                new CiStep("a", "next") { Commands = [Cmd("next")] }),
        };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowSkip = true }, Environment(tools: tool => tool != "hadolint"));

        results.Select(r => r.Outcome).Should().Equal(CiOutcome.Skipped, CiOutcome.Passed);
        results[0].Note.Should().Be("hadolint is not on PATH; install hadolint");
        _ran.Should().Equal("next");
    }

    [Theory]
    [InlineData("actionlint", "actionlint")]
    [InlineData("hadolint", "lint-and-static")]
    [InlineData("shellcheck", "lint-and-static")]
    [InlineData("detect-secrets-hook", "security")]
    public void LocalCiCommand_ExitsOneWhenARequiredToolIsMissing_UnlessAllowSkipIsPassed(string missingTool, string jobKey)
    {
        var env = Environment(tools: tool => tool != missingTool, files: ["Dockerfile"]);

        int strictExit = LocalCiCommand.Execute([jobKey], allowSkip: false, baseRef: "origin/main", list: false, environment: env);
        strictExit.Should().Be(1);

        int skipExit = LocalCiCommand.Execute([jobKey], allowSkip: true, baseRef: "origin/main", list: false, environment: env);
        skipExit.Should().Be(0);

        var skipResults = LocalCiRunner.Run(CiPlan.Select([jobKey]), Defaults with { AllowSkip = true }, env);
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
    public void DatabaseTests_FailBeforeRunning_WhenNoPostgreSQLIsReachable()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment(postgres: false));

        results.Single().Outcome.Should().Be(CiOutcome.Failed);
        results.Single().Note.Should().Contain("CADENCE_TEST_POSTGRES").And.Contain("--allow-skip");
        _ran.Should().BeEmpty("a run without a database would pass with every database test skipped");
    }

    [Fact]
    public void DatabaseTests_RunAsSkipped_WithAllowSkip()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowSkip = true }, Environment(postgres: false));

        results.Single().Outcome.Should().Be(CiOutcome.Passed);
        results.Single().Note.Should().StartWith("PostgreSQL tests skipped (--allow-skip)");
        _ran.Should().Equal("test");
    }

    /// <summary>
    /// Regression for #5325: with CADENCE_TEST_DOCKER=1 the kernel tests used to skip while the run reported green. A
    /// passing test step whose results show skipped database tests now fails.
    /// </summary>
    [Fact]
    public void DatabaseTests_ThatDidNotExecute_FailAPassingTestStep()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };
        var skipped = new[] { new SkippedDatabaseTest("Cadence.IntegrationTests.Kernel.RlsTests.Isolates", "Cadence.IntegrationTests.dll", "no database") };

        var results = LocalCiRunner.Run(jobs, Defaults, Environment(skippedDatabaseTests: skipped));

        results.Single().Outcome.Should().Be(CiOutcome.Failed);
        results.Single().Note.Should().Contain("1 database test(s) did not execute").And.Contain("RlsTests.Isolates").And.Contain("--allow-skip");
        _variables["test"].Should().ContainKey("CADENCE_TEST_EVIDENCE_DIR");
    }

    [Fact]
    public void DatabaseTests_ThatDidNotExecute_AreAccepted_WithAllowSkip()
    {
        var jobs = new[] { Job("t", new CiStep("t", "tests") { NeedsPostgres = true, Commands = [Cmd("test")] }) };
        var skipped = new[] { new SkippedDatabaseTest("Cadence.IntegrationTests.Kernel.RlsTests.Isolates", "Cadence.IntegrationTests.dll", "no database") };

        var results = LocalCiRunner.Run(jobs, Defaults with { AllowSkip = true }, Environment(skippedDatabaseTests: skipped));

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
    public void FilePattern_KeepsOnlyMatchingPaths()
    {
        var step = new CiStep("a", "dup") { FilePattern = @"\.(cs|vue)$" };

        new[] { "a.cs", "b.md", "c.vue", "d.csproj" }.Where(p => CiPlan.MatchesFilePattern(step, p))
            .Should().Equal("a.cs", "c.vue");
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
}
