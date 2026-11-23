using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
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
    private static readonly IReadOnlyDictionary<string, string> UsesCommands = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["rhysd/actionlint"] = "actionlint",
        ["docker/build-push-action"] = "docker build",
        ["aquasecurity/trivy-action"] = "trivy image",
    };

    private static readonly HashSet<string> SetupAndArtifactActions = new(StringComparer.Ordinal)
    {
        "actions/checkout",
        "actions/setup-dotnet",
        "actions/setup-node",
        "pnpm/action-setup",
        "actions/cache",
        "actions/upload-artifact",
        "actions/download-artifact",
        "docker/setup-buildx-action",
        "github/codeql-action/upload-sarif",
    };

    private static readonly IReadOnlyDictionary<string, string> RootPackageScripts = LoadRootPackageScripts();

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
        FindInvalidInProcessSteps(CiPlan.Jobs).Should().BeEmpty();

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
            else
            {
                step.InProcessCiText.Should().BeNull($"'{job.Key} / {step.Name}' has null InProcess");
                step.InProcessDisplay.Should().BeNull($"'{job.Key} / {step.Name}' has null InProcess");
                step.InProcessForwardsBaseRef.Should().BeFalse($"'{job.Key} / {step.Name}' has null InProcess");
            }
        }
    }

    /// <summary>
    /// Each checked command (<c>actionlint</c>, <c>hadolint</c>, <c>shellcheck</c>, <c>docker</c>, <c>trivy</c>,
    /// <c>dotnet</c>, <c>pnpm</c>, <c>node</c>, <c>bash</c>, <c>python3</c> and <c>git diff --exit-code</c>) of a
    /// mirrored step is run locally with the same arguments and <c>ForwardsBaseRef</c> semantics. Only shell variables
    /// (<c>$CHANGED_FILES</c>, <c>"$RUNNER_TEMP/..."</c>) may follow, and anything else only where the local command
    /// declares its CI text because it differs on purpose.
    /// </summary>
    [Fact]
    public void EveryDotnetPnpmAndGitDiffCommandInTheWorkflow_IsRunLocallyWithTheSameArguments()
    {
        FindUnmappedWorkflowCommands(CiPlan.Jobs, Workflow).Should().BeEmpty();
        FindForwardsBaseRefMismatches(CiPlan.Jobs, Workflow).Should().BeEmpty();
    }

    [Fact]
    public void EveryUsesActionInMirroredJobs_IsSetupArtifactOrMappedToALocalCommand()
    {
        foreach (var job in CiPlan.Jobs)
        {
            foreach (var workflowStep in WorkflowJobFor(job).Steps.Where(s => s.Uses is not null))
            {
                bool known = SetupAndArtifactActions.Contains(workflowStep.Uses!) || UsesCommands.ContainsKey(workflowStep.Uses!);
                known.Should().BeTrue($"'{job.Key} / {workflowStep.Name}' uses '{workflowStep.Uses}', which must be mapped in LocalCiPlanTests");
            }
        }
    }

    private static List<string> FindInvalidInProcessSteps(IReadOnlyList<CiJob> planJobs)
    {
        var invalid = new List<string>();
        foreach (var job in planJobs)
        {
            foreach (var step in job.Steps)
            {
                if (step.InProcess is not null)
                {
                    if (string.IsNullOrWhiteSpace(step.InProcessCiText))
                    {
                        invalid.Add($"'{job.Key} / {step.Name}' defines InProcess without InProcessCiText");
                    }
                }
                else
                {
                    if (step.InProcessCiText is not null)
                    {
                        invalid.Add($"'{job.Key} / {step.Name}' sets InProcessCiText while InProcess is null");
                    }

                    if (step.InProcessDisplay is not null)
                    {
                        invalid.Add($"'{job.Key} / {step.Name}' sets InProcessDisplay while InProcess is null");
                    }

                    if (step.InProcessForwardsBaseRef)
                    {
                        invalid.Add($"'{job.Key} / {step.Name}' sets InProcessForwardsBaseRef while InProcess is null");
                    }
                }
            }
        }

        return invalid;
    }

    private static List<string> FindUnmappedWorkflowCommands(IReadOnlyList<CiJob> planJobs, IReadOnlyList<WorkflowJob> workflow)
    {
        var unmapped = new List<string>();
        foreach (var job in planJobs)
        {
            var workflowJob = workflow.Single(w => w.Key == job.Key);
            foreach (var step in job.Steps.Where(s => s.NotRunLocally is null))
            {
                var workflowStep = workflowJob.Steps.Single(s => s.Name == step.Name);
                string rawRun = workflowStep.Run ?? "";
                var local = step.Commands.Select(c => (Text: Normalize(c.MatchText), Declared: c.CiText is not null, ForwardsBaseRef: c.ForwardsBaseRef))
                    .Concat(step.InProcess is null || step.InProcessCiText is null
                        ? []
                        : [(Text: Normalize(step.InProcessCiText), Declared: false, ForwardsBaseRef: step.InProcessForwardsBaseRef)])
                    .ToList();
                var workflowCommands = rawRun.Split('\n')
                    .Select(line => ShellControl().Split(Normalize(line))[0])
                    .Where(IsCheckedWorkflowCommand);

                foreach (var line in workflowCommands.Where(line => !local.Any(l => SameCommand(line, rawRun, l.Text, l.Declared, l.ForwardsBaseRef))))
                {
                    unmapped.Add($"'{job.Key} / {step.Name}' runs '{line}' in ci.yml, so cadence ci must run it with the same arguments and --base forwarding");
                }
            }
        }

        return unmapped;
    }

    private static List<string> FindForwardsBaseRefMismatches(IReadOnlyList<CiJob> planJobs, IReadOnlyList<WorkflowJob> workflow)
    {
        var mismatches = new List<string>();
        foreach (var job in planJobs)
        {
            var workflowJob = workflow.Single(w => w.Key == job.Key);
            foreach (var step in job.Steps.Where(s => s.NotRunLocally is null))
            {
                var workflowStep = workflowJob.Steps.Single(s => s.Name == step.Name);
                string rawRun = workflowStep.Run ?? "";
                var workflowLines = rawRun.Split('\n')
                    .Select(line => ShellControl().Split(Normalize(line))[0])
                    .Where(IsCheckedWorkflowCommand)
                    .ToList();

                foreach (var command in step.Commands)
                {
                    string localText = Normalize(command.MatchText);
                    bool declared = command.CiText is not null;
                    var matchingLines = workflowLines
                        .Where(line => MatchesCommandIgnoringBaseRef(line, localText, declared))
                        .ToList();
                    if (matchingLines.Count == 0)
                    {
                        continue;
                    }

                    bool workflowForwardsBaseRef = matchingLines.Any(line => WorkflowCommandForwardsBaseRef(line, rawRun));
                    if (command.ForwardsBaseRef != workflowForwardsBaseRef)
                    {
                        mismatches.Add(
                            $"'{job.Key} / {step.Name}' command '{command.Display}' sets ForwardsBaseRef={command.ForwardsBaseRef}, " +
                            $"but ci.yml forwards --base={workflowForwardsBaseRef}");
                    }
                }

                if (step.InProcess is not null && step.InProcessCiText is not null)
                {
                    string inProcessText = Normalize(step.InProcessCiText);
                    var matchingLines = workflowLines
                        .Where(line => MatchesCommandIgnoringBaseRef(line, inProcessText, declaredDifference: false))
                        .ToList();
                    if (matchingLines.Count > 0)
                    {
                        bool workflowForwardsBaseRef = matchingLines.Any(line => WorkflowCommandForwardsBaseRef(line, rawRun));
                        if (step.InProcessForwardsBaseRef != workflowForwardsBaseRef)
                        {
                            mismatches.Add(
                                $"'{job.Key} / {step.Name}' InProcess '{inProcessText}' sets InProcessForwardsBaseRef={step.InProcessForwardsBaseRef}, " +
                                $"but ci.yml forwards --base={workflowForwardsBaseRef}");
                        }
                    }
                }
            }
        }

        return mismatches;
    }

    private static bool IsCheckedWorkflowCommand(string line) =>
        line == "actionlint"
        || line.StartsWith("actionlint ", StringComparison.Ordinal)
        || line == "hadolint"
        || line.StartsWith("hadolint ", StringComparison.Ordinal)
        || line == "shellcheck"
        || line.StartsWith("shellcheck ", StringComparison.Ordinal)
        || line.StartsWith("docker ", StringComparison.Ordinal)
        || line == "trivy"
        || line.StartsWith("trivy ", StringComparison.Ordinal)
        || line.StartsWith("dotnet ", StringComparison.Ordinal)
        || line.StartsWith("pnpm ", StringComparison.Ordinal)
        || line.StartsWith("node ", StringComparison.Ordinal)
        || line.StartsWith("bash ", StringComparison.Ordinal)
        || (line.StartsWith("python3 ", StringComparison.Ordinal)
            && !line.StartsWith("python3 -m venv ", StringComparison.Ordinal)
            && !line.StartsWith("python3 -m pip ", StringComparison.Ordinal))
        || line.StartsWith("git diff --exit-code", StringComparison.Ordinal);

    private static bool SameCommand(string workflowLine, string stepRun, string local, bool declaredDifference, bool localForwardsBaseRef) =>
        MatchesCommandIgnoringBaseRef(workflowLine, local, declaredDifference)
        && WorkflowCommandForwardsBaseRef(workflowLine, stepRun) == localForwardsBaseRef;

    private static bool MatchesCommandIgnoringBaseRef(string workflowLine, string local, bool declaredDifference)
    {
        if (workflowLine == local) return true;
        if (!workflowLine.StartsWith(local + " ", StringComparison.Ordinal)) return false;
        return declaredDifference
            || workflowLine[(local.Length + 1)..].Split(' ').All(token => token.StartsWith('$') || token.StartsWith("\"$", StringComparison.Ordinal));
    }

    private static bool WorkflowCommandForwardsBaseRef(string workflowLine, string stepRun) =>
        workflowLine.Contains("--base ", StringComparison.Ordinal)
        || ((workflowLine.Contains("\"${SCOPE_ARGS[@]}\"", StringComparison.Ordinal)
                || workflowLine.Contains("${SCOPE_ARGS[@]}", StringComparison.Ordinal))
            && stepRun.Contains("--base ", StringComparison.Ordinal));

    [Fact]
    public void StepsAreLeftOutLocally_OnlyWhenTheWorkflowRunsThemOnPushAlone()
    {
        foreach (var (job, step, workflowStep) in PlanStepsWithWorkflowSteps().Where(t => t.Step.NotRunLocally is not null))
        {
            step.NotRunLocally.Should().NotBeNullOrWhiteSpace();
            bool pushOnly = RulesetService.RequiresPushEvent(WorkflowJobFor(job).If ?? "")
                || RulesetService.RequiresPushEvent(workflowStep.If ?? "");
            bool runnerProvisioning = (workflowStep.Run ?? "").Contains("steps.playwright-cache.outputs.cache-hit", StringComparison.Ordinal);
            bool githubSarifUpload = workflowStep.Uses == "github/codeql-action/upload-sarif";
            (pushOnly || runnerProvisioning || githubSarifUpload).Should().BeTrue($"'{job.Key} / {step.Name}' is never run locally");
        }

        foreach (var job in CiPlan.Jobs)
        {
            bool pushOnly = WorkflowJobFor(job).If is { } jobIf && RulesetService.RequiresPushEvent(jobIf);
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
        FindCoveredByViolations(CiPlan.Jobs, RootPackageScripts).Should().BeEmpty();
    }

    private static List<string> FindCoveredByViolations(
        IReadOnlyList<CiJob> planJobs,
        IReadOnlyDictionary<string, string> packageScripts)
    {
        var violations = new List<string>();
        var stepsInOrder = planJobs.SelectMany(j => j.Steps.Select(s => (Job: j, Step: s))).ToList();
        var indexByKey = stepsInOrder
            .Select((entry, idx) => (entry.Step.Key, Index: idx))
            .ToDictionary(x => x.Key, x => x.Index, StringComparer.Ordinal);

        for (int idx = 0; idx < stepsInOrder.Count; idx++)
        {
            var (job, step) = stepsInOrder[idx];
            if (step.CoveredBy is null)
            {
                continue;
            }

            if (!indexByKey.TryGetValue(step.CoveredBy, out int coverIdx))
            {
                violations.Add($"'{step.Key}' sets CoveredBy='{step.CoveredBy}', which does not exist in CiPlan.Jobs");
                continue;
            }

            if (coverIdx >= idx)
            {
                violations.Add($"'{step.Key}' sets CoveredBy='{step.CoveredBy}', which does not run earlier");
                continue;
            }

            var (coverJob, coverStep) = stepsInOrder[coverIdx];
            if (coverStep.NotRunLocally is not null)
            {
                violations.Add($"'{step.Key}' cannot be covered by '{coverStep.Key}' because '{coverStep.Key}' has NotRunLocally");
                continue;
            }

            if ((coverJob.OnRequestOnly is not null || coverJob.Key == CiPlan.ContainerSecurityJobKey) && coverJob.Key != job.Key)
            {
                violations.Add($"'{step.Key}' cannot be covered by conditional or on-request job '{coverJob.Key}'");
                continue;
            }

            if (step.InProcess is not null || step.Commands.Count == 0)
            {
                violations.Add($"'{step.Key}' sets CoveredBy='{step.CoveredBy}' without local Commands (or with InProcess)");
                continue;
            }

            foreach (var command in step.Commands)
            {
                if (!IsCommandCoveredByStep(command, coverStep, packageScripts, out string reason))
                {
                    violations.Add($"'{step.Key}' command '{command.Display}' is not covered by '{coverStep.Key}': {reason}");
                }
            }
        }

        return violations;
    }

    private static bool IsCommandCoveredByStep(
        CiCommand command,
        CiStep coverStep,
        IReadOnlyDictionary<string, string> packageScripts,
        out string reason)
    {
        if (coverStep.Commands.Any(c => Normalize(c.MatchText) == Normalize(command.MatchText) && c.ForwardsBaseRef == command.ForwardsBaseRef))
        {
            reason = "";
            return true;
        }

        string commandText = Normalize(command.MatchText);
        if (commandText.StartsWith("pnpm ", StringComparison.Ordinal))
        {
            foreach (var coverCmd in coverStep.Commands)
            {
                string coverText = Normalize(coverCmd.MatchText);
                if (coverText.StartsWith("pnpm ", StringComparison.Ordinal))
                {
                    string scriptName = coverText["pnpm ".Length..].Trim();
                    if (packageScripts.TryGetValue(scriptName, out string? scriptBody))
                    {
                        var chainedCommands = scriptBody
                            .Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(Normalize);
                        if (chainedCommands.Contains(commandText, StringComparer.Ordinal))
                        {
                            reason = "";
                            return true;
                        }
                    }
                }
            }
        }

        if (TryExtractSingleCheckGate(command, out string? gateName))
        {
            var resolvedGate = ResolveQualityGate(gateName!);
            if (resolvedGate is null)
            {
                reason = $"unknown quality gate '{gateName}'";
                return false;
            }

            foreach (var coverCmd in coverStep.Commands)
            {
                if (!TryExtractFullCheckExclusions(coverCmd, out var excludedGates))
                {
                    continue;
                }

                var resolvedExcluded = new HashSet<string>(StringComparer.Ordinal);
                foreach (string excluded in excludedGates)
                {
                    var excludedGate = ResolveQualityGate(excluded);
                    if (excludedGate is null)
                    {
                        reason = $"covering command '{coverCmd.Display}' excludes unknown gate '{excluded}'";
                        return false;
                    }

                    resolvedExcluded.Add(excludedGate.Name);
                }

                if (resolvedExcluded.Contains(resolvedGate.Name))
                {
                    reason = $"covering command '{coverCmd.Display}' excludes gate '{resolvedGate.Name}'";
                    return false;
                }

                if (command.ForwardsBaseRef && !coverCmd.ForwardsBaseRef)
                {
                    reason = $"covered command forwards --base, but covering command '{coverCmd.Display}' does not";
                    return false;
                }

                reason = "";
                return true;
            }

            reason = $"no full 'cadence check' command in '{coverStep.Key}' covers gate '{resolvedGate.Name}'";
            return false;
        }

        reason = $"no matching command in '{coverStep.Key}'";
        return false;
    }

    private static bool TryExtractSingleCheckGate(CiCommand command, out string? gateName)
    {
        gateName = null;
        var checkArgs = ExtractCliArgumentsAfterCheck(command);
        if (checkArgs is null || checkArgs.Count != 1 || checkArgs[0].StartsWith('-'))
        {
            return false;
        }

        gateName = checkArgs[0];
        return true;
    }

    private static bool TryExtractFullCheckExclusions(CiCommand command, out List<string> excludedGates)
    {
        excludedGates = [];
        var checkArgs = ExtractCliArgumentsAfterCheck(command);
        if (checkArgs is null)
        {
            return false;
        }

        int index = 0;
        while (index < checkArgs.Count)
        {
            if (checkArgs[index] is "--exclude" or "-e" && index + 1 < checkArgs.Count)
            {
                excludedGates.Add(checkArgs[index + 1]);
                index += 2;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string>? ExtractCliArgumentsAfterCheck(CiCommand command)
    {
        if (command.FileName == "node"
            && command.Arguments.Count >= 2
            && command.Arguments[0] == "scripts/run-cli.js"
            && command.Arguments[1] == "check")
        {
            return [.. command.Arguments.Skip(2)];
        }

        if (command.FileName == "dotnet")
        {
            int separator = -1;
            for (int i = 0; i < command.Arguments.Count; i++)
            {
                if (command.Arguments[i] == "--")
                {
                    separator = i;
                    break;
                }
            }

            if (separator >= 0
                && separator + 1 < command.Arguments.Count
                && command.Arguments[separator + 1] == "check")
            {
                return [.. command.Arguments.Skip(separator + 2)];
            }
        }

        return null;
    }

    private static QualityGate? ResolveQualityGate(string rawName)
    {
        string normalized = rawName.Trim().ToLowerInvariant().Replace('_', '-');
        return QualityGates.All.FirstOrDefault(
            g => g.AllNames.Any(alias => string.Equals(alias, normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyDictionary<string, string> LoadRootPackageScripts()
    {
        string packageJsonPath = Path.Combine(RepoRoot(), "package.json");
        var root = JsonNode.Parse(File.ReadAllText(packageJsonPath))!.AsObject();
        var scriptsObj = root["scripts"]!.AsObject();
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in scriptsObj)
        {
            if (value is not null)
            {
                scripts[name] = value.GetValue<string>();
            }
        }

        return scripts;
    }

    [Fact]
    public void Select_WithoutJobs_RunsEveryJobThatIsNotOnRequestOnly()
    {
        CiPlan.Select([]).Select(j => j.Key).Should().Equal(
            "actionlint",
            "native-aot-build",
            "lint-and-static",
            "dotnet-quality",
            "docs-build",
            "security",
            "container-security",
            "unit-tests",
            "dotnet-tests",
            "accessibility-e2e",
            "compliance");
    }

    [Fact]
    public void ParseWorkflow_ExtractsMultilineNeedsForCiGateAndSyntheticWorkflows()
    {
        Workflow.Single(j => j.Key == "ci-gate").Needs.Should().Equal(
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
            "compliance");

        var parsed = ParseWorkflow(
        [
            "jobs:",
            "  synthetic-gate:",
            "    name: Synthetic Gate",
            "    needs:",
            "      [",
            "        classify,",
            "        actionlint,",
            "        lint-and-static,",
            "        dotnet-quality,",
            "        native-aot-build,",
            "        docs-build,",
            "        security,",
            "        unit-tests,",
            "        dotnet-tests,",
            "        compliance,",
            "      ]",
            "  block-needs:",
            "    needs:",
            "      - actionlint",
            "      - security",
        ]);

        parsed[0].Needs.Should().HaveCount(10);
        parsed[1].Needs.Should().Equal("actionlint", "security");
    }

    [Theory]
    [InlineData("node scripts/run-cli.js check unknown-gate")]
    [InlineData("bash scripts/unmapped-gate.sh")]
    [InlineData("python3 scripts/unmapped-check.py")]
    [InlineData("hadolint --failure-threshold warning \"${DOCKERFILES[@]}\"")]
    [InlineData("shellcheck --severity=warning \"${SHELL_SCRIPTS[@]}\"")]
    [InlineData("docker build -t cadence-extra:latest .")]
    [InlineData("trivy fs .")]
    public void FindUnmappedWorkflowCommands_RejectsUnmappedNodeBashPython3HadolintShellCheckDockerAndTrivyCommands(string driftedCommand)
    {
        var parsed = ParseWorkflow(
        [
            "jobs:",
            "  sample:",
            "    name: Sample",
            "    steps:",
            "      - name: Run actionlint",
            "        uses: rhysd/actionlint@914e7df21a07ef503a81201c76d2b11c789d3fca # v1.7.12",
            "      - name: Check",
            $"        run: {driftedCommand}",
        ]);
        var plan = new[]
        {
            new CiJob("sample", "Sample",
            [
                new CiStep("sample", "Run actionlint") { Commands = [new CiCommand("actionlint", [])] },
                new CiStep("sample", "Check") { Commands = [new CiCommand("dotnet", ["build", "Cadence.sln"])] },
            ]),
        };

        FindUnmappedWorkflowCommands(plan, parsed).Should().ContainSingle()
            .Which.Should().Contain("sample / Check").And.Contain(driftedCommand);
    }

    [Fact]
    public void NullInProcessWithCiTextOrDisplay_IsRejectedByInProcessAndCommandParityChecks()
    {
        var parsed = ParseWorkflow(
        [
            "jobs:",
            "  dotnet-tests:",
            "    name: Tests",
            "    steps:",
            "      - name: Verify DB",
            "        run: |",
            "          bash scripts/test-check-database-test-results.sh",
            "          bash scripts/check-database-test-results.sh",
        ]);
        var plan = new[]
        {
            new CiJob("dotnet-tests", "Tests",
            [
                new CiStep("dotnet-tests", "Verify DB")
                {
                    Commands = [new CiCommand("bash", ["scripts/test-check-database-test-results.sh"])],
                    InProcessDisplay = "verify DB",
                    InProcessCiText = "bash scripts/check-database-test-results.sh",
                },
            ]),
        };

        FindInvalidInProcessSteps(plan).Should().Equal(
            "'dotnet-tests / Verify DB' sets InProcessCiText while InProcess is null",
            "'dotnet-tests / Verify DB' sets InProcessDisplay while InProcess is null");
        FindUnmappedWorkflowCommands(plan, parsed).Should().ContainSingle()
            .Which.Should().Contain("bash scripts/check-database-test-results.sh");
    }

    [Fact]
    public void FindForwardsBaseRefMismatches_RejectsMissingOrUnexpectedBaseRefForwarding()
    {
        var parsed = ParseWorkflow(
        [
            "jobs:",
            "  quality:",
            "    name: Quality",
            "    steps:",
            "      - name: Scoped sentinels",
            "        run: |",
            "          SCOPE_ARGS=(--base \"origin/$BASE_REF\")",
            "          node scripts/run-cli.js check deid \"${SCOPE_ARGS[@]}\"",
            "      - name: Full markdown",
            "        run: node scripts/run-cli.js check markdown",
            "      - name: Snapshot guard",
            "        run: |",
            "          SCOPE_ARGS=(--base \"origin/$BASE_REF\")",
            "          node scripts/run-cli.js check gxp-snapshot \"${SCOPE_ARGS[@]}\"",
        ]);
        var driftedPlan = new[]
        {
            new CiJob("quality", "Quality",
            [
                new CiStep("quality", "Scoped sentinels")
                {
                    Commands = [new CiCommand("node", ["scripts/run-cli.js", "check", "deid"], ForwardsBaseRef: false)],
                },
                new CiStep("quality", "Full markdown")
                {
                    Commands = [new CiCommand("node", ["scripts/run-cli.js", "check", "markdown"], ForwardsBaseRef: true)],
                },
                new CiStep("quality", "Snapshot guard")
                {
                    InProcess = (_, _) => 0,
                    InProcessDisplay = "check snapshot",
                    InProcessCiText = "node scripts/run-cli.js check gxp-snapshot",
                    InProcessForwardsBaseRef = false,
                },
            ]),
        };

        FindForwardsBaseRefMismatches(driftedPlan, parsed).Should().HaveCount(3);
        FindUnmappedWorkflowCommands(driftedPlan, parsed).Should().HaveCount(3);
    }

    [Fact]
    public void FindCoveredByViolations_RejectsExcludedGatesAliasesUncoveredCommandsAndOutOfOrderSteps()
    {
        var driftedPlan = new[]
        {
            new CiJob("dotnet-quality", "Quality",
            [
                new CiStep("dotnet-quality", "Run Cadence quality sentinels")
                {
                    Commands =
                    [
                        new CiCommand("dotnet", ["run", "--no-build", "--project", "packages/cli/src/Cadence.Cli.csproj", "--", "check", "--exclude", "vulnerabilities", "--exclude", "audit-security"], ForwardsBaseRef: true),
                    ],
                },
            ]),
            new CiJob("security", "Security",
            [
                new CiStep("security", "Excluded gate")
                {
                    CoveredBy = "dotnet-quality/Run Cadence quality sentinels",
                    Commands = [new CiCommand("node", ["scripts/run-cli.js", "check", "vulnerabilities"])],
                },
                new CiStep("security", "Excluded via alias")
                {
                    CoveredBy = "dotnet-quality/Run Cadence quality sentinels",
                    Commands = [new CiCommand("node", ["scripts/run-cli.js", "check", "security"])],
                },
                new CiStep("security", "Wrong command")
                {
                    CoveredBy = "dotnet-quality/Run Cadence quality sentinels",
                    Commands = [new CiCommand("pnpm", ["install", "--frozen-lockfile"])],
                },
                new CiStep("security", "Later target")
                {
                    CoveredBy = "security/Final step",
                    Commands = [new CiCommand("pnpm", ["lint"])],
                },
                new CiStep("security", "Final step")
                {
                    Commands = [new CiCommand("pnpm", ["lint"])],
                },
            ]),
        };

        var violations = FindCoveredByViolations(driftedPlan, RootPackageScripts);
        violations.Should().HaveCount(4);
        violations.Should().Contain(v => v.Contains("excludes gate 'vulnerabilities'", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Contains("excludes gate 'security'", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Contains("pnpm install --frozen-lockfile", StringComparison.Ordinal));
        violations.Should().Contain(v => v.Contains("does not run earlier", StringComparison.Ordinal));
    }

    [Fact]
    public void ContainerSecurityJob_MatchesWorkflowMatrixTrivySettingsAndClassifyTriggerGlobs()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        string raw = string.Join('\n', lines);

        var containerJob = CiPlan.Jobs.Single(j => j.Key == CiPlan.ContainerSecurityJobKey);
        containerJob.Needs.Should().Equal("classify");
        CiPlan.ContainerScanTargets.Should().HaveCount(20);

        var composeStep = containerJob.Steps.Single(s => s.Name == "Validate Docker Compose configuration");
        composeStep.RequiredTool.Should().Be("docker");
        composeStep.NeedsDocker.Should().BeTrue();
        composeStep.Commands.Select(c => c.Display).Should().Equal(
            "docker compose -f docker/docker-compose.yml config -q");

        var buildStep = containerJob.Steps.Single(s => s.Name == "Build container image for security scan");
        buildStep.NeedsDocker.Should().BeTrue();
        buildStep.Commands.Select(c => c.Display).Should().Equal(
            CiPlan.ContainerScanTargets.Select(t => string.IsNullOrEmpty(t.AppName)
                ? $"docker build -t {t.ImageTag} -f {t.Dockerfile} {t.Context}"
                : $"docker build -t {t.ImageTag} -f {t.Dockerfile} --target runtime --build-arg APP_NAME={t.AppName} {t.Context}"));

        var scanStep = containerJob.Steps.Single(s => s.Name == "Run container vulnerability scan");
        scanStep.RequiredTool.Should().Be("trivy");
        scanStep.NeedsDocker.Should().BeTrue();
        scanStep.Commands.Select(c => c.Display).Should().Equal(
            CiPlan.ContainerScanTargets.Select(t =>
                $"trivy image --exit-code 1 --ignore-unfixed --pkg-types os,library --severity CRITICAL,HIGH {t.ImageTag}"));

        var sarifStep = containerJob.Steps.Single(s => s.Name == "Upload Trivy scan results to GitHub Security tab");
        sarifStep.NotRunLocally.Should().NotBeNullOrWhiteSpace();

        foreach (var target in CiPlan.ContainerScanTargets)
        {
            raw.Should().Contain($"- target: {target.Target}");
            raw.Should().Contain($"context: {target.Context}");
            raw.Should().Contain($"dockerfile: {target.Dockerfile}");
        }

        raw.Should().Contain("docker compose -f docker/docker-compose.yml config -q")
            .And.Contain("docker/setup-buildx-action@")
            .And.Contain("docker/build-push-action@")
            .And.Contain("cache-from: type=gha")
            .And.Contain("cache-to: type=gha,mode=max")
            .And.Contain("exit-code: \"1\"")
            .And.Contain("ignore-unfixed: true")
            .And.Contain("vuln-type: \"os,library\"")
            .And.Contain("severity: \"CRITICAL,HIGH\"");

        var triggerLine = lines
            .Select(l => l.Trim())
            .Single(l => l.StartsWith("docker/*|", StringComparison.Ordinal) && l.EndsWith(')'));
        var workflowGlobs = triggerLine.TrimEnd(')').Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        CiPlan.ContainerScanTriggerGlobs.Should().Equal(workflowGlobs);

        CiPlan.IsContainerScanTrigger("apps/gateway/src/Program.cs").Should().BeTrue();
        CiPlan.IsContainerScanTrigger("packages/kernel/src/Cadence.Kernel.Domain/Entity.cs").Should().BeTrue();
        CiPlan.IsContainerScanTrigger("Cadence.sln").Should().BeTrue();
        CiPlan.IsContainerScanTrigger(".editorconfig").Should().BeTrue();
        CiPlan.IsContainerScanTrigger("BannedSymbols.txt").Should().BeTrue();
        CiPlan.IsContainerScanTrigger("package.json").Should().BeFalse();
        CiPlan.IsContainerScanTrigger("apps/web/package.json").Should().BeFalse();
        CiPlan.IsContainerScanTrigger("pnpm-lock.yaml").Should().BeFalse();
        CiPlan.IsContainerScanTrigger("tests/Cadence.UnitTests/SampleTests.cs").Should().BeFalse();
    }

    [Fact]
    public void Select_AddsTheJobsANamedJobNeeds_InPlanOrder()
    {
        CiPlan.Select(["container-security"]).Select(j => j.Key).Should().Equal("container-security");
        CiPlan.Select(["compliance"]).Select(j => j.Key).Should().Equal("native-aot-build", "unit-tests", "dotnet-tests", "compliance");
        CiPlan.Select(["lint-and-static"]).Select(j => j.Key).Should().Equal("native-aot-build", "lint-and-static");
        CiPlan.Select(["docs-build"]).Select(j => j.Key).Should().Equal("native-aot-build", "docs-build");
        CiPlan.Select(["security"]).Select(j => j.Key).Should().Equal("native-aot-build", "security");
        CiPlan.Select(["accessibility-e2e", "dotnet-quality"]).Select(j => j.Key).Should().Equal("dotnet-quality", "accessibility-e2e");
    }

    [Theory]
    [InlineData("no-such-job", "Unknown job")]
    [InlineData("automerge", "Unknown job")]
    [InlineData("ci-gate", "no local equivalent")]
    [InlineData("classify", "no local equivalent")]
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
            .And.Contain("refs/tags/v")
            .And.Contain("github.event_name == 'workflow_dispatch'");
        deployDocsJob.If.Should().Contain("refs/heads/main")
            .And.Contain("github.event_name == 'workflow_dispatch'")
            .And.NotContain("release-and-archive");
    }

    [Fact]
    public void DownstreamWorkflows_AreWorkflowCallOnly_AndProductionPipelinePublishesImmutableReleaseEvidence()
    {
        string deployDocsRaw = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "deploy-docs.yml"));
        deployDocsRaw.Should().Contain("workflow_call:")
            .And.NotContain("workflow_dispatch")
            .And.NotContain("CADENCE_DOCS_SKIP_CHECKS");

        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "production-pipeline.yml"));
        string raw = string.Join('\n', lines);

        raw.Should().Contain("workflow_call:")
            .And.NotContain("workflow_dispatch:")
            .And.NotContain("github.event_name == 'workflow_call'",
                "reusable workflows inherit the caller's push or workflow_dispatch event_name in GitHub Actions")
            .And.Contain("retention-days: 400");

        var parsedJobs = ParseWorkflow(lines);
        var releaseJob = parsedJobs.Single(j => j.Key == "release-and-archive");
        var downloadDotnetStep = releaseJob.Steps.Single(s => s.Name == "Download Verified .NET Test Evidence");
        var downloadBrowserStep = releaseJob.Steps.Single(s => s.Name == "Download Verified Browser-to-Database Test Evidence");
        var verifyEvidenceStep = releaseJob.Steps.Single(s => s.Name == "Verify Combined Database and Browser Test Evidence");
        var generateStep = releaseJob.Steps.Single(s => s.Name == "Generate GxP Reports");

        downloadDotnetStep.Uses.Should().Be("actions/download-artifact");
        downloadBrowserStep.Uses.Should().Be("actions/download-artifact");
        verifyEvidenceStep.Run.Should().Contain("bash scripts/check-database-test-results.sh");

        int verifyIndex = releaseJob.Steps.IndexOf(verifyEvidenceStep);
        int generateIndex = releaseJob.Steps.IndexOf(generateStep);
        verifyIndex.Should().BeLessThan(generateIndex,
            "combined .NET and browser TRX evidence must be verified before committed docs/SDLC reports are deleted");
        generateStep.Run.Should().Contain("rm -f docs/SDLC/Requirements_Traceability_Matrix.md docs/SDLC/IQ_OQ_PQ_Execution_Report.md")
            .And.Contain("node scripts/run-cli.js gxp snapshot --results-dir TestResults/gxp");

        var containerPublishJob = parsedJobs.Single(j => j.Key == "publish-container-images");
        containerPublishJob.Needs.Should().Equal("release-and-archive");
        containerPublishJob.If.Should().Be("startsWith(github.ref, 'refs/tags/v')");

        var githubReleaseJob = parsedJobs.Single(j => j.Key == "publish-github-release");
        githubReleaseJob.Needs.Should().Equal("release-and-archive", "publish-container-images");
        githubReleaseJob.If.Should().Be("startsWith(github.ref, 'refs/tags/v')");
        githubReleaseJob.Steps.Single(s => s.Name == "Publish Immutable GitHub Release")
            .Run.Should().Contain("gh release create")
            .And.Contain("--verify-tag");
    }

    [Fact]
    public void DeployDocsWorkflow_RunsUnconditionalPreBuildChecksViaWorkflowCall()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "deploy-docs.yml"));
        string raw = string.Join('\n', lines);

        raw.Should().NotContain("github.event_name == 'workflow_call'",
            "reusable workflows inherit the caller's push or workflow_dispatch event_name in GitHub Actions");

        var buildBlocks = ExtractJobStepBlocks(lines, "build");
        string portalBuildBlock = buildBlocks.Single(b => b.Contains("- name: Build Documentation Portal", StringComparison.Ordinal));
        portalBuildBlock.Should().NotContain("CADENCE_DOCS_SKIP_CHECKS")
            .And.Contain("run: pnpm docs:build");
    }

    [Fact]
    public void SetupDotnetSteps_AcrossAllWorkflows_IncludeCompleteNuGetCacheDependencyPaths()
    {
        string workflowsDir = Path.Combine(RepoRoot(), ".github", "workflows");
        var workflowFiles = Directory.EnumerateFiles(workflowsDir, "*.yml")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        int setupDotnetCount = 0;
        var violations = new List<string>();
        foreach (string path in workflowFiles)
        {
            string fileName = Path.GetFileName(path);
            string[] lines = File.ReadAllLines(path);
            violations.AddRange(FindIncompleteSetupDotnetCaches(fileName, lines, out int countInFile));
            setupDotnetCount += countInFile;
        }

        setupDotnetCount.Should().Be(10);
        violations.Should().BeEmpty(
            "every actions/setup-dotnet step must hash global.json, Directory.Build.props, Directory.Build.targets, Directory.Packages.props, NuGet.Config, and **/*.csproj");

        string[] driftedWorkflow =
        [
            "jobs:",
            "  sample:",
            "    steps:",
            "      - name: Setup .NET SDK",
            "        uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73 # v6",
            "        with:",
            "          global-json-file: global.json",
            "          cache: true",
            "          cache-dependency-path: |",
            "            global.json",
            "            Directory.Build.props",
            "            Directory.Packages.props",
            "            **/*.csproj",
        ];

        FindIncompleteSetupDotnetCaches("sample.yml", driftedWorkflow, out int driftedCount)
            .Should().Equal("sample.yml:sample (missing Directory.Build.targets, NuGet.Config)");
        driftedCount.Should().Be(1);
    }

    [Fact]
    public void NativeAotBuild_CachesPublishesAndSharesSingleCliArtifactAcrossDownstreamCliJobs()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        string[] expectedDownstreamJobs = ["lint-and-static", "docs-build", "security", "compliance"];

        var jobsCachingCli = Workflow
            .Where(j => ExtractJobStepBlocks(lines, j.Key).Any(b => b.Contains("path: ./bin/cadence", StringComparison.Ordinal) && b.Contains("actions/cache@", StringComparison.Ordinal)))
            .Select(j => j.Key)
            .ToList();
        jobsCachingCli.Should().Equal("native-aot-build");

        var aotBlocks = ExtractJobStepBlocks(lines, "native-aot-build");
        string publishBlock = aotBlocks.Single(b => b.Contains("- name: Publish the CLI as Native AOT", StringComparison.Ordinal));
        publishBlock.Should().Contain("if: steps.cadence-cli-cache.outputs.cache-hit != 'true'")
            .And.Contain("dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64 -o ./bin");

        string uploadBlock = aotBlocks.Single(b => b.Contains("actions/upload-artifact@", StringComparison.Ordinal));
        uploadBlock.Should().Contain("name: cadence-cli-${{ github.run_id }}-${{ github.run_attempt }}")
            .And.Contain("path: ./bin/cadence")
            .And.Contain("if-no-files-found: error")
            .And.Contain("retention-days: 400");

        foreach (string downstream in expectedDownstreamJobs)
        {
            Workflow.Single(j => j.Key == downstream).Needs.Should().Contain("native-aot-build");
            CiPlan.Jobs.Single(j => j.Key == downstream).Needs.Should().Contain("native-aot-build");

            var stepBlocks = ExtractJobStepBlocks(lines, downstream);
            stepBlocks.Should().ContainSingle(b =>
                b.Contains("actions/download-artifact@", StringComparison.Ordinal) &&
                b.Contains("name: cadence-cli-${{ github.run_id }}-${{ github.run_attempt }}", StringComparison.Ordinal) &&
                b.Contains("path: ./bin", StringComparison.Ordinal));
            string.Join('\n', stepBlocks).Should().NotContain("steps.cadence-cli-cache");
        }

        Workflow.Single(j => j.Key == "compliance").If.Should().Contain("needs.native-aot-build.result == 'success'");

        var localAotPublish = CiPlan.Jobs.Single(j => j.Key == "native-aot-build")
            .Steps.Single(s => s.Name == "Publish the CLI as Native AOT");
        var publishCommand = localAotPublish.Commands.Should().ContainSingle().Subject;
        publishCommand.CiText.Should().Be("dotnet publish packages/cli/src/Cadence.Cli.csproj -c Release -r linux-x64 -o ./bin");
        publishCommand.Arguments.Take(5).Should().Equal(
            "publish",
            "packages/cli/src/Cadence.Cli.csproj",
            "-c",
            "Release",
            "-r");
        publishCommand.Arguments[5].Should().BeOneOf("linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64");
        publishCommand.Arguments.Skip(6).Should().Equal("-o", "./bin");

        var expectedCacheHit = new CiEnvironmentIfPassed(
            "native-aot-build/Publish the CLI as Native AOT",
            "CADENCE_CLI_CACHE_HIT",
            "true");
        string[] nodeCliDownstreamJobs = ["lint-and-static", "docs-build", "security"];
        var nodeCliSteps = CiPlan.Jobs
            .Where(j => nodeCliDownstreamJobs.Contains(j.Key, StringComparer.Ordinal))
            .SelectMany(j => j.Steps)
            .Where(s => s.Commands.Any(c => c.FileName == "node" && c.Arguments.FirstOrDefault() == "scripts/run-cli.js"))
            .ToList();
        nodeCliSteps.Should().HaveCount(8);
        foreach (var step in nodeCliSteps)
        {
            step.EnvironmentIfPassed.Should().Contain(expectedCacheHit, $"{step.Job}/{step.Name} must reuse <worktreeRoot>/bin/cadence");
        }

        var unitTestsStep = CiPlan.Jobs.Single(j => j.Key == "unit-tests")
            .Steps.Single(s => s.Name == "Run frontend unit tests");
        unitTestsStep.Commands.Select(c => c.MatchText).Should().Equal(
            "pnpm -r test",
            "pnpm exec vitest run --config vitest.config.mjs");
    }

    private static List<string> FindIncompleteSetupDotnetCaches(string workflowFile, string[] lines, out int stepCount)
    {
        string[] requiredPaths =
        [
            "global.json",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "NuGet.Config",
            "**/*.csproj",
        ];

        stepCount = 0;
        var violations = new List<string>();
        foreach (var job in ParseWorkflow(lines))
        {
            foreach (string block in ExtractJobStepBlocks(lines, job.Key))
            {
                if (!block.Contains("actions/setup-dotnet@", StringComparison.Ordinal))
                {
                    continue;
                }

                stepCount++;
                var blockLines = block.Split('\n').Select(l => l.Trim()).ToHashSet(StringComparer.Ordinal);
                var missing = new List<string>();
                if (!blockLines.Contains("cache: true"))
                {
                    missing.Add("cache: true");
                }

                missing.AddRange(requiredPaths.Where(p => !blockLines.Contains(p)));
                if (missing.Count > 0)
                {
                    violations.Add($"{workflowFile}:{job.Key} (missing {string.Join(", ", missing)})");
                }
            }
        }

        return violations;
    }

    [Fact]
    public void AotRuntimeIdentifier_IsPortable()
    {
        CiPlan.AotRuntimeIdentifier.Should().MatchRegex("^(linux|osx|win)-(x64|arm64|x86|arm)$");
    }

    [Fact]
    public void LintAndStaticAndSecuritySteps_UseUnifiedPrettierIgnoreAndNonMutatingSecretGates()
    {
        var lintJob = CiPlan.Jobs.Single(j => j.Key == "lint-and-static");
        var styleStep = lintJob.Steps.Single(s => s.Name == "Run frontend style and lint");
        styleStep.Commands[0].Arguments.Should().Equal(
            "exec",
            "prettier",
            "--check",
            "--ignore-path",
            ".prettierignore",
            "**/*.{js,jsx,ts,tsx,mjs,cjs,vue,json,css,md,yaml,yml}");

        var securityJob = CiPlan.Jobs.Single(j => j.Key == "security");
        var secretStep = securityJob.Steps.Single(s => s.Name == "Run secret detection");
        secretStep.Commands.Select(c => c.Display).Should().Equal(
            "node scripts/run-cli.js check clean-secrets",
            "node scripts/run-cli.js check security");
    }

    [Fact]
    public void AccessibilityE2eJob_RunsWebWorkflowSuiteWithDemoModeDisabled()
    {
        var e2eJob = CiPlan.Jobs.Single(j => j.Key == "accessibility-e2e"); // deid-ignore: ci.yml job key
        var workflowStep = e2eJob.Steps.Single(s => s.Name == "Run Web Application E2E Workflow Tests");
        workflowStep.Commands.Select(c => c.Display).Should().Equal("pnpm --filter web test:e2e:workflows");
        workflowStep.Environment.Should().Contain("CADENCE_TEST_BROWSER", "1")
            .And.Contain("DEMO_MODE", "false");

        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        string workflowBlock = ExtractJobStepBlocks(lines, "accessibility-e2e") // deid-ignore: ci.yml job key
            .Single(b => b.Contains("name: Run Web Application E2E Workflow Tests", StringComparison.Ordinal));
        workflowBlock.Should().Contain("DEMO_MODE: \"false\"")
            .And.Contain("run: pnpm --filter web test:e2e:workflows");
    }

    [Fact]
    public void SetupNodePnpmCache_IsUsedOnlyInJobsThatRunPnpmInstall()
    {
        string workflowsDir = Path.Combine(RepoRoot(), ".github", "workflows");
        var violations = Directory.EnumerateFiles(workflowsDir, "*.yml")
            .OrderBy(p => p, StringComparer.Ordinal)
            .SelectMany(path => FindJobsWithUnpopulatedPnpmCache(Path.GetFileName(path), File.ReadAllLines(path)))
            .ToList();

        violations.Should().BeEmpty(
            "actions/setup-node with 'cache: pnpm' fails its post-job saveCache step with a Path Validation Error whenever pnpm-lock.yaml changes and no step runs 'pnpm install' to create the pnpm store directory");

        string[] driftedWorkflow =
        [
            "jobs:",
            "  security:",
            "    name: Security and Vulnerability Audit",
            "    steps:",
            "      - name: Set up Node.js",
            "        uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7",
            "        with:",
            "          node-version: \"22\"",
            "          cache: \"pnpm\"",
            "      - name: Run dependency audit",
            "        run: node scripts/run-cli.js check vulnerabilities",
        ];

        FindJobsWithUnpopulatedPnpmCache("ci.yml", driftedWorkflow)
            .Should().Equal("ci.yml:security");
    }

    private static List<string> FindJobsWithUnpopulatedPnpmCache(string workflowFile, string[] lines)
    {
        var violations = new List<string>();
        foreach (var job in ParseWorkflow(lines))
        {
            if (!JobUsesSetupNodePnpmCache(lines, job.Key))
            {
                continue;
            }

            bool installsPackages = job.Steps.Any(
                s => s.Run?.Contains("pnpm install", StringComparison.Ordinal) is true);
            if (!installsPackages)
            {
                violations.Add($"{workflowFile}:{job.Key}");
            }
        }

        return violations;
    }

    private static bool JobUsesSetupNodePnpmCache(string[] lines, string jobKey)
    {
        foreach (string stepBlock in ExtractJobStepBlocks(lines, jobKey))
        {
            if (stepBlock.Contains("actions/setup-node", StringComparison.Ordinal) &&
                SetupNodePnpmCacheLine().IsMatch(stepBlock))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> ExtractJobStepBlocks(string[] lines, string jobKey)
    {
        string jobHeader = $"  {jobKey}:";
        int start = Array.FindIndex(lines, l => l.TrimEnd() == jobHeader);
        if (start < 0)
        {
            return [];
        }

        var blocks = new List<string>();
        var current = new List<string>();
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (TryMatch(line, @"^  ([A-Za-z0-9_-]+):\s*$", out _))
            {
                break;
            }

            if (line.StartsWith("      - ", StringComparison.Ordinal))
            {
                if (current.Count > 0)
                {
                    blocks.Add(string.Join('\n', current));
                    current.Clear();
                }

                current.Add(line);
            }
            else if (current.Count > 0)
            {
                current.Add(line);
            }
        }

        if (current.Count > 0)
        {
            blocks.Add(string.Join('\n', current));
        }

        return blocks;
    }

    [GeneratedRegex(@"(?m)^\s+cache:\s*[""']?pnpm[""']?\s*(?:#.*)?$")]
    private static partial Regex SetupNodePnpmCacheLine();
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

    private sealed record WorkflowStep(string Name, string? If, string? Run, string? Uses = null);

    private sealed record WorkflowJob(string Key, string Name, string? If, IReadOnlyList<string> Needs, List<WorkflowStep> Steps);

    /// <summary>
    /// Reads the parts of a GitHub Actions workflow this test needs (job keys, names, <c>if</c>, single-line or
    /// multiline <c>needs</c>, and each step's name, <c>if</c>, mapped <c>uses</c> and <c>run</c>), relying on the
    /// two-space indentation ci.yml uses.
    /// </summary>
    private static List<WorkflowJob> ParseWorkflow(string[] lines)
    {
        var jobs = new List<WorkflowJob>();
        bool inJobs = false;
        string? key = null, name = null, jobIf = null;
        IReadOnlyList<string> needs = [];
        List<WorkflowStep> steps = [];
        string? stepName = null, stepIf = null, stepRun = null, stepUses = null;
        bool inStep = false;

        void FlushStep()
        {
            if (inStep && stepName is not null) steps.Add(new WorkflowStep(stepName, stepIf, stepRun, stepUses));
            inStep = false;
            stepName = stepIf = stepRun = stepUses = null;
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
            else if (!inStep && TryMatch(line, @"^    name: (.+)$", out var jobName))
            {
                name = Unquote(jobName);
            }
            else if (!inStep && TryMatch(line, @"^    if: (.+)$", out var condition))
            {
                jobIf = ParseScalarOrFolded(condition, lines, ref i, "      ");
            }
            else if (!inStep && TryMatch(line, @"^    needs:\s*(.*)$", out var rawNeeds))
            {
                needs = ParseNeeds(rawNeeds, lines, ref i);
            }
            else if (TryMatch(line, @"^      - (.*)$", out var firstKey))
            {
                FlushStep();
                inStep = true;
                if (TryMatch(firstKey, @"^name: (.+)$", out var dashName))
                {
                    stepName = Unquote(dashName);
                }
                else if (TryMatch(firstKey, @"^uses:\s*([^\s@#]+)", out var dashAction))
                {
                    stepUses = dashAction;
                    if (UsesCommands.TryGetValue(dashAction, out var mappedRun)) stepRun = mappedRun;
                }
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
            else if (TryMatch(line, @"^        uses:\s*([^\s@#]+)", out var action))
            {
                stepUses = action;
                if (UsesCommands.TryGetValue(action, out var mappedRun)) stepRun = mappedRun;
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

    private static string ParseScalarOrFolded(string head, string[] lines, ref int index, string indent)
    {
        string trimmed = head.Trim();
        if (trimmed is not ">-" and not ">" and not "|" and not "|-")
        {
            return head;
        }

        var parts = new List<string>();
        while (index < lines.Length && (lines[index].Trim().Length == 0 || lines[index].StartsWith(indent, StringComparison.Ordinal)))
        {
            string part = lines[index++].Trim();
            if (part.Length > 0) parts.Add(part);
        }

        return string.Join(' ', parts);
    }

    private static IReadOnlyList<string> ParseNeeds(string rawNeeds, string[] lines, ref int index)
    {
        string value = StripInlineComment(rawNeeds).Trim();
        if (value.Length == 0 && index < lines.Length && lines[index].TrimStart().StartsWith('['))
        {
            value = StripInlineComment(lines[index++]).Trim();
        }

        if (value.StartsWith('['))
        {
            var buffer = new List<string> { value };
            while (!value.Contains(']') && index < lines.Length)
            {
                value = StripInlineComment(lines[index++]).Trim();
                buffer.Add(value);
            }

            string joined = string.Join(' ', buffer);
            int open = joined.IndexOf('[');
            int close = joined.LastIndexOf(']');
            string inner = open >= 0 && close > open ? joined[(open + 1)..close] : joined.Trim('[', ']');
            return [.. inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Unquote)];
        }

        if (value.Length == 0)
        {
            var items = new List<string>();
            while (index < lines.Length && TryMatch(lines[index], @"^      -\s*(.+)$", out var item))
            {
                items.Add(Unquote(StripInlineComment(item)));
                index++;
            }

            return items;
        }

        return [Unquote(value)];
    }

    private static string StripInlineComment(string line)
    {
        int hash = line.IndexOf('#');
        return hash >= 0 ? line[..hash] : line;
    }

    private static bool TryMatch(string input, string pattern, out string group)
    {
        var match = Regex.Match(input, pattern);
        group = match.Success ? match.Groups[1].Value : "";
        return match.Success;
    }

    private static string Unquote(string value) => value.Trim().Trim('"', '\'');
}
