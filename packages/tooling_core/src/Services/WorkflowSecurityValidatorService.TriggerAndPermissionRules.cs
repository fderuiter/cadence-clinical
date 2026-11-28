using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class WorkflowSecurityValidatorService
{
    [GeneratedRegex(@"(?:github\.event\.pull_request\.head\.repo\.full_name\s*==\s*github\.repository|github\.repository\s*==\s*github\.event\.pull_request\.head\.repo\.full_name)")]
    private static partial Regex SameRepoForkGuardRegex();

    [GeneratedRegex(@"github\.event_name\s*==\s*['""]pull_request(?:_target)?['""]\s*\|\|")]
    private static partial Regex PullRequestEventOrBypassRegex();

    [GeneratedRegex(@"\bgh\s+(?:pr\s+(?:edit|comment|merge|review|close|reopen|ready|lock|unlock|create)|(?:issue|label|release)\s+(?:create|edit|delete|comment|close|reopen|upload))\b")]
    private static partial Regex MutatingGhCliCommandRegex();

    [GeneratedRegex(@"\bgh\s+api\b")]
    private static partial Regex GhApiInvocationRegex();

    [GeneratedRegex(@"(?:\s-X\s*(?:POST|PUT|PATCH|DELETE)\b|--method(?:=|\s+)(?:POST|PUT|PATCH|DELETE)\b|\s-[fF]\b|--(?:raw-)?field\b|--input\b)", RegexOptions.IgnoreCase)]
    private static partial Regex GhApiMutatingFlagRegex();

    [GeneratedRegex(@"\b(?:gh\s+release\s+(?:create|upload|edit)|docker\s+push)\b")]
    private static partial Regex PublishCommandRegex();

    private static void ValidateWorkflowPermissionsAndTriggers(
        string relativePath,
        ParsedWorkflow workflow,
        bool triggersPullRequest,
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

        bool triggersIssueComment = workflow.Triggers.Contains("issue_comment");
        bool triggersWorkflowDispatch = workflow.Triggers.Contains("workflow_dispatch");

        if (triggersWorkflowDispatch)
        {
            ValidateWorkflowDispatchInputs(relativePath, workflow, errors);
        }

        foreach (var job in workflow.Jobs)
        {
            ValidateJobPermissions(
                relativePath,
                job,
                triggersPullRequest,
                triggersIssueComment,
                triggersWorkflowDispatch,
                errors);
        }
    }

    private static void ValidateWorkflowDispatchInputs(
        string relativePath,
        ParsedWorkflow workflow,
        List<string> errors)
    {
        foreach (var input in workflow.WorkflowDispatchInputs)
        {
            if (string.IsNullOrWhiteSpace(input.Type) ||
                (!string.Equals(input.Type, "choice", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(input.Type, "boolean", StringComparison.OrdinalIgnoreCase)))
            {
                string found = string.IsNullOrWhiteSpace(input.Type) ? "untyped" : input.Type;
                errors.Add($"{relativePath}:{input.LineNumber}: workflow_dispatch input '{input.Name}' must declare 'type: choice' or 'type: boolean' (found '{found}').");
            }
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
        bool triggersWorkflowDispatch,
        List<string> errors)
    {
        if (string.Equals(job.PermissionsScalar, "write-all", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{relativePath}:{job.PermissionsLine}: Job '{job.Key}' sets 'permissions: write-all'; grant only explicit least-privilege scopes.");
            return;
        }

        string jobIf = job.IfExpression;
        bool restrictedToPushOrDispatch = IsRestrictedToPushOrDispatchOnly(jobIf);

        if (triggersWorkflowDispatch &&
            DeploysAttestsOrPublishes(job) &&
            !ExcludesWorkflowDispatch(jobIf) &&
            !HasProtectedDispatchRefGuard(jobIf))
        {
            errors.Add($"{relativePath}:{job.LineNumber}: Job '{job.Key}' deploys, attests, or publishes on a 'workflow_dispatch' workflow without restricting manual dispatches to protected refs ('refs/heads/main', 'refs/heads/release/*', or 'refs/tags/v*').");
        }

        bool hasAnyWrite = job.Permissions.Any(p => string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase));
        if (!hasAnyWrite)
        {
            return;
        }

        if ((triggersPullRequest || triggersIssueComment) && !restrictedToPushOrDispatch)
        {
            foreach (var perm in job.Permissions.Where(p =>
                         IsReleaseOrPublishScope(p.Scope) &&
                         string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"{relativePath}:{perm.LineNumber}: Job '{job.Key}' grants '{perm.Scope}: write' on a pull_request or issue_comment workflow.");
            }
        }

        if (triggersPullRequest && !restrictedToPushOrDispatch)
        {
            var nonContentsWrites = job.Permissions
                .Where(p => !string.Equals(p.Scope, "contents", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (nonContentsWrites.Count > 0 && !HasSameRepoForkGuard(jobIf))
            {
                ValidatePullRequestJobStepForkGuards(relativePath, job, nonContentsWrites, errors);
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

    private static void ValidatePullRequestJobStepForkGuards(
        string relativePath,
        ParsedJob job,
        List<PermissionEntry> nonContentsWrites,
        List<string> errors)
    {
        bool anyGuardedStep = false;
        bool reportedUnguardedMutatingStep = false;
        string scopesSummary = string.Join(", ", nonContentsWrites.Select(p => $"{p.Scope}: write"));

        foreach (var step in job.Steps)
        {
            bool stepGuarded = HasSameRepoForkGuard(step.IfExpression) ||
                               IsRestrictedToPushOrDispatchOnly(step.IfExpression);
            if (stepGuarded)
            {
                anyGuardedStep = true;
                continue;
            }

            if (IsMutatingPullRequestStep(step))
            {
                reportedUnguardedMutatingStep = true;
                errors.Add(
                    $"{relativePath}:{step.LineNumber}: Job '{job.Key}' grants write permissions ({scopesSummary}) on a pull_request workflow, and mutating step lacks a same-repository fork guard ('github.event.pull_request.head.repo.full_name == github.repository').");
            }
        }

        if (!anyGuardedStep && !reportedUnguardedMutatingStep)
        {
            foreach (var perm in nonContentsWrites)
            {
                errors.Add(
                    $"{relativePath}:{perm.LineNumber}: Job '{job.Key}' grants '{perm.Scope}: write' on a pull_request workflow without gating the job or its write steps on 'github.event.pull_request.head.repo.full_name == github.repository'.");
            }
        }
    }

    private static bool HasSameRepoForkGuard(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        if (PullRequestEventOrBypassRegex().IsMatch(expression))
        {
            return false;
        }

        return SameRepoForkGuardRegex().IsMatch(expression);
    }

    private static bool IsMutatingPullRequestStep(ParsedStep step)
    {
        if (step.Uses is { } uses)
        {
            ExtractUsesRefAndComment(uses.RawValue, out string actionRef, out _);
            int atIdx = actionRef.IndexOf('@', StringComparison.Ordinal);
            string slug = atIdx > 0 ? actionRef[..atIdx] : actionRef;
            if (slug.StartsWith("github/codeql-action/upload-sarif", StringComparison.OrdinalIgnoreCase) ||
                slug.StartsWith("actions/github-script", StringComparison.OrdinalIgnoreCase) ||
                slug.StartsWith("actions/deploy-pages", StringComparison.OrdinalIgnoreCase) ||
                slug.StartsWith("actions/attest-build-provenance", StringComparison.OrdinalIgnoreCase) ||
                slug.StartsWith("peter-evans/", StringComparison.OrdinalIgnoreCase) ||
                slug.StartsWith("marocchino/", StringComparison.OrdinalIgnoreCase) ||
                slug.StartsWith("softprops/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (step.RunLines.Count == 0)
        {
            return false;
        }

        string combinedScript = string.Join(
            '\n',
            step.RunLines.Select(l => StripShellComment(StripGitHubExpressions(l.Text))));
        return MutatingGhCliCommandRegex().IsMatch(combinedScript) ||
               (GhApiInvocationRegex().IsMatch(combinedScript) && GhApiMutatingFlagRegex().IsMatch(combinedScript));
    }

    private static bool IsReleaseOrPublishScope(string scope) =>
        string.Equals(scope, "contents", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, "packages", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, "pages", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, "attestations", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, "id-token", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scope, "deployments", StringComparison.OrdinalIgnoreCase);

    private static bool DeploysAttestsOrPublishes(ParsedJob job)
    {
        if (job.ReusableWorkflowUses is not null)
        {
            return true;
        }

        if (job.Permissions.Any(p =>
                IsReleaseOrPublishScope(p.Scope) &&
                string.Equals(p.Access, "write", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var step in job.Steps)
        {
            if (step.Uses is { } uses)
            {
                ExtractUsesRefAndComment(uses.RawValue, out string actionRef, out _);
                int at = actionRef.IndexOf('@', StringComparison.Ordinal);
                string slug = at > 0 ? actionRef[..at] : actionRef;
                if (slug.StartsWith("actions/deploy-pages", StringComparison.OrdinalIgnoreCase) ||
                    slug.StartsWith("actions/upload-pages-artifact", StringComparison.OrdinalIgnoreCase) ||
                    slug.StartsWith("actions/attest", StringComparison.OrdinalIgnoreCase) ||
                    slug.StartsWith("softprops/action-gh-release", StringComparison.OrdinalIgnoreCase) ||
                    slug.StartsWith("ncipollo/release-action", StringComparison.OrdinalIgnoreCase) ||
                    slug.StartsWith("docker/build-push-action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (step.RunLines.Any(line => PublishCommandRegex().IsMatch(line.Text)))
            {
                return true;
            }
        }

        return false;
    }
}
