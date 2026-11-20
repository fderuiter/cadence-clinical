using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Cli.Services;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// Keeps <see cref="CiPlan"/> (<c>cadence ci</c>, #5017) in step with <c>.github/workflows/ci.yml</c>: a job or step
/// added, renamed, removed, or given a different command in the workflow fails here until the plan follows.
/// </summary>
public partial class LocalCiPlanTests
{
    private const string PushOnly = "github.event_name == 'push'";

    private static readonly IReadOnlyList<WorkflowJob> Workflow = ParseWorkflow(
        File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml")));

    [Fact]
    public void EveryWorkflowJob_IsInThePlanOrListedAsGitHubOnly()
    {
        var planKeys = CiPlan.Jobs.Select(j => j.Key).ToList();

        planKeys.Should().NotIntersectWith(CiPlan.GitHubOnlyJobs.Keys);
        Workflow.Select(j => j.Key).Should().BeEquivalentTo(planKeys.Concat(CiPlan.GitHubOnlyJobs.Keys));
        CiPlan.Jobs.Select(j => j.Name).Should().Equal(planKeys.Select(key => Workflow.Single(w => w.Key == key).Name));
    }

    [Fact]
    public void EachJob_HasTheWorkflowStepsThatRunACommand_InOrder()
    {
        foreach (var job in CiPlan.Jobs)
        {
            var planNames = job.Steps.Select(s => s.Name).ToList();
            var workflowNames = WorkflowJobFor(job).Steps
                .Where(s => s.Run is not null || planNames.Contains(s.Name))
                .Select(s => s.Name);

            planNames.Should().Equal(workflowNames, $"the '{job.Key}' job's steps must match ci.yml");
        }
    }

    [Fact]
    public void EachStep_KeepsGoingAfterAFailureOnlyWhereTheWorkflowStepHasIfAlways()
    {
        foreach (var (job, step, workflowStep) in PlanStepsWithWorkflowSteps())
        {
            bool always = workflowStep.If?.Contains("always()", StringComparison.Ordinal) is true;
            step.Always.Should().Be(always, $"'{job.Key} / {step.Name}' has 'if: {workflowStep.If}' in ci.yml");
        }
    }

    [Fact]
    public void EachLocalCommand_AppearsInItsWorkflowStep()
    {
        foreach (var (job, step, workflowStep) in PlanStepsWithWorkflowSteps())
        {
            string run = Normalize(workflowStep.Run ?? "");
            foreach (var command in step.Commands)
            {
                run.Should().Contain(Normalize(command.MatchText), $"'{job.Key} / {step.Name}' runs it locally");
            }

            if (step.InProcess is not null)
            {
                step.InProcessCiText.Should().NotBeNull();
                run.Should().Contain(Normalize(step.InProcessCiText!));
            }
        }
    }

    /// <summary>
    /// Each dotnet, pnpm and <c>git diff --exit-code</c> command of a mirrored step is run locally with the same
    /// arguments. Only shell variables (<c>$CHANGED_FILES</c>, <c>"$RUNNER_TEMP/..."</c>) may follow, and anything
    /// else only where the local command declares its CI text because it differs on purpose.
    /// </summary>
    [Fact]
    public void EveryDotnetPnpmAndGitDiffCommandInTheWorkflow_IsRunLocallyWithTheSameArguments()
    {
        foreach (var (job, step, workflowStep) in PlanStepsWithWorkflowSteps().Where(t => t.Step.NotRunLocally is null))
        {
            var local = step.Commands.Select(c => (Text: Normalize(c.MatchText), Declared: c.CiText is not null))
                .Concat(step.InProcessCiText is null ? [] : [(Text: Normalize(step.InProcessCiText), Declared: true)])
                .ToList();
            var workflowCommands = (workflowStep.Run ?? "").Split('\n')
                .Select(line => ShellControl().Split(Normalize(line))[0])
                .Where(line => line.StartsWith("dotnet ", StringComparison.Ordinal)
                    || line.StartsWith("pnpm ", StringComparison.Ordinal)
                    || line.StartsWith("git diff --exit-code", StringComparison.Ordinal));

            foreach (var line in workflowCommands)
            {
                local.Should().Contain(l => SameCommand(line, l.Text, l.Declared),
                    $"'{job.Key} / {step.Name}' runs '{line}' in ci.yml, so cadence ci must run it with the same arguments");
            }
        }
    }

    private static bool SameCommand(string workflowLine, string local, bool declaredDifference)
    {
        if (workflowLine == local) return true;
        if (!workflowLine.StartsWith(local + " ", StringComparison.Ordinal)) return false;
        return declaredDifference
            || workflowLine[(local.Length + 1)..].Split(' ').All(token => token.StartsWith('$') || token.StartsWith("\"$", StringComparison.Ordinal));
    }

    [Fact]
    public void StepsAreLeftOutLocally_OnlyWhenTheWorkflowRunsThemOnPushAlone()
    {
        foreach (var (job, step, workflowStep) in PlanStepsWithWorkflowSteps().Where(t => t.Step.NotRunLocally is not null))
        {
            step.NotRunLocally.Should().NotBeNullOrWhiteSpace();
            string conditions = $"{WorkflowJobFor(job).If} {workflowStep.If}";
            bool pushOnly = conditions.Contains(PushOnly, StringComparison.Ordinal);
            bool runnerProvisioning = (workflowStep.Run ?? "").Contains("steps.playwright-cache.outputs.cache-hit", StringComparison.Ordinal);
            (pushOnly || runnerProvisioning).Should().BeTrue($"'{job.Key} / {step.Name}' is never run locally");
        }

        foreach (var job in CiPlan.Jobs)
        {
            bool pushOnly = WorkflowJobFor(job).If?.Contains(PushOnly, StringComparison.Ordinal) is true;
            (job.OnRequestOnly is not null).Should().Be(pushOnly, $"'{job.Key}' runs by default unless CI runs it on push alone");
        }
    }

    [Fact]
    public void JobNeeds_MatchTheWorkflow()
    {
        foreach (var job in CiPlan.Jobs)
        {
            job.Needs.Should().Equal(WorkflowJobFor(job).Needs, $"'{job.Key}' needs the same jobs as in ci.yml");
            job.NeedsStep.Keys.Should().BeSubsetOf(job.Needs);
            foreach (var stepKey in job.NeedsStep.Values)
            {
                CiPlan.Jobs.SelectMany(j => j.Steps).Should().Contain(s => s.Key == stepKey);
            }
        }
    }

    [Fact]
    public void CoveredBy_NamesAStepThatRunsEarlier()
    {
        var order = CiPlan.Jobs.SelectMany(j => j.Steps).Select(s => s.Key).ToList();

        foreach (var step in CiPlan.Jobs.SelectMany(j => j.Steps).Where(s => s.CoveredBy is not null))
        {
            order.Should().Contain(step.CoveredBy!);
            order.IndexOf(step.CoveredBy!).Should().BeLessThan(order.IndexOf(step.Key), $"'{step.Key}' is covered by an earlier step");
        }
    }

    [Fact]
    public void Select_WithoutJobs_RunsEveryJobThatIsNotOnRequestOnly()
    {
        CiPlan.Select([]).Select(j => j.Key).Should().Equal(
            "lint-and-static",
            "dotnet-quality",
            "native-aot-build",
            "docs-build",
            "security",
            "unit-tests",
            "dotnet-tests",
            "accessibility-e2e",
            "compliance");
    }

    [Fact]
    public void Select_AddsTheJobsANamedJobNeeds_InPlanOrder()
    {
        CiPlan.Select(["compliance"]).Select(j => j.Key).Should().Equal("unit-tests", "dotnet-tests", "compliance");
        CiPlan.Select(["accessibility-e2e", "dotnet-quality"]).Select(j => j.Key).Should().Equal("dotnet-quality", "accessibility-e2e");
    }

    [Theory]
    [InlineData("no-such-job", "Unknown job")]
    [InlineData("automerge", "no local equivalent")]
    [InlineData("release-and-archive", "no local equivalent")]
    [InlineData("deploy-docs", "no local equivalent")]
    public void Select_RejectsUnknownAndGitHubOnlyJobs(string key, string message)
    {
        var select = () => CiPlan.Select([key]);

        select.Should().Throw<ArgumentException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void WorkflowTriggersAndConcurrency_IncludeVersionTagsAndProtectPushRunsFromCancellation()
    {
        string raw = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));

        raw.Should().MatchRegex(@"push:\s+branches:\s+\[""?main""?,\s*""?release/\*""?\]\s+tags:\s+\[""v\*""\]");
        raw.Should().Contain("cancel-in-progress: ${{ github.event_name == 'pull_request' }}");
    }

    [Fact]
    public void ReleaseAndDocsCallerJobs_DependIndependentlyOnEveryPushValidationJob()
    {
        string[] expectedValidationNeeds =
        [
            "actionlint",
            "lint-and-static",
            "dotnet-quality",
            "native-aot-build",
            "docs-build",
            "security",
            "container-security",
            "unit-tests",
            "dotnet-tests",
            "accessibility-e2e",
            "compliance",
        ];

        var releaseJob = Workflow.Single(j => j.Key == "release-and-archive");
        var deployDocsJob = Workflow.Single(j => j.Key == "deploy-docs");

        releaseJob.Needs.Should().Equal(expectedValidationNeeds);
        deployDocsJob.Needs.Should().Equal(expectedValidationNeeds,
            "deploy-docs must gate on push validation jobs independently of release-and-archive");
        releaseJob.If.Should().Contain("refs/heads/main")
            .And.Contain("refs/heads/release/")
            .And.Contain("refs/tags/v");
        deployDocsJob.If.Should().Contain("refs/heads/main")
            .And.NotContain("release-and-archive");
    }

    [Fact]
    public void AotRuntimeIdentifier_IsPortable()
    {
        CiPlan.AotRuntimeIdentifier.Should().MatchRegex("^(linux|osx|win)-(x64|arm64|x86|arm)$");
    }

    private static IEnumerable<(CiJob Job, CiStep Step, WorkflowStep WorkflowStep)> PlanStepsWithWorkflowSteps() =>
        CiPlan.Jobs.SelectMany(job => job.Steps.Select(step =>
            (job, step, WorkflowJobFor(job).Steps.Single(s => s.Name == step.Name))));

    private static WorkflowJob WorkflowJobFor(CiJob job) => Workflow.Single(w => w.Key == job.Key);

    /// <summary>Where a shell line stops being the command: <c>||</c>, <c>&amp;&amp;</c>, a pipe or a redirection.</summary>
    [GeneratedRegex(@" (\|\||&&|\||2>|>)")]
    private static partial Regex ShellControl();

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "Cadence.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName ?? throw new InvalidOperationException("Cadence.sln not found above the test output.");
        }

        return dir;
    }

    private sealed record WorkflowStep(string Name, string? If, string? Run);

    private sealed record WorkflowJob(string Key, string Name, string? If, IReadOnlyList<string> Needs, List<WorkflowStep> Steps);

    /// <summary>
    /// Reads the parts of a GitHub Actions workflow this test needs (job keys, names, <c>if</c>, single-line
    /// <c>needs</c>, and each step's name, <c>if</c> and <c>run</c>), relying on the two-space indentation ci.yml uses.
    /// </summary>
    private static List<WorkflowJob> ParseWorkflow(string[] lines)
    {
        var jobs = new List<WorkflowJob>();
        bool inJobs = false;
        string? key = null, name = null, jobIf = null;
        IReadOnlyList<string> needs = [];
        List<WorkflowStep> steps = [];
        string? stepName = null, stepIf = null, stepRun = null;
        bool inStep = false;

        void FlushStep()
        {
            if (inStep && stepName is not null) steps.Add(new WorkflowStep(stepName, stepIf, stepRun));
            inStep = false;
            stepName = stepIf = stepRun = null;
        }

        void FlushJob()
        {
            FlushStep();
            if (key is not null) jobs.Add(new WorkflowJob(key, name ?? key, jobIf, needs, steps));
            key = name = jobIf = null;
            needs = [];
            steps = [];
        }

        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i++];
            if (line == "jobs:")
            {
                inJobs = true;
            }
            else if (!inJobs)
            {
                // The triggers and permissions above 'jobs:' are not needed.
            }
            else if (TryMatch(line, @"^  ([A-Za-z0-9_-]+):\s*$", out var jobKey))
            {
                FlushJob();
                key = jobKey;
            }
            else if (key is null)
            {
                // Nothing before the first job key.
            }
            else if (TryMatch(line, @"^    name: (.+)$", out var jobName))
            {
                name = Unquote(jobName);
            }
            else if (TryMatch(line, @"^    if: (.+)$", out var condition))
            {
                if (condition is ">-" or ">" or "|")
                {
                    var block = new List<string>();
                    while (i < lines.Length && (lines[i].Trim().Length == 0 || lines[i].StartsWith("      ", StringComparison.Ordinal)))
                    {
                        block.Add(lines[i].Trim());
                        i++;
                    }

                    jobIf = string.Join(' ', block);
                }
                else
                {
                    jobIf = condition;
                }
            }
            else if (TryMatch(line, @"^    needs: \[(.*)\]\s*$", out var needList))
            {
                needs = [.. needList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
            else if (line.TrimEnd() == "    needs:")
            {
                var rawNeeds = new List<string>();
                while (i < lines.Length && lines[i].StartsWith("      ", StringComparison.Ordinal))
                {
                    rawNeeds.Add(lines[i].Trim().Trim('[', ']'));
                    i++;
                }

                needs = [.. string.Join(',', rawNeeds).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
            else if (TryMatch(line, @"^      - (.*)$", out var firstKey))
            {
                FlushStep();
                inStep = true;
                if (TryMatch(firstKey, @"^name: (.+)$", out var dashName)) stepName = Unquote(dashName);
            }
            else if (!inStep)
            {
                // Job settings other than name, if and needs.
            }
            else if (TryMatch(line, @"^        name: (.+)$", out var nestedName))
            {
                stepName = Unquote(nestedName);
            }
            else if (TryMatch(line, @"^        if: (.+)$", out var stepCondition))
            {
                stepIf = stepCondition;
            }
            else if (TryMatch(line, @"^        run: (.*)$", out var run))
            {
                if (!run.StartsWith('|'))
                {
                    stepRun = run;
                    continue;
                }

                // A block scalar: every following line that is blank or indented further than 'run:'.
                var block = new List<string>();
                while (i < lines.Length && (lines[i].Trim().Length == 0 || lines[i].StartsWith("          ", StringComparison.Ordinal)))
                {
                    block.Add(lines[i].Trim());
                    i++;
                }

                stepRun = string.Join('\n', block);
            }
        }

        FlushJob();
        return jobs;
    }

    private static bool TryMatch(string input, string pattern, out string group)
    {
        var match = Regex.Match(input, pattern);
        group = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private static string Unquote(string value) => value.Trim().Trim('"', '\'');
}
