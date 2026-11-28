using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;
using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

public static partial class RulesetService
{
    private static readonly string[] RequiredConflictCheckTypes =
    [
        "opened",
        "synchronize",
        "reopened",
        "edited",
    ];

    private static readonly HashSet<string> ExcludedPrOrchestrationJobKeys = new(StringComparer.Ordinal)
    {
        "classify",
    };

    private static readonly string[] PushOrDispatchEvents = ["push", "workflow_dispatch"];

    private static readonly string[] PullRequestEvents = ["pull_request"];

    [GeneratedRegex(@"\bgh\s+pr\s+merge\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GhPrMergeRegex();

    [GeneratedRegex(@"^(?:github\.event_name\s*(?<op>==|!=)\s*['""](?<val>[A-Za-z0-9_]+)['""]|['""](?<val2>[A-Za-z0-9_]+)['""]\s*(?<op>==|!=)\s*github\.event_name)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EventNameComparisonRegex();

    [GeneratedRegex(@"^needs\.classify\.outputs\.[A-Za-z0-9_-]+\s*==\s*['""]true['""]$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClassifyOutputTrueRegex();

    [GeneratedRegex(@"^github\.ref\s*==\s*['""](?<ref>[^'""]+)['""]$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitRefEqualsRegex();

    [GeneratedRegex(@"^startsWith\(\s*github\.ref\s*,\s*['""](?<prefix>[^'""]+)['""]\s*\)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitRefStartsWithRegex();

    private static List<WorkflowJobInfo> LoadAndValidateWorkflows(
        string repoRoot,
        GitIndexReader? reader,
        List<string> errors)
    {
        var allJobs = new List<WorkflowJobInfo>();
        string workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
        bool useIndex = reader is { HasAuthoritativeIndex: true };

        bool dirExists = useIndex
            ? reader!.DirectoryExists(".github/workflows")
            : Directory.Exists(workflowsDir);
        if (!dirExists)
        {
            errors.Add("Directory '.github/workflows' does not exist.");
            return allJobs;
        }

        bool ciExists = useIndex
            ? reader!.FileExists(".github/workflows/ci.yml")
            : File.Exists(Path.Combine(workflowsDir, "ci.yml"));
        if (!ciExists)
        {
            errors.Add("Required workflow '.github/workflows/ci.yml' is missing.");
        }

        string conflictRelPath = $".github/workflows/{ConflictCheckWorkflowFile}";
        bool conflictExists = useIndex
            ? reader!.FileExists(conflictRelPath)
            : File.Exists(Path.Combine(workflowsDir, ConflictCheckWorkflowFile));
        if (!conflictExists)
        {
            errors.Add($"Required workflow '{conflictRelPath}' is missing.");
        }

        if (useIndex)
        {
            string[] stagedWorkflowPaths = reader!.Paths
                .Where(IsWorkflowYamlPath)
                .Order(StringComparer.Ordinal)
                .ToArray();

            foreach (string relPath in stagedWorkflowPaths)
            {
                if (!reader.TryReadLines(relPath, out string[] lines))
                {
                    errors.Add($"{relPath}: could not read staged workflow blob.");
                    continue;
                }

                allJobs.AddRange(ParseWorkflowJobs(Path.GetFileName(relPath), lines));
            }

            return allJobs;
        }

        string[] workflowFiles = Directory.GetFiles(workflowsDir, "*.yml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(workflowsDir, "*.yaml", SearchOption.TopDirectoryOnly))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (string path in workflowFiles)
        {
            allJobs.AddRange(ParseWorkflowJobs(Path.GetFileName(path), File.ReadAllLines(path)));
        }

        return allJobs;
    }

    private static bool IsWorkflowYamlPath(string relativePath)
    {
        const string prefix = ".github/workflows/";
        if (!relativePath.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = relativePath.AsSpan(prefix.Length);
        if (remainder.IsEmpty || remainder.Contains('/'))
        {
            return false;
        }

        return remainder.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            || remainder.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateWorkflowParity(
        IReadOnlyList<RulesetDefinition> definitions,
        IReadOnlyList<WorkflowJobInfo> workflowJobs,
        List<string> errors)
    {
        var eligiblePrCheckNames = workflowJobs
            .Where(IsEligibleBranchStatusCheckJob)
            .Select(j => j.Name)
            .ToHashSet(StringComparer.Ordinal);

        var eligibleMergeGroupCheckNames = workflowJobs
            .Where(IsEligibleMergeGroupStatusCheckJob)
            .Select(j => j.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var def in definitions.Where(d => d.Target == "branch"))
        {
            string prefix = $".github/rulesets/{def.FileName}";
            foreach (string context in def.RequiredStatusChecks
                .Select(check => check.Context)
                .Where(ctx => !string.IsNullOrWhiteSpace(ctx)))
            {
                if (!eligiblePrCheckNames.Contains(context))
                {
                    errors.Add(
                        $"{prefix}: required status check '{context}' does not match any unconditional pull-request job in '.github/workflows/*.yml'.");
                }
                else if (!eligibleMergeGroupCheckNames.Contains(context))
                {
                    errors.Add(
                        $"{prefix}: required status check '{context}' does not run unconditionally on 'merge_group' events in '.github/workflows/*.yml'.");
                }
            }
        }
    }

    private static bool IsEligibleBranchStatusCheckJob(WorkflowJobInfo job)
    {
        if (!job.OnPullRequest || job.Uses is not null || job.HasMatrix)
        {
            return false;
        }

        if (IsPushOnlyJob(job) || ExcludedPrOrchestrationJobKeys.Contains(job.Key))
        {
            return false;
        }

        if (job.IfCondition is not null
            && job.IfCondition.Contains("needs.", StringComparison.Ordinal)
            && !IsAlwaysOnlyCondition(job.IfCondition))
        {
            return false;
        }

        return true;
    }

    private static bool IsEligibleMergeGroupStatusCheckJob(WorkflowJobInfo job) =>
        job.OnMergeGroup && IsEligibleBranchStatusCheckJob(job) && !IsPullRequestOnlyJob(job);

    private static void ValidateCiGateCoverage(IReadOnlyList<WorkflowJobInfo> workflowJobs, List<string> errors)
    {
        var ciJobs = workflowJobs.Where(j => j.WorkflowFile == "ci.yml").ToList();
        if (ciJobs.Count == 0)
        {
            return;
        }

        var ciGate = ciJobs.FirstOrDefault(j => j.Key == CiGateJobKey);
        if (ciGate is null)
        {
            errors.Add($".github/workflows/ci.yml: missing '{CiGateJobKey}' job ('name: {CiGateCheckName}').");
            return;
        }

        if (ciGate.Name != CiGateCheckName)
        {
            errors.Add($".github/workflows/ci.yml: job '{CiGateJobKey}' must have 'name: {CiGateCheckName}' (found '{ciGate.Name}').");
        }

        if (!IsAlwaysOnlyCondition(ciGate.IfCondition))
        {
            errors.Add($".github/workflows/ci.yml: job '{CiGateJobKey}' must set 'if: always()' so failed or skipped upstream jobs fail the gate.");
        }

        if (ciGate.PullRequestTrigger.HasExplicitTypes && !ciGate.PullRequestTrigger.Types.Contains("edited"))
        {
            errors.Add(".github/workflows/ci.yml: 'on.pull_request.types' must include 'edited' when explicit activity types are configured.");
        }

        var expectedValidationJobs = ciJobs
            .Where(j => j.Key != CiGateJobKey && !IsPushOnlyJob(j) && !ExcludedPrOrchestrationJobKeys.Contains(j.Key))
            .ToList();

        var expectedKeys = expectedValidationJobs.Select(j => j.Key).ToHashSet(StringComparer.Ordinal);
        var actualNeeds = ciGate.Needs.ToHashSet(StringComparer.Ordinal);

        foreach (string missing in expectedKeys.Except(actualNeeds, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            errors.Add($".github/workflows/ci.yml: '{CiGateJobKey}.needs' is missing pull-request validation job '{missing}'.");
        }

        foreach (string unexpected in actualNeeds.Except(expectedKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            errors.Add($".github/workflows/ci.yml: '{CiGateJobKey}.needs' includes non-validation or unknown job '{unexpected}'.");
        }

        ValidateCiGateResultEvaluations(ciGate, expectedValidationJobs, errors);
        ValidateMergeGroupClassifyParity(ciGate, ciJobs, expectedValidationJobs, errors);
    }

    private static void ValidateCiGateResultEvaluations(
        WorkflowJobInfo ciGate,
        IReadOnlyList<WorkflowJobInfo> validationJobs,
        List<string> errors)
    {
        if (!ciGate.StepRun.Contains("exit 1", StringComparison.Ordinal))
        {
            errors.Add($".github/workflows/ci.yml: '{CiGateJobKey}' step script must exit non-zero ('exit 1') when validation jobs do not pass.");
        }

        foreach (var job in validationJobs)
        {
            string resultExpr = $"needs.{job.Key}.result";
            var matchingEnv = ciGate.StepEnv
                .Where(kv => kv.Value.Contains(resultExpr, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToList();

            bool checkedDirectly = ciGate.StepRun.Contains(resultExpr, StringComparison.Ordinal);
            bool checkedViaEnv = matchingEnv.Any(envVar => ciGate.StepRun.Contains($"${envVar}", StringComparison.Ordinal)
                || ciGate.StepRun.Contains($"${{{envVar}}}", StringComparison.Ordinal));

            if (!checkedDirectly && !checkedViaEnv)
            {
                errors.Add($".github/workflows/ci.yml: '{CiGateJobKey}' does not evaluate '{resultExpr}' in its step script.");
                continue;
            }

            bool isPathConditional = job.IfCondition?.Contains("needs.classify.outputs.", StringComparison.Ordinal) is true;
            if (!isPathConditional && matchingEnv.Any(envVar => AllowsSkippedForEnvVar(ciGate.StepRun, envVar)))
            {
                errors.Add($".github/workflows/ci.yml: '{CiGateJobKey}' must fail when required job '{job.Key}' is skipped.");
            }
            else if (isPathConditional && ciGate.OnMergeGroup && !HasPullRequestAndMergeGroupOnlySkipAllowance(ciGate.StepRun, matchingEnv))
            {
                errors.Add($".github/workflows/ci.yml: '{CiGateJobKey}' must allow 'skipped' for conditional job '{job.Key}' on both 'pull_request' and 'merge_group' while failing skipped 'push'/'workflow_dispatch' runs.");
            }
        }
    }

    private static void ValidateMergeGroupClassifyParity(
        WorkflowJobInfo ciGate,
        IReadOnlyList<WorkflowJobInfo> ciJobs,
        IReadOnlyList<WorkflowJobInfo> validationJobs,
        List<string> errors)
    {
        if (!ciGate.OnMergeGroup)
        {
            return;
        }

        var classifyConditionalJobs = validationJobs
            .Where(j => j.IfCondition?.Contains("needs.classify.outputs.", StringComparison.Ordinal) is true)
            .ToList();
        if (classifyConditionalJobs.Count == 0)
        {
            return;
        }

        var classify = ciJobs.FirstOrDefault(j => j.Key == "classify");
        if (classify is null)
        {
            errors.Add(".github/workflows/ci.yml: missing 'classify' job required by path-conditional validation jobs.");
            return;
        }

        if (!EvaluatesJobIf(classify.IfCondition, "pull_request", classifyOutputTrue: false, "refs/heads/main") ||
            !EvaluatesJobIf(classify.IfCondition, "merge_group", classifyOutputTrue: false, "refs/heads/main"))
        {
            errors.Add(".github/workflows/ci.yml: job 'classify' must run on both 'pull_request' and 'merge_group' events.");
        }

        foreach (var job in classifyConditionalJobs)
        {
            bool runsWhenMergeGroupFalse = EvaluatesJobIf(job.IfCondition, "merge_group", classifyOutputTrue: false, "refs/heads/main");
            bool runsWhenMergeGroupTrue = EvaluatesJobIf(job.IfCondition, "merge_group", classifyOutputTrue: true, "refs/heads/main");
            if (runsWhenMergeGroupFalse || !runsWhenMergeGroupTrue)
            {
                errors.Add($".github/workflows/ci.yml: job '{job.Key}' must trigger on 'merge_group' only when 'needs.classify.outputs.*' is 'true'.");
            }

            if (!EvaluatesJobIf(job.IfCondition, "push", classifyOutputTrue: false, "refs/heads/main"))
            {
                errors.Add($".github/workflows/ci.yml: job '{job.Key}' must keep 'push' runs unconditional.");
            }

            if (job.IfCondition!.Contains("workflow_dispatch", StringComparison.Ordinal) &&
                (!EvaluatesJobIf(job.IfCondition, "workflow_dispatch", classifyOutputTrue: false, "refs/heads/main") ||
                 !EvaluatesJobIf(job.IfCondition, "workflow_dispatch", classifyOutputTrue: false, "refs/heads/release/1.0") ||
                 !EvaluatesJobIf(job.IfCondition, "workflow_dispatch", classifyOutputTrue: false, "refs/tags/v1.0.0") ||
                 EvaluatesJobIf(job.IfCondition, "workflow_dispatch", classifyOutputTrue: false, "refs/heads/feature/unprotected")))
            {
                errors.Add($".github/workflows/ci.yml: job '{job.Key}' must keep protected 'workflow_dispatch' refs ('main', 'release/*', 'v*') unconditional while rejecting unprotected refs.");
            }
        }
    }

    private static bool HasPullRequestAndMergeGroupOnlySkipAllowance(string stepRun, IReadOnlyList<string> envVars)
    {
        if (envVars.Count == 0 || !envVars.Any(envVar => AllowsSkippedForEnvVar(stepRun, envVar)))
        {
            return false;
        }

        int guardDepth = 0;
        bool sawNonConditionalFailureBranch = false;
        foreach (string rawLine in stepRun.Split('\n'))
        {
            string line = rawLine.Trim();
            if (guardDepth == 0)
            {
                if (IsPullRequestAndMergeGroupShellIf(line) && !EndsWithInlineFi(line))
                {
                    guardDepth = 1;
                    continue;
                }
            }
            else
            {
                UpdateShellGuardDepth(line, ref guardDepth, ref sawNonConditionalFailureBranch);
            }

            if (envVars.Any(envVar => AllowsSkippedOnLine(line, envVar)) && guardDepth == 0)
            {
                return false;
            }
        }

        return sawNonConditionalFailureBranch;
    }

    private static void UpdateShellGuardDepth(string line, ref int guardDepth, ref bool sawNonConditionalFailureBranch)
    {
        if (guardDepth == 1 && (line.StartsWith("elif ", StringComparison.Ordinal) || line == "else"))
        {
            guardDepth = 0;
            sawNonConditionalFailureBranch = true;
            return;
        }

        if (line.StartsWith("if ", StringComparison.Ordinal) && !EndsWithInlineFi(line))
        {
            guardDepth++;
        }
        else if (line == "fi" || line.StartsWith("fi;", StringComparison.Ordinal))
        {
            guardDepth--;
        }
    }

    private static bool IsPullRequestAndMergeGroupShellIf(string trimmedLine) =>
        trimmedLine.StartsWith("if ", StringComparison.Ordinal)
        && trimmedLine.Contains("EVENT_NAME", StringComparison.Ordinal)
        && trimmedLine.Contains("pull_request", StringComparison.Ordinal)
        && trimmedLine.Contains("merge_group", StringComparison.Ordinal);

    private static bool EndsWithInlineFi(string trimmedLine) =>
        trimmedLine.EndsWith("; fi", StringComparison.Ordinal)
        || trimmedLine.EndsWith("\tfi", StringComparison.Ordinal);

    private static bool AllowsSkippedForEnvVar(string stepRun, string envVar) =>
        stepRun.Split('\n').Any(line => AllowsSkippedOnLine(line, envVar));

    private static bool AllowsSkippedOnLine(string line, string envVar) =>
        (line.Contains($"${envVar}", StringComparison.Ordinal) || line.Contains($"${{{envVar}}}", StringComparison.Ordinal))
        && line.Contains("skipped", StringComparison.Ordinal);

    private static void ValidateConflictCheckAndMergePolicy(IReadOnlyList<WorkflowJobInfo> workflowJobs, List<string> errors)
    {
        foreach (var job in workflowJobs)
        {
            if (string.Equals(job.Key, "automerge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(job.Name, "Enable Automerge", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($".github/workflows/{job.WorkflowFile}: job '{job.Key}' is retired; use maintainer-attributed native GitHub auto-merge instead of workflow automerge jobs.");
            }

            if (GhPrMergeRegex().IsMatch(job.StepRun))
            {
                errors.Add($".github/workflows/{job.WorkflowFile}: job '{job.Key}' invokes 'gh pr merge'; GITHUB_TOKEN merges are forbidden because they suppress downstream 'push' workflows.");
            }
        }

        var conflictJobs = workflowJobs.Where(j => j.WorkflowFile == ConflictCheckWorkflowFile).ToList();
        if (conflictJobs.Count == 0)
        {
            return;
        }

        var conflictJob = conflictJobs.FirstOrDefault(j => j.Key == ConflictCheckJobKey)
            ?? conflictJobs.FirstOrDefault(j => j.Name == ConflictCheckName);
        if (conflictJob is null)
        {
            errors.Add($".github/workflows/{ConflictCheckWorkflowFile}: missing '{ConflictCheckJobKey}' job ('name: {ConflictCheckName}').");
            return;
        }

        if (conflictJob.Name != ConflictCheckName)
        {
            errors.Add($".github/workflows/{ConflictCheckWorkflowFile}: job '{conflictJob.Key}' must have 'name: {ConflictCheckName}' (found '{conflictJob.Name}').");
        }

        if (!conflictJob.OnPullRequest || !conflictJob.PullRequestTrigger.HasExplicitTypes)
        {
            errors.Add($".github/workflows/{ConflictCheckWorkflowFile}: 'on.pull_request.types' must explicitly include 'opened', 'synchronize', 'reopened', and 'edited' so base-branch edits re-run '{ConflictCheckName}'.");
        }
        else
        {
            foreach (string requiredType in RequiredConflictCheckTypes)
            {
                if (!conflictJob.PullRequestTrigger.Types.Contains(requiredType))
                {
                    errors.Add($".github/workflows/{ConflictCheckWorkflowFile}: 'on.pull_request.types' is missing required activity type '{requiredType}'.");
                }
            }
        }

        if (!conflictJob.StepRun.Contains("git merge-tree --write-tree", StringComparison.Ordinal) ||
            !conflictJob.StepRun.Contains("refs/pull/", StringComparison.Ordinal) ||
            !conflictJob.StepRun.Contains("exit 1", StringComparison.Ordinal))
        {
            errors.Add($".github/workflows/{ConflictCheckWorkflowFile}: '{conflictJob.Key}' must fetch '$GITHUB_REPOSITORY' refs ('refs/pull/<number>/head') and fail closed ('exit 1') using 'git merge-tree --write-tree'.");
        }

        if (conflictJob.StepRun.Contains("gh pr view", StringComparison.Ordinal) ||
            conflictJob.StepRun.Contains("UNKNOWN", StringComparison.Ordinal))
        {
            errors.Add($".github/workflows/{ConflictCheckWorkflowFile}: '{conflictJob.Key}' must not poll 'gh pr view' or mask 'UNKNOWN' mergeability states.");
        }
    }

    private static bool IsPushOnlyJob(WorkflowJobInfo job) =>
        job.IfCondition is not null && RequiresPushEvent(job.IfCondition);

    private static bool IsPullRequestOnlyJob(WorkflowJobInfo job) =>
        job.IfCondition is not null && RequiresAnyEvent(job.IfCondition, PullRequestEvents);

    /// <summary>True when a GitHub Actions <c>if:</c> expression restricts execution to <c>push</c> or <c>workflow_dispatch</c> events.</summary>
    public static bool RequiresPushEvent(string expression) =>
        RequiresAnyEvent(expression, PushOrDispatchEvents);

    private static bool RequiresAnyEvent(string expression, IReadOnlyList<string> eventNames)
    {
        string trimmed = WorkflowExpressionPrimitives.UnwrapExpression(expression);
        if (trimmed.Length == 0)
        {
            return false;
        }

        var orClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(trimmed, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(clause => RequiresAnyEvent(clause, eventNames));
        }

        var andClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(trimmed, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(clause => RequiresAnyEvent(clause, eventNames));
        }

        string normalized = WorkflowExpressionPrimitives.CollapseWhitespace(trimmed);
        return eventNames.Any(eventName =>
            normalized == $"github.event_name == '{eventName}'"
            || normalized == $"github.event_name == \"{eventName}\""
            || normalized == $"'{eventName}' == github.event_name"
            || normalized == $"\"{eventName}\" == github.event_name");
    }

    private static bool EvaluatesJobIf(string? expression, string eventName, bool classifyOutputTrue, string gitRef)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return true;
        }

        string trimmed = WorkflowExpressionPrimitives.UnwrapExpression(expression);
        if (trimmed.Length == 0)
        {
            return true;
        }

        var orClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(trimmed, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.Any(clause => EvaluatesJobIf(clause, eventName, classifyOutputTrue, gitRef));
        }

        var andClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(trimmed, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.All(clause => EvaluatesJobIf(clause, eventName, classifyOutputTrue, gitRef));
        }

        if (trimmed.StartsWith('!'))
        {
            return !EvaluatesJobIf(trimmed[1..], eventName, classifyOutputTrue, gitRef);
        }

        return EvaluateAtomicIfClause(WorkflowExpressionPrimitives.CollapseWhitespace(trimmed), eventName, classifyOutputTrue, gitRef);
    }

    private static bool EvaluateAtomicIfClause(string normalized, string eventName, bool classifyOutputTrue, string gitRef)
    {
        if (normalized is "always()" or "success()" or "true")
        {
            return true;
        }

        if (normalized is "cancelled()" or "failure()" or "false")
        {
            return false;
        }

        var eventEq = EventNameComparisonRegex().Match(normalized);
        if (eventEq.Success)
        {
            string op = eventEq.Groups["op"].Value;
            string target = eventEq.Groups["val"].Success ? eventEq.Groups["val"].Value : eventEq.Groups["val2"].Value;
            bool matches = string.Equals(eventName, target, StringComparison.Ordinal);
            return op == "==" ? matches : !matches;
        }

        if (ClassifyOutputTrueRegex().IsMatch(normalized))
        {
            return classifyOutputTrue;
        }

        var refEq = GitRefEqualsRegex().Match(normalized);
        if (refEq.Success)
        {
            return string.Equals(gitRef, refEq.Groups["ref"].Value, StringComparison.Ordinal);
        }

        var refPrefix = GitRefStartsWithRegex().Match(normalized);
        if (refPrefix.Success)
        {
            return gitRef.StartsWith(refPrefix.Groups["prefix"].Value, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsAlwaysOnlyCondition(string? condition) =>
        condition is not null && WorkflowExpressionPrimitives.CollapseWhitespace(condition) is "always()" or "${{ always() }}";
}
