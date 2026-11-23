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
/// least-privilege workflow token permissions, ref-guarded <c>workflow_dispatch</c> controls with typed inputs,
/// and bot actor gating on <c>issue_comment</c> mutation jobs.
/// </summary>
public static partial class WorkflowSecurityValidatorService
{
    public static int ValidateWorkflowSecurity(
        string[]? files = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating GitHub Actions workflow security, SHA pinning, and token permissions...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ No workflow or composite action files in target list.");
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var targets = ResolveTargetFiles(files, repoRoot, reader, out bool explicitFilterEmpty);
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
                ValidateFile(fullPath, relativePath, isWorkflow, reader, errors);
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
    }

    private static List<(string FullPath, string RelativePath, bool IsWorkflow)> ResolveTargetFiles(
        string[]? files,
        string repoRoot,
        GitIndexReader? reader,
        out bool explicitFilterEmpty)
    {
        explicitFilterEmpty = false;
        var results = new List<(string FullPath, string RelativePath, bool IsWorkflow)>();

        if (files is not null)
        {
            foreach (string raw in files)
            {
                string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                if (string.IsNullOrEmpty(rel))
                {
                    continue;
                }

                string fullPath = Path.Combine(repoRoot, rel);
                bool exists = reader is not null ? reader.FileExists(rel) : File.Exists(fullPath);
                if (!exists)
                {
                    continue;
                }

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

    private static void ValidateFile(
        string fullPath,
        string relativePath,
        bool isWorkflow,
        GitIndexReader? reader,
        List<string> errors)
    {
        string[] lines;
        if (reader is not null)
        {
            if (!reader.TryReadLines(relativePath, out lines))
            {
                return;
            }
        }
        else
        {
            lines = File.ReadAllLines(fullPath);
        }
        if (isWorkflow)
        {
            var workflow = ParseWorkflowFile(lines);
            bool triggersPullRequest = workflow.Triggers.Contains("pull_request") ||
                                       workflow.Triggers.Contains("pull_request_target");
            ValidateWorkflowPermissionsAndTriggers(relativePath, workflow, triggersPullRequest, errors);
            foreach (var uses in workflow.JobUses)
            {
                ValidateUsesReference(relativePath, uses, errors);
            }

            foreach (var step in workflow.Steps)
            {
                ValidateStep(relativePath, step, triggersPullRequest, errors);
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
                ValidateStep(relativePath, step, triggersPullRequest: false, errors);
            }
        }
    }

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

    private static bool IsRestrictedToPushOrDispatchOnly(string jobIf)
    {
        string unwrapped = UnwrapExpression(jobIf);
        if (unwrapped.Length == 0 ||
            unwrapped.Contains("pull_request", StringComparison.Ordinal) ||
            unwrapped.Contains("issue_comment", StringComparison.Ordinal))
        {
            return false;
        }

        var orBranches = SplitTopLevelLogical(unwrapped, "||");
        return orBranches.Count > 0 && orBranches.All(BranchRequiresPushOrDispatch);
    }

    private static bool BranchRequiresPushOrDispatch(string branch)
    {
        string unwrapped = UnwrapExpression(branch);
        var orClauses = SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(BranchRequiresPushOrDispatch);
        }

        var andClauses = SplitTopLevelLogical(unwrapped, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(BranchRequiresPushOrDispatch);
        }

        string normalized = CollapseSpaces(unwrapped);
        return normalized is "github.event_name == 'push'"
            or "github.event_name == \"push\""
            or "'push' == github.event_name"
            or "\"push\" == github.event_name"
            or "github.event_name == 'workflow_dispatch'"
            or "github.event_name == \"workflow_dispatch\""
            or "'workflow_dispatch' == github.event_name"
            or "\"workflow_dispatch\" == github.event_name";
    }

    private static bool ExcludesWorkflowDispatch(string jobIf)
    {
        string unwrapped = UnwrapExpression(jobIf);
        if (unwrapped.Length == 0)
        {
            return false;
        }

        var orBranches = SplitTopLevelLogical(unwrapped, "||");
        return orBranches.Count > 0 && orBranches.All(BranchExcludesWorkflowDispatch);
    }

    private static bool BranchExcludesWorkflowDispatch(string branch)
    {
        string unwrapped = UnwrapExpression(branch);
        var orClauses = SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(BranchExcludesWorkflowDispatch);
        }

        var andClauses = SplitTopLevelLogical(unwrapped, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(BranchExcludesWorkflowDispatch);
        }

        string normalized = CollapseSpaces(unwrapped);
        return normalized is "github.event_name == 'push'"
            or "github.event_name == \"push\""
            or "'push' == github.event_name"
            or "\"push\" == github.event_name"
            or "github.event_name == 'pull_request'"
            or "github.event_name == \"pull_request\""
            or "github.event_name != 'workflow_dispatch'"
            or "github.event_name != \"workflow_dispatch\"";
    }

    private static bool HasProtectedDispatchRefGuard(string jobIf)
    {
        string unwrapped = UnwrapExpression(jobIf);
        if (unwrapped.Length == 0)
        {
            return false;
        }

        var orBranches = SplitTopLevelLogical(unwrapped, "||");
        return orBranches.Count > 0 &&
               orBranches.All(branch => BranchExcludesWorkflowDispatch(branch) || BranchRequiresProtectedRef(branch));
    }

    private static bool BranchRequiresProtectedRef(string branch)
    {
        string unwrapped = UnwrapExpression(branch);
        var orClauses = SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(IsProtectedRefClause);
        }

        var andClauses = SplitTopLevelLogical(unwrapped, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(BranchRequiresProtectedRef);
        }

        return IsProtectedRefClause(unwrapped);
    }

    private static bool IsProtectedRefClause(string clause)
    {
        string unwrapped = UnwrapExpression(clause);
        var orClauses = SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(IsProtectedRefClause);
        }

        string normalized = CollapseSpaces(unwrapped);
        return normalized is "github.ref == 'refs/heads/main'"
            or "github.ref == \"refs/heads/main\""
            or "startsWith(github.ref, 'refs/heads/release/')"
            or "startsWith(github.ref, \"refs/heads/release/\")"
            or "startsWith(github.ref, 'refs/tags/v')"
            or "startsWith(github.ref, \"refs/tags/v\")";
    }

    private static string UnwrapExpression(string expression)
    {
        string current = expression.Trim();
        if (current.StartsWith("${{", StringComparison.Ordinal) && current.EndsWith("}}", StringComparison.Ordinal))
        {
            current = current[3..^2].Trim();
        }

        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && HasBalancedOuterParens(current))
        {
            current = current[1..^1].Trim();
        }

        return current;
    }

    private static bool HasBalancedOuterParens(string text)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            UpdateExprQuoteAndDepth(text[i], ref quote, ref depth);
            if (depth == 0 && i < text.Length - 1)
            {
                return false;
            }
        }

        return depth == 0 && quote == '\0';
    }

    private static List<string> SplitTopLevelLogical(string text, string op)
    {
        var parts = new List<string>();
        int depth = 0;
        char quote = '\0';
        int start = 0;

        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            UpdateExprQuoteAndDepth(ch, ref quote, ref depth);
            if (quote == '\0' && depth == 0 && i + op.Length <= text.Length &&
                string.CompareOrdinal(text, i, op, 0, op.Length) == 0)
            {
                parts.Add(text[start..i].Trim());
                i += op.Length;
                start = i;
                continue;
            }

            i++;
        }

        parts.Add(text[start..].Trim());
        return parts;
    }

    private static void UpdateExprQuoteAndDepth(char ch, ref char quote, ref int depth)
    {
        if (quote != '\0')
        {
            if (ch == quote)
            {
                quote = '\0';
            }

            return;
        }

        if (ch is '\'' or '"')
        {
            quote = ch;
        }
        else if (ch == '(')
        {
            depth++;
        }
        else if (ch == ')' && depth > 0)
        {
            depth--;
        }
    }

    private static string CollapseSpaces(string value) =>
        WhitespaceCollapseRegex().Replace(value.Trim(), " ");

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

    private static void ValidateStep(
        string relativePath,
        ParsedStep step,
        bool triggersPullRequest,
        List<string> errors)
    {
        foreach (var runLine in step.RunLines.Where(line => DirectInputsInterpolationRegex().IsMatch(line.Text)))
        {
            errors.Add($"{relativePath}:{runLine.LineNumber}: Step 'run:' script interpolates workflow inputs directly; pass 'inputs.*' through 'env:' variables to prevent script injection.");
        }

        if (step.Uses is { } uses)
        {
            string? actionName = ValidateUsesReference(relativePath, uses, errors);
            if (actionName is not null && IsCheckoutAction(actionName))
            {
                if (!string.Equals(step.PersistCredentials, "false", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{relativePath}:{uses.LineNumber}: Step using '{actionName}' must set 'persist-credentials: false' under 'with:'.");
                }

                if (step.CheckoutRepository is { } checkoutRepo)
                {
                    bool isSelfRepo = SelfRepositoryExpressionRegex().IsMatch(checkoutRepo.Value);
                    if (checkoutRepo.Value.Contains("head.repo", StringComparison.OrdinalIgnoreCase) ||
                        checkoutRepo.Value.Contains("head_ref", StringComparison.OrdinalIgnoreCase) ||
                        (triggersPullRequest && !isSelfRepo))
                    {
                        errors.Add($"{relativePath}:{checkoutRepo.LineNumber}: Step using '{actionName}' sets 'repository: {checkoutRepo.Value}'; checking out fork or external pull request repositories is forbidden.");
                    }
                }

                if (step.CheckoutRef is { } checkoutRef &&
                    (checkoutRef.Value.Contains("pull_request.head.", StringComparison.OrdinalIgnoreCase) ||
                     checkoutRef.Value.Contains("github.head_ref", StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"{relativePath}:{checkoutRef.LineNumber}: Step using '{actionName}' sets 'ref: {checkoutRef.Value}'; checking out untrusted pull request head refs is forbidden (fetch refs from '$GITHUB_REPOSITORY' instead).");
                }
            }
        }

        ValidateStepRunScript(relativePath, step, errors);
    }

    private static void ValidateStepRunScript(string relativePath, ParsedStep step, List<string> errors)
    {
        foreach (var (lineNumber, rawLine) in step.RunLines)
        {
            string sanitized = StripShellComment(StripGitHubExpressions(rawLine));
            if (sanitized.Length == 0)
            {
                continue;
            }

            if (MaskedShellErrorRegex().IsMatch(sanitized))
            {
                errors.Add($"{relativePath}:{lineNumber}: Step 'run:' script masks command failure with a fallback ('|| ...'); fail closed instead of swallowing errors.");
            }
        }
    }

    private static string StripGitHubExpressions(string line) =>
        GitHubExpressionRegex().Replace(line, "EXPR");

    private static string StripShellComment(string line)
    {
        char quote = '\0';
        int index = 0;
        while (index < line.Length)
        {
            char ch = line[index];
            if (quote != '\0')
            {
                if (ch == '\\' && quote == '"' && index + 1 < line.Length)
                {
                    index += 2;
                    continue;
                }

                if (ch == quote)
                {
                    quote = '\0';
                }

                index++;
                continue;
            }

            if (ch is '\'' or '"')
            {
                quote = ch;
            }
            else if (ch == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1])))
            {
                return line[..index].TrimEnd();
            }

            index++;
        }

        return line.Trim();
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
            errors.Add($"{relativePath}:{lineNumber}: Container reference '{dockerRef}' must be pinned to an immutable 'sha256:<64-hex>' digest (e.g. 'docker://image:tag@sha256:...')."); // pragma: allowlist secret
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

        var onTracker = new OnSectionTracker();
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
                    else
                    {
                        stepTracker.CaptureBlockLine(rawLine, trimmed, lineNumber);
                    }

                    continue;
                }

                blockParentIndent = -1;
                captureBlockIntoJobIf = false;
                stepTracker.EndBlockScalar();
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (indent == 0 && !trimmed.StartsWith('-'))
            {
                stepTracker.Flush(workflow.Steps, currentJob?.Steps);
                currentJob = null;
                jobSection = JobSection.None;
                onTracker.Reset();
                topSection = ParseTopLevelLine(trimmed, lineNumber, workflow, ref blockParentIndent);
                continue;
            }

            switch (topSection)
            {
                case TopSection.On:
                    onTracker.ProcessLine(trimmed, indent, lineNumber, workflow, ref blockParentIndent);
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

        stepTracker.Flush(workflow.Steps, currentJob?.Steps);
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

    private sealed class OnSectionTracker
    {
        private int _eventIndent = -1;
        private int _dispatchChildIndent = -1;
        private int _inputNameIndent = -1;
        private bool _inWorkflowDispatch;
        private bool _inDispatchInputs;
        private WorkflowDispatchInput? _currentInput;

        public void Reset()
        {
            _eventIndent = -1;
            _dispatchChildIndent = -1;
            _inputNameIndent = -1;
            _inWorkflowDispatch = false;
            _inDispatchInputs = false;
            _currentInput = null;
        }

        public void ProcessLine(
            string trimmed,
            int indent,
            int lineNumber,
            ParsedWorkflow workflow,
            ref int blockParentIndent)
        {
            if (_eventIndent < 0 && trimmed.StartsWith('-'))
            {
                string item = Unquote(StripInlineComment(trimmed[1..].Trim()));
                if (item.Length > 0)
                {
                    workflow.Triggers.Add(item);
                }

                return;
            }

            if (!trimmed.StartsWith('-') && (_eventIndent < 0 || indent == _eventIndent))
            {
                _eventIndent = indent;
                _dispatchChildIndent = -1;
                _inputNameIndent = -1;
                _inDispatchInputs = false;
                _currentInput = null;
                if (TrySplitKeyValue(trimmed, out string eventName, out string eventVal))
                {
                    workflow.Triggers.Add(eventName);
                    _inWorkflowDispatch = string.Equals(eventName, "workflow_dispatch", StringComparison.OrdinalIgnoreCase);
                    if (IsBlockScalarHeader(StripInlineComment(eventVal)))
                    {
                        blockParentIndent = indent;
                    }
                }

                return;
            }

            if (!_inWorkflowDispatch || trimmed.StartsWith('-') || !TrySplitKeyValue(trimmed, out string key, out string rawVal))
            {
                return;
            }

            string cleanVal = StripInlineComment(rawVal);
            if (IsBlockScalarHeader(cleanVal))
            {
                blockParentIndent = indent;
                return;
            }

            if (_dispatchChildIndent < 0 || indent == _dispatchChildIndent)
            {
                _dispatchChildIndent = indent;
                _inputNameIndent = -1;
                _currentInput = null;
                _inDispatchInputs = string.Equals(key, "inputs", StringComparison.Ordinal);
                return;
            }

            if (!_inDispatchInputs || indent <= _dispatchChildIndent)
            {
                return;
            }

            if (_inputNameIndent < 0 || indent == _inputNameIndent)
            {
                _inputNameIndent = indent;
                _currentInput = new WorkflowDispatchInput(key, lineNumber);
                workflow.WorkflowDispatchInputs.Add(_currentInput);
                return;
            }

            if (_currentInput is not null && indent > _inputNameIndent && string.Equals(key, "type", StringComparison.Ordinal))
            {
                _currentInput.Type = Unquote(cleanVal);
            }
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
                stepTracker.Flush(workflow.Steps, currentJob?.Steps);
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
            stepTracker.Flush(workflow.Steps, currentJob.Steps);
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
            stepTracker.ProcessLine(trimmed, indent, lineNumber, workflow.Steps, currentJob.Steps, ref blockParentIndent);
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
            var usesEntry = new UsesEntry(lineNumber, propRawVal);
            currentJob.ReusableWorkflowUses = usesEntry;
            workflow.JobUses.Add(usesEntry);
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
                    stepTracker.CaptureBlockLine(rawLine, trimmed, lineNumber);
                    continue;
                }

                blockParentIndent = -1;
                stepTracker.EndBlockScalar();
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (indent == 0 && !trimmed.StartsWith('-'))
            {
                stepTracker.Flush(action.Steps, null);
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
                stepTracker.Flush(action.Steps, null);
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
                stepTracker.ProcessLine(trimmed, indent, lineNumber, action.Steps, jobSteps: null, ref blockParentIndent);
            }
        }

        stepTracker.Flush(action.Steps, null);
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

    [GeneratedRegex(@"(?:github\.event\.pull_request\.head\.repo\.full_name\s*==\s*github\.repository|github\.repository\s*==\s*github\.event\.pull_request\.head\.repo\.full_name)")]
    private static partial Regex SameRepoForkGuardRegex();

    [GeneratedRegex(@"github\.event_name\s*==\s*['""]pull_request(?:_target)?['""]\s*\|\|")]
    private static partial Regex PullRequestEventOrBypassRegex();

    [GeneratedRegex(@"^\$\{\{\s*github\.repository\s*\}\}$")]
    private static partial Regex SelfRepositoryExpressionRegex();

    [GeneratedRegex(@"\$\{\{.*?\}\}")]
    private static partial Regex GitHubExpressionRegex();

    [GeneratedRegex(@"\|\|\s*(?:true\b|:(?:\s|$|;|\))|exit\s+0\b|echo\b|printf\b)")]
    private static partial Regex MaskedShellErrorRegex();

    [GeneratedRegex(@"\bgh\s+(?:pr\s+(?:edit|comment|merge|review|close|reopen|ready|lock|unlock|create)|(?:issue|label|release)\s+(?:create|edit|delete|comment|close|reopen|upload))\b")]
    private static partial Regex MutatingGhCliCommandRegex();

    [GeneratedRegex(@"\bgh\s+api\b")]
    private static partial Regex GhApiInvocationRegex();

    [GeneratedRegex(@"(?:\s-X\s*(?:POST|PUT|PATCH|DELETE)\b|--method(?:=|\s+)(?:POST|PUT|PATCH|DELETE)\b|\s-[fF]\b|--(?:raw-)?field\b|--input\b)", RegexOptions.IgnoreCase)]
    private static partial Regex GhApiMutatingFlagRegex();

    [GeneratedRegex(@"\$\{\{\s*(?:inputs\.|github\.event\.inputs\.)")]
    private static partial Regex DirectInputsInterpolationRegex();

    [GeneratedRegex(@"\b(?:gh\s+release\s+(?:create|upload|edit)|docker\s+push)\b")]
    private static partial Regex PublishCommandRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceCollapseRegex();

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

    private enum StepBlockMode
    {
        None,
        If,
        Run,
    }

    private sealed record UsesEntry(int LineNumber, string RawValue);

    private sealed record PermissionEntry(string Scope, string Access, int LineNumber);

    private sealed class WorkflowDispatchInput(string name, int lineNumber)
    {
        public string Name { get; } = name;

        public int LineNumber { get; } = lineNumber;

        public string? Type { get; set; }
    }

    private sealed class ParsedStep
    {
        private readonly StringBuilder _ifBuilder = new();

        public int LineNumber { get; init; } = 1;

        public UsesEntry? Uses { get; set; }

        public string? PersistCredentials { get; set; }

        public (int LineNumber, string Value)? CheckoutRepository { get; set; }

        public (int LineNumber, string Value)? CheckoutRef { get; set; }

        public List<(int LineNumber, string Text)> RunLines { get; } = [];

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

    private sealed class ParsedJob(string key, int lineNumber)
    {
        private readonly StringBuilder _ifBuilder = new();

        public string Key { get; } = key;

        public int LineNumber { get; } = lineNumber;

        public int PermissionsLine { get; set; } = lineNumber;

        public string? PermissionsScalar { get; set; }

        public List<PermissionEntry> Permissions { get; } = [];

        public UsesEntry? ReusableWorkflowUses { get; set; }

        public List<ParsedStep> Steps { get; } = [];

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

        public List<WorkflowDispatchInput> WorkflowDispatchInputs { get; } = [];

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
        private StepBlockMode _blockMode;
        private ParsedStep? _current;

        public void Reset(int stepsKeyIndent)
        {
            _stepsKeyIndent = stepsKeyIndent;
            _stepDashIndent = -1;
            _stepPropIndent = -1;
            _inWithBlock = false;
            _blockMode = StepBlockMode.None;
            _current = null;
        }

        public void Flush(List<ParsedStep> steps, List<ParsedStep>? jobSteps = null)
        {
            if (_current is not null)
            {
                steps.Add(_current);
                jobSteps?.Add(_current);
                _current = null;
            }

            _inWithBlock = false;
            _blockMode = StepBlockMode.None;
        }

        public void CaptureBlockLine(string rawLine, string trimmed, int lineNumber)
        {
            if (_current is null)
            {
                return;
            }

            if (_blockMode == StepBlockMode.If && trimmed.Length > 0 && !trimmed.StartsWith('#'))
            {
                _current.AppendIf(StripInlineComment(trimmed));
            }
            else if (_blockMode == StepBlockMode.Run && trimmed.Length > 0 && !trimmed.StartsWith('#'))
            {
                _current.RunLines.Add((lineNumber, rawLine));
            }
        }

        public void EndBlockScalar() => _blockMode = StepBlockMode.None;

        public void ProcessLine(
            string trimmed,
            int indent,
            int lineNumber,
            List<ParsedStep> steps,
            List<ParsedStep>? jobSteps,
            ref int blockParentIndent)
        {
            if (trimmed.StartsWith('-') &&
                (trimmed.Length == 1 || char.IsWhiteSpace(trimmed[1])) &&
                (_stepDashIndent < 0 ? indent >= _stepsKeyIndent : indent == _stepDashIndent))
            {
                Flush(steps, jobSteps);
                _stepDashIndent = indent;
                _stepPropIndent = indent + 2;
                _current = new ParsedStep { LineNumber = lineNumber };

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
                else
                {
                    RecordWithEntry(withKey, Unquote(cleanVal), lineNumber);
                }
            }
        }

        private void ProcessStepProperty(
            string propLine,
            int propIndent,
            int lineNumber,
            ref int blockParentIndent)
        {
            if (_current is null || !TrySplitKeyValue(propLine, out string key, out string rawVal))
            {
                return;
            }

            string cleanVal = StripInlineComment(rawVal);
            if (string.Equals(key, "if", StringComparison.Ordinal))
            {
                if (IsBlockScalarHeader(cleanVal))
                {
                    _blockMode = StepBlockMode.If;
                    blockParentIndent = propIndent;
                }
                else
                {
                    _current.AppendIf(Unquote(cleanVal));
                }

                return;
            }

            if (string.Equals(key, "run", StringComparison.Ordinal))
            {
                if (IsBlockScalarHeader(cleanVal))
                {
                    _blockMode = StepBlockMode.Run;
                    blockParentIndent = propIndent;
                }
                else if (cleanVal.Length > 0)
                {
                    _current.RunLines.Add((lineNumber, Unquote(rawVal)));
                }

                return;
            }

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
                    ParseFlowWith(cleanVal, lineNumber);
                }
                else
                {
                    _inWithBlock = true;
                }
            }
        }

        private void ParseFlowWith(string flowText, int lineNumber)
        {
            if (_current is null)
            {
                return;
            }

            foreach (string item in flowText[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (TrySplitKeyValue(item, out string k, out string v))
                {
                    RecordWithEntry(k, Unquote(StripInlineComment(v)), lineNumber);
                }
            }
        }

        private void RecordWithEntry(string key, string value, int lineNumber)
        {
            if (_current is null)
            {
                return;
            }

            if (string.Equals(key, "persist-credentials", StringComparison.OrdinalIgnoreCase))
            {
                _current.PersistCredentials = value;
            }
            else if (string.Equals(key, "repository", StringComparison.OrdinalIgnoreCase))
            {
                _current.CheckoutRepository = (lineNumber, value);
            }
            else if (string.Equals(key, "ref", StringComparison.OrdinalIgnoreCase))
            {
                _current.CheckoutRef = (lineNumber, value);
            }
        }
    }
}
