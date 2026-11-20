using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates GitHub Actions workflows (<c>.github/workflows/*.{yml,yaml}</c>) and composite or container actions
/// (<c>.github/actions/**/action.{yml,yaml}</c>) for 40-character commit SHA pinning, inline version comments,
/// <c>sha256:</c> digests on <c>docker://</c> references, <c>persist-credentials: false</c> on <c>actions/checkout</c>,
/// least-privilege workflow token permissions, and bot actor gating on <c>issue_comment</c> mutation jobs.
/// </summary>
public static partial class WorkflowSecurityValidatorService
{
    public static int ValidateWorkflowSecurity(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating GitHub Actions workflow security, SHA pinning, and token permissions...");

        var targets = ResolveTargetFiles(files, repoRoot, out bool explicitFilterEmpty);
        if (explicitFilterEmpty)
        {
            Console.WriteLine("✔ No workflow or composite action files in target list.");
            return 0;
        }

        if (targets.Count == 0)
        {
            Console.WriteLine($"[ERROR] No GitHub workflow or action files found under '{Path.Combine(repoRoot, ".github")}'.");
            return 1;
        }

        var errors = new List<string>();
        foreach (var (fullPath, relativePath, isWorkflow) in targets)
        {
            ValidateFile(fullPath, relativePath, isWorkflow, errors);
        }

        if (errors.Count > 0)
        {
            foreach (string error in errors)
            {
                Console.WriteLine($"[WORKFLOW SECURITY] {error}");
            }

            Console.WriteLine($"\n[ERROR] Found {errors.Count} workflow security violation(s).");
            return 1;
        }

        Console.WriteLine($"✔ Validated {targets.Count} workflow and action file(s) cleanly.");
        return 0;
    }

    private static List<(string FullPath, string RelativePath, bool IsWorkflow)> ResolveTargetFiles(
        string[]? files,
        string repoRoot,
        out bool explicitFilterEmpty)
    {
        explicitFilterEmpty = false;
        var results = new List<(string FullPath, string RelativePath, bool IsWorkflow)>();

        if (files is { Length: > 0 })
        {
            foreach (string raw in files)
            {
                string fullPath = Path.IsPathRooted(raw) ? raw : Path.Combine(repoRoot, raw);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
                if (IsWorkflowPath(rel))
                {
                    results.Add((fullPath, rel, true));
                }
                else if (IsCompositeActionPath(rel))
                {
                    results.Add((fullPath, rel, false));
                }
            }

            results.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
            explicitFilterEmpty = results.Count == 0;
            return results;
        }

        string workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
        if (Directory.Exists(workflowsDir))
        {
            foreach (string file in Directory.EnumerateFiles(workflowsDir))
            {
                string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (IsWorkflowPath(rel))
                {
                    results.Add((file, rel, true));
                }
            }
        }

        string actionsDir = Path.Combine(repoRoot, ".github", "actions");
        if (Directory.Exists(actionsDir))
        {
            foreach (string file in Directory.EnumerateFiles(actionsDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (IsCompositeActionPath(rel))
                {
                    results.Add((file, rel, false));
                }
            }
        }

        results.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
        return results;
    }

    private static bool IsWorkflowPath(string relativePath)
    {
        if (!relativePath.StartsWith(".github/workflows/", StringComparison.Ordinal))
        {
            return false;
        }

        string remainder = relativePath[".github/workflows/".Length..];
        return !remainder.Contains('/', StringComparison.Ordinal) && IsYamlExtension(remainder);
    }

    private static bool IsCompositeActionPath(string relativePath)
    {
        if (!relativePath.StartsWith(".github/actions/", StringComparison.Ordinal))
        {
            return false;
        }

        string fileName = Path.GetFileName(relativePath);
        return string.Equals(fileName, "action.yml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "action.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYamlExtension(string fileName) =>
        fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    private static void ValidateFile(string fullPath, string relativePath, bool isWorkflow, List<string> errors)
    {
        string[] lines = File.ReadAllLines(fullPath);
        if (isWorkflow)
        {
            var workflow = ParseWorkflowFile(lines);
            ValidateWorkflowPermissionsAndTriggers(relativePath, workflow, errors);
            foreach (var uses in workflow.JobUses)
            {
                ValidateUsesReference(relativePath, uses, errors);
            }

            foreach (var step in workflow.Steps)
            {
                ValidateStep(relativePath, step, errors);
            }
        }
        else
        {
            var action = ParseActionFile(lines);
            if (action.DockerImage is { } dockerImage)
            {
                ValidateDockerReference(relativePath, dockerImage.LineNumber, dockerImage.Value, errors);
            }

            foreach (var step in action.Steps)
            {
                ValidateStep(relativePath, step, errors);
            }
        }
    }

    private static void ValidateWorkflowPermissionsAndTriggers(
        string relativePath,
        ParsedWorkflow workflow,
        List<string> errors)
    {
        if (!workflow.HasTopLevelPermissions)
        {
            errors.Add($"{relativePath}:1: Workflow is missing a top-level 'permissions:' block; declare read-only default permissions (e.g. 'contents: read').");
        }
        else
        {
            ValidateTopLevelPermissions(relativePath, workflow, errors);
        }

        bool triggersPullRequest = workflow.Triggers.Contains("pull_request") ||
                                   workflow.Triggers.Contains("pull_request_target");
        bool triggersIssueComment = workflow.Triggers.Contains("issue_comment");

        foreach (var job in workflow.Jobs)
        {
            ValidateJobPermissions(relativePath, job, triggersPullRequest, triggersIssueComment, errors);
        }
    }

    private static void ValidateTopLevelPermissions(
        string relativePath,
        ParsedWorkflow workflow,
        List<string> errors)
    {
        if (workflow.TopLevelPermissionsScalar is { Length: > 0 } scalar)
        {
            if (string.Equals(scalar, "write-all", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{relativePath}:{workflow.TopLevelPermissionsLine}: Top-level 'permissions: write-all' is forbidden; scope write permissions to individual jobs.");
            }
            else if (!string.Equals(scalar, "read-all", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(scalar, "{}", StringComparison.Ordinal))
            {
                errors.Add($"{relativePath}:{workflow.TopLevelPermissionsLine}: Invalid top-level permissions value '{scalar}'.");
            }
        }

        foreach (var perm in workflow.TopLevelPermissions.Where(p =>
                     string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{relativePath}:{perm.LineNumber}: Top-level write permission '{perm.Scope}: write' is unscoped; keep workflow default permissions read-only and grant write access only on the specific job that needs it.");
        }
    }

    private static void ValidateJobPermissions(
        string relativePath,
        ParsedJob job,
        bool triggersPullRequest,
        bool triggersIssueComment,
        List<string> errors)
    {
        if (string.Equals(job.PermissionsScalar, "write-all", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{relativePath}:{job.PermissionsLine}: Job '{job.Key}' sets 'permissions: write-all'; grant only explicit least-privilege scopes.");
            return;
        }

        bool hasAnyWrite = job.Permissions.Any(p => string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase));
        if (!hasAnyWrite)
        {
            return;
        }

        string jobIf = job.IfExpression;
        bool restrictedToPushOrDispatch = IsRestrictedToPushOrDispatchOnly(jobIf);

        if ((triggersPullRequest || triggersIssueComment) && !restrictedToPushOrDispatch)
        {
            foreach (var perm in job.Permissions.Where(p =>
                         string.Equals(p.Scope, "contents", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"{relativePath}:{perm.LineNumber}: Job '{job.Key}' grants 'contents: write' on a pull_request or issue_comment workflow.");
            }
        }

        if (triggersIssueComment && !restrictedToPushOrDispatch)
        {
            foreach (var perm in job.Permissions.Where(p =>
                         string.Equals(p.Scope, "pull-requests", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"{relativePath}:{perm.LineNumber}: Job '{job.Key}' grants 'pull-requests: write' on an issue_comment workflow; restrict comment updates to 'issues: write'.");
            }

            if (!HasApprovedBotActorGate(jobIf))
            {
                errors.Add($"{relativePath}:{job.LineNumber}: Job '{job.Key}' grants write permissions on 'issue_comment' without gating on 'github.event.comment.user.type == 'Bot'' and an explicit allowed bot login.");
            }
        }
    }

    private static bool IsRestrictedToPushOrDispatchOnly(string jobIf)
    {
        if (string.IsNullOrWhiteSpace(jobIf) ||
            jobIf.Contains("pull_request", StringComparison.Ordinal) ||
            jobIf.Contains("issue_comment", StringComparison.Ordinal))
        {
            return false;
        }

        return jobIf.Contains("github.event_name == 'push'", StringComparison.Ordinal) ||
               jobIf.Contains("github.event_name == \"push\"", StringComparison.Ordinal) ||
               jobIf.Contains("github.event_name == 'workflow_dispatch'", StringComparison.Ordinal);
    }

    private static bool HasApprovedBotActorGate(string jobIf)
    {
        if (string.IsNullOrWhiteSpace(jobIf))
        {
            return false;
        }

        bool checksBotType = jobIf.Contains("user.type == 'Bot'", StringComparison.Ordinal) ||
                             jobIf.Contains("user.type == \"Bot\"", StringComparison.Ordinal);
        bool checksAllowedLogin = BotLoginAllowlistRegex().IsMatch(jobIf);
        return checksBotType && checksAllowedLogin;
    }

    private static void ValidateStep(string relativePath, ParsedStep step, List<string> errors)
    {
        if (step.Uses is not { } uses)
        {
            return;
        }

        string? actionName = ValidateUsesReference(relativePath, uses, errors);
        if (actionName is not null &&
            IsCheckoutAction(actionName) &&
            !string.Equals(step.PersistCredentials, "false", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{relativePath}:{uses.LineNumber}: Step using '{actionName}' must set 'persist-credentials: false' under 'with:'.");
        }
    }

    private static bool IsCheckoutAction(string actionName) =>
        string.Equals(actionName, "actions/checkout", StringComparison.OrdinalIgnoreCase) ||
        actionName.StartsWith("actions/checkout/", StringComparison.OrdinalIgnoreCase);

    private static string? ValidateUsesReference(string relativePath, UsesEntry uses, List<string> errors)
    {
        ExtractUsesRefAndComment(uses.RawValue, out string actionRef, out string comment);
        if (string.IsNullOrEmpty(actionRef))
        {
            errors.Add($"{relativePath}:{uses.LineNumber}: Empty 'uses:' reference.");
            return null;
        }

        // Only './' and '../' relative paths are local; never skip arbitrary dot-prefixed external references.
        if (actionRef.StartsWith("./", StringComparison.Ordinal) || actionRef.StartsWith("../", StringComparison.Ordinal))
        {
            if (actionRef.Contains('@', StringComparison.Ordinal))
            {
                errors.Add($"{relativePath}:{uses.LineNumber}: Local path reference '{actionRef}' must not include '@<ref>'.");
            }

            return null;
        }

        if (actionRef.StartsWith("docker://", StringComparison.OrdinalIgnoreCase))
        {
            ValidateDockerReference(relativePath, uses.LineNumber, actionRef, errors);
            return null;
        }

        if (actionRef.StartsWith('.'))
        {
            errors.Add($"{relativePath}:{uses.LineNumber}: Dot-prefixed external action '{actionRef}' is forbidden; local actions must start with './' or '../'.");
            return null;
        }

        int atIndex = actionRef.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0 || actionRef.IndexOf('@', atIndex + 1) >= 0)
        {
            errors.Add($"{relativePath}:{uses.LineNumber}: Action reference '{actionRef}' is missing '@<40-char-commit-sha>'.");
            return null;
        }

        string actionName = actionRef[..atIndex];
        string refValue = actionRef[(atIndex + 1)..];

        if (!ValidActionSlugRegex().IsMatch(actionName))
        {
            errors.Add($"{relativePath}:{uses.LineNumber}: Action reference '{actionName}' is not a valid 'owner/repo' slug.");
            return actionName;
        }

        if (!CommitShaRegex().IsMatch(refValue))
        {
            errors.Add($"{relativePath}:{uses.LineNumber}: Action '{actionName}' is pinned to '{refValue}' instead of a full 40-character hexadecimal commit SHA.");
            return actionName;
        }

        if (string.IsNullOrWhiteSpace(comment))
        {
            errors.Add($"{relativePath}:{uses.LineNumber}: Action '{actionName}' pinned to '{refValue}' must include an inline version comment (e.g. '# v4').");
        }

        return actionName;
    }

    private static void ValidateDockerReference(string relativePath, int lineNumber, string dockerRef, List<string> errors)
    {
        if (!DockerSha256DigestRegex().IsMatch(dockerRef))
        {
            errors.Add($"{relativePath}:{lineNumber}: Container reference '{dockerRef}' must be pinned to an immutable 'sha256:<64-hex>' digest (e.g. 'docker://image:tag@sha256:...').");
        }
    }

    private static void ExtractUsesRefAndComment(string rawValue, out string actionRef, out string comment)
    {
        string trimmed = rawValue.Trim();
        comment = string.Empty;

        if (trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\''))
        {
            char quote = trimmed[0];
            int closeIdx = trimmed.IndexOf(quote, 1);
            if (closeIdx > 0)
            {
                actionRef = trimmed[1..closeIdx].Trim();
                string rest = trimmed[(closeIdx + 1)..].Trim();
                if (rest.StartsWith('#'))
                {
                    comment = rest[1..].Trim();
                }

                return;
            }
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '#' && (i == 0 || char.IsWhiteSpace(trimmed[i - 1])))
            {
                actionRef = trimmed[..i].Trim();
                comment = trimmed[(i + 1)..].Trim();
                return;
            }
        }

        actionRef = trimmed;
    }

    private static ParsedWorkflow ParseWorkflowFile(string[] lines)
    {
        var workflow = new ParsedWorkflow();
        TopSection topSection = TopSection.None;
        JobSection jobSection = JobSection.None;
        ParsedJob? currentJob = null;
        var stepTracker = new StepTracker();

        int onChildIndent = -1;
        int jobsChildIndent = -1;
        int jobPropIndent = -1;
        int blockParentIndent = -1;
        bool captureBlockIntoJobIf = false;

        for (int idx = 0; idx < lines.Length; idx++)
        {
            int lineNumber = idx + 1;
            string rawLine = lines[idx];
            string trimmed = rawLine.Trim();
            int indent = CountLeadingSpaces(rawLine);

            if (blockParentIndent >= 0)
            {
                if (trimmed.Length == 0 || indent > blockParentIndent)
                {
                    if (captureBlockIntoJobIf && currentJob is not null && trimmed.Length > 0 && !trimmed.StartsWith('#'))
                    {
                        currentJob.AppendIf(StripInlineComment(trimmed));
                    }

                    continue;
                }

                blockParentIndent = -1;
                captureBlockIntoJobIf = false;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (indent == 0 && !trimmed.StartsWith('-'))
            {
                stepTracker.Flush(workflow.Steps);
                currentJob = null;
                jobSection = JobSection.None;
                topSection = ParseTopLevelLine(trimmed, lineNumber, workflow, ref blockParentIndent);
                continue;
            }

            switch (topSection)
            {
                case TopSection.On:
                    ParseOnLine(trimmed, indent, ref onChildIndent, workflow.Triggers);
                    break;

                case TopSection.Permissions:
                    ParsePermissionMappingLine(trimmed, lineNumber, workflow.TopLevelPermissions);
                    break;

                case TopSection.Jobs:
                    ParseJobsSectionLine(
                        trimmed,
                        indent,
                        lineNumber,
                        workflow,
                        stepTracker,
                        ref jobsChildIndent,
                        ref jobPropIndent,
                        ref currentJob,
                        ref jobSection,
                        ref blockParentIndent,
                        ref captureBlockIntoJobIf);
                    break;
            }
        }

        stepTracker.Flush(workflow.Steps);
        return workflow;
    }

    private static TopSection ParseTopLevelLine(
        string trimmed,
        int lineNumber,
        ParsedWorkflow workflow,
        ref int blockParentIndent)
    {
        if (!TrySplitKeyValue(trimmed, out string key, out string rawVal))
        {
            return TopSection.None;
        }

        string cleanVal = StripInlineComment(rawVal);
        if (IsBlockScalarHeader(cleanVal))
        {
            blockParentIndent = 0;
            return TopSection.None;
        }

        if (string.Equals(key, "on", StringComparison.Ordinal))
        {
            if (cleanVal.Length > 0)
            {
                ExtractInlineTriggers(cleanVal, workflow.Triggers);
                return TopSection.None;
            }

            return TopSection.On;
        }

        if (string.Equals(key, "permissions", StringComparison.Ordinal))
        {
            workflow.HasTopLevelPermissions = true;
            workflow.TopLevelPermissionsLine = lineNumber;
            if (cleanVal.Length > 0)
            {
                if (cleanVal.StartsWith('{') && cleanVal.EndsWith('}') && cleanVal.Length > 2)
                {
                    ParseFlowPermissions(cleanVal, lineNumber, workflow.TopLevelPermissions);
                }
                else
                {
                    workflow.TopLevelPermissionsScalar = Unquote(cleanVal);
                }

                return TopSection.None;
            }

            return TopSection.Permissions;
        }

        return string.Equals(key, "jobs", StringComparison.Ordinal) ? TopSection.Jobs : TopSection.None;
    }

    private static void ParseOnLine(string trimmed, int indent, ref int onChildIndent, HashSet<string> triggers)
    {
        if (trimmed.StartsWith('-'))
        {
            string item = Unquote(StripInlineComment(trimmed[1..].Trim()));
            if (item.Length > 0)
            {
                triggers.Add(item);
            }

            return;
        }

        if (onChildIndent < 0)
        {
            onChildIndent = indent;
        }

        if (indent == onChildIndent && TrySplitKeyValue(trimmed, out string eventName, out _))
        {
            triggers.Add(eventName);
        }
    }

    private static void ParseJobsSectionLine(
        string trimmed,
        int indent,
        int lineNumber,
        ParsedWorkflow workflow,
        StepTracker stepTracker,
        ref int jobsChildIndent,
        ref int jobPropIndent,
        ref ParsedJob? currentJob,
        ref JobSection jobSection,
        ref int blockParentIndent,
        ref bool captureBlockIntoJobIf)
    {
        if (!trimmed.StartsWith('-') && (jobsChildIndent < 0 || indent == jobsChildIndent))
        {
            if (TrySplitKeyValue(trimmed, out string jobKey, out _))
            {
                jobsChildIndent = indent;
                jobPropIndent = -1;
                stepTracker.Flush(workflow.Steps);
                currentJob = new ParsedJob(jobKey, lineNumber);
                workflow.Jobs.Add(currentJob);
                jobSection = JobSection.None;
            }

            return;
        }

        if (currentJob is null)
        {
            return;
        }

        if (!trimmed.StartsWith('-') && indent > jobsChildIndent && (jobPropIndent < 0 || indent == jobPropIndent))
        {
            jobPropIndent = indent;
            stepTracker.Flush(workflow.Steps);
            jobSection = ParseJobPropertyLine(
                trimmed,
                indent,
                lineNumber,
                currentJob,
                workflow,
                stepTracker,
                ref blockParentIndent,
                ref captureBlockIntoJobIf);
            return;
        }

        if (jobSection == JobSection.Permissions && indent > jobPropIndent)
        {
            ParsePermissionMappingLine(trimmed, lineNumber, currentJob.Permissions);
        }
        else if (jobSection == JobSection.Steps)
        {
            stepTracker.ProcessLine(trimmed, indent, lineNumber, workflow.Steps, ref blockParentIndent);
        }
    }

    private static JobSection ParseJobPropertyLine(
        string trimmed,
        int indent,
        int lineNumber,
        ParsedJob currentJob,
        ParsedWorkflow workflow,
        StepTracker stepTracker,
        ref int blockParentIndent,
        ref bool captureBlockIntoJobIf)
    {
        if (!TrySplitKeyValue(trimmed, out string propKey, out string propRawVal))
        {
            return JobSection.None;
        }

        string cleanVal = StripInlineComment(propRawVal);
        if (string.Equals(propKey, "if", StringComparison.Ordinal))
        {
            if (IsBlockScalarHeader(cleanVal))
            {
                blockParentIndent = indent;
                captureBlockIntoJobIf = true;
            }
            else
            {
                currentJob.AppendIf(Unquote(cleanVal));
            }

            return JobSection.None;
        }

        if (IsBlockScalarHeader(cleanVal))
        {
            blockParentIndent = indent;
            return JobSection.None;
        }

        if (string.Equals(propKey, "uses", StringComparison.Ordinal))
        {
            workflow.JobUses.Add(new UsesEntry(lineNumber, propRawVal));
            return JobSection.None;
        }

        if (string.Equals(propKey, "permissions", StringComparison.Ordinal))
        {
            currentJob.PermissionsLine = lineNumber;
            if (cleanVal.Length > 0)
            {
                if (cleanVal.StartsWith('{') && cleanVal.EndsWith('}') && cleanVal.Length > 2)
                {
                    ParseFlowPermissions(cleanVal, lineNumber, currentJob.Permissions);
                }
                else
                {
                    currentJob.PermissionsScalar = Unquote(cleanVal);
                }

                return JobSection.None;
            }

            return JobSection.Permissions;
        }

        if (string.Equals(propKey, "steps", StringComparison.Ordinal))
        {
            stepTracker.Reset(indent);
            return JobSection.Steps;
        }

        return JobSection.None;
    }

    private static ParsedAction ParseActionFile(string[] lines)
    {
        var action = new ParsedAction();
        bool inRuns = false;
        bool inSteps = false;
        int runsPropIndent = -1;
        int blockParentIndent = -1;
        var stepTracker = new StepTracker();

        for (int idx = 0; idx < lines.Length; idx++)
        {
            int lineNumber = idx + 1;
            string rawLine = lines[idx];
            string trimmed = rawLine.Trim();
            int indent = CountLeadingSpaces(rawLine);

            if (blockParentIndent >= 0)
            {
                if (trimmed.Length == 0 || indent > blockParentIndent)
                {
                    continue;
                }

                blockParentIndent = -1;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (indent == 0 && !trimmed.StartsWith('-'))
            {
                stepTracker.Flush(action.Steps);
                inSteps = false;
                if (TrySplitKeyValue(trimmed, out string topKey, out string topVal))
                {
                    if (IsBlockScalarHeader(StripInlineComment(topVal)))
                    {
                        blockParentIndent = 0;
                    }

                    inRuns = string.Equals(topKey, "runs", StringComparison.Ordinal);
                }
                else
                {
                    inRuns = false;
                }

                continue;
            }

            if (!inRuns)
            {
                continue;
            }

            if (!trimmed.StartsWith('-') && (runsPropIndent < 0 || indent == runsPropIndent))
            {
                runsPropIndent = indent;
                stepTracker.Flush(action.Steps);
                inSteps = false;

                if (TrySplitKeyValue(trimmed, out string runsKey, out string runsVal))
                {
                    string cleanVal = Unquote(StripInlineComment(runsVal));
                    if (IsBlockScalarHeader(cleanVal))
                    {
                        blockParentIndent = indent;
                    }
                    else if (string.Equals(runsKey, "image", StringComparison.Ordinal) &&
                             cleanVal.StartsWith("docker://", StringComparison.OrdinalIgnoreCase))
                    {
                        action.DockerImage = (lineNumber, cleanVal);
                    }
                    else if (string.Equals(runsKey, "steps", StringComparison.Ordinal))
                    {
                        stepTracker.Reset(indent);
                        inSteps = true;
                    }
                }

                continue;
            }

            if (inSteps)
            {
                stepTracker.ProcessLine(trimmed, indent, lineNumber, action.Steps, ref blockParentIndent);
            }
        }

        stepTracker.Flush(action.Steps);
        return action;
    }

    private static void ExtractInlineTriggers(string cleanVal, HashSet<string> triggers)
    {
        string inner = cleanVal.Trim('[', ']');
        foreach (string part in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = Unquote(part);
            if (name.Length > 0)
            {
                triggers.Add(name);
            }
        }
    }

    private static void ParseFlowPermissions(string flowText, int lineNumber, List<PermissionEntry> target)
    {
        string inner = flowText[1..^1];
        foreach (string item in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ParsePermissionMappingLine(item, lineNumber, target);
        }
    }

    private static void ParsePermissionMappingLine(string trimmed, int lineNumber, List<PermissionEntry> target)
    {
        if (TrySplitKeyValue(trimmed, out string scope, out string rawAccess))
        {
            string access = Unquote(StripInlineComment(rawAccess));
            if (access.Length > 0)
            {
                target.Add(new PermissionEntry(scope, access, lineNumber));
            }
        }
    }

    private static bool TrySplitKeyValue(string trimmed, out string key, out string rawValue)
    {
        int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            key = string.Empty;
            rawValue = string.Empty;
            return false;
        }

        if (colon + 1 < trimmed.Length && !char.IsWhiteSpace(trimmed[colon + 1]))
        {
            key = string.Empty;
            rawValue = string.Empty;
            return false;
        }

        key = Unquote(trimmed[..colon].Trim());
        rawValue = trimmed[(colon + 1)..].Trim();
        return key.Length > 0;
    }

    private static string StripInlineComment(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 && (trimmed[0] == '"' || trimmed[0] == '\''))
        {
            char quote = trimmed[0];
            int close = trimmed.IndexOf(quote, 1);
            if (close > 0)
            {
                return trimmed[..(close + 1)];
            }
        }

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] == '#' && (i == 0 || char.IsWhiteSpace(trimmed[i - 1])))
            {
                return trimmed[..i].TrimEnd();
            }
        }

        return trimmed;
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static bool IsBlockScalarHeader(string cleanValue) =>
        cleanValue.Length > 0 && BlockScalarRegex().IsMatch(cleanValue);

    private static int CountLeadingSpaces(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current)
            {
                break;
            }

            current = parent ?? string.Empty;
        }

        return Directory.GetCurrentDirectory();
    }

    [GeneratedRegex(@"^[|>][+-]?[0-9]*$")]
    private static partial Regex BlockScalarRegex();

    [GeneratedRegex(@"^[0-9a-fA-F]{40}$")]
    private static partial Regex CommitShaRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$")]
    private static partial Regex ValidActionSlugRegex();

    [GeneratedRegex(@"^docker://[A-Za-z0-9][A-Za-z0-9._/-]*(?::[A-Za-z0-9._-]+)?@sha256:[0-9a-fA-F]{64}$")]
    private static partial Regex DockerSha256DigestRegex();

    [GeneratedRegex(@"user\.login\s*==\s*['""][A-Za-z0-9_-]+\[bot\]['""]")]
    private static partial Regex BotLoginAllowlistRegex();

    private enum TopSection
    {
        None,
        On,
        Permissions,
        Jobs,
    }

    private enum JobSection
    {
        None,
        Permissions,
        Steps,
    }

    private sealed record UsesEntry(int LineNumber, string RawValue);

    private sealed record PermissionEntry(string Scope, string Access, int LineNumber);

    private sealed class ParsedStep
    {
        public UsesEntry? Uses { get; set; }

        public string? PersistCredentials { get; set; }
    }

    private sealed class ParsedJob(string key, int lineNumber)
    {
        private readonly StringBuilder _ifBuilder = new();

        public string Key { get; } = key;

        public int LineNumber { get; } = lineNumber;

        public int PermissionsLine { get; set; } = lineNumber;

        public string? PermissionsScalar { get; set; }

        public List<PermissionEntry> Permissions { get; } = [];

        public string IfExpression => _ifBuilder.ToString();

        public void AppendIf(string fragment)
        {
            if (fragment.Length == 0)
            {
                return;
            }

            if (_ifBuilder.Length > 0)
            {
                _ifBuilder.Append(' ');
            }

            _ifBuilder.Append(fragment);
        }
    }

    private sealed class ParsedWorkflow
    {
        public HashSet<string> Triggers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasTopLevelPermissions { get; set; }

        public int TopLevelPermissionsLine { get; set; } = 1;

        public string? TopLevelPermissionsScalar { get; set; }

        public List<PermissionEntry> TopLevelPermissions { get; } = [];

        public List<ParsedJob> Jobs { get; } = [];

        public List<UsesEntry> JobUses { get; } = [];

        public List<ParsedStep> Steps { get; } = [];
    }

    private sealed class ParsedAction
    {
        public (int LineNumber, string Value)? DockerImage { get; set; }

        public List<ParsedStep> Steps { get; } = [];
    }

    private sealed class StepTracker
    {
        private int _stepsKeyIndent = -1;
        private int _stepDashIndent = -1;
        private int _stepPropIndent = -1;
        private bool _inWithBlock;
        private ParsedStep? _current;

        public void Reset(int stepsKeyIndent)
        {
            _stepsKeyIndent = stepsKeyIndent;
            _stepDashIndent = -1;
            _stepPropIndent = -1;
            _inWithBlock = false;
            _current = null;
        }

        public void Flush(List<ParsedStep> steps)
        {
            if (_current is not null)
            {
                steps.Add(_current);
                _current = null;
            }

            _inWithBlock = false;
        }

        public void ProcessLine(
            string trimmed,
            int indent,
            int lineNumber,
            List<ParsedStep> steps,
            ref int blockParentIndent)
        {
            if (trimmed.StartsWith('-') &&
                (trimmed.Length == 1 || char.IsWhiteSpace(trimmed[1])) &&
                (_stepDashIndent < 0 ? indent >= _stepsKeyIndent : indent == _stepDashIndent))
            {
                Flush(steps);
                _stepDashIndent = indent;
                _stepPropIndent = indent + 2;
                _current = new ParsedStep();

                string afterDash = trimmed[1..].Trim();
                if (afterDash.Length > 0)
                {
                    ProcessStepProperty(afterDash, _stepPropIndent, lineNumber, ref blockParentIndent);
                }

                return;
            }

            if (_current is null)
            {
                return;
            }

            if (indent == _stepPropIndent && !trimmed.StartsWith('-'))
            {
                _inWithBlock = false;
                ProcessStepProperty(trimmed, indent, lineNumber, ref blockParentIndent);
                return;
            }

            if (_inWithBlock && indent > _stepPropIndent && TrySplitKeyValue(trimmed, out string withKey, out string withVal))
            {
                string cleanVal = StripInlineComment(withVal);
                if (IsBlockScalarHeader(cleanVal))
                {
                    blockParentIndent = indent;
                }
                else if (string.Equals(withKey, "persist-credentials", StringComparison.OrdinalIgnoreCase))
                {
                    _current.PersistCredentials = Unquote(cleanVal);
                }
            }
        }

        private void ProcessStepProperty(string propLine, int propIndent, int lineNumber, ref int blockParentIndent)
        {
            if (_current is null || !TrySplitKeyValue(propLine, out string key, out string rawVal))
            {
                return;
            }

            string cleanVal = StripInlineComment(rawVal);
            if (IsBlockScalarHeader(cleanVal))
            {
                blockParentIndent = propIndent;
                return;
            }

            if (string.Equals(key, "uses", StringComparison.Ordinal))
            {
                _current.Uses = new UsesEntry(lineNumber, rawVal);
            }
            else if (string.Equals(key, "with", StringComparison.Ordinal))
            {
                if (cleanVal.StartsWith('{') && cleanVal.EndsWith('}') && cleanVal.Length > 2)
                {
                    ParseFlowWith(cleanVal);
                }
                else
                {
                    _inWithBlock = true;
                }
            }
        }

        private void ParseFlowWith(string flowText)
        {
            if (_current is null)
            {
                return;
            }

            foreach (string item in flowText[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TrySplitKeyValue(item, out string k, out string v) &&
                    string.Equals(k, "persist-credentials", StringComparison.OrdinalIgnoreCase))
                {
                    _current.PersistCredentials = Unquote(StripInlineComment(v));
                }
            }
        }
    }
}
