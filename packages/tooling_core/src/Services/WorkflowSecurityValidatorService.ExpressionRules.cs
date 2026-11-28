using System;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class WorkflowSecurityValidatorService
{
    [GeneratedRegex(@"user\.login\s*==\s*['""][A-Za-z0-9_-]+\[bot\]['""]")]
    private static partial Regex BotLoginAllowlistRegex();

    private static bool IsRestrictedToPushOrDispatchOnly(string jobIf)
    {
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(jobIf);
        if (unwrapped.Length == 0 ||
            unwrapped.Contains("pull_request", StringComparison.Ordinal) ||
            unwrapped.Contains("issue_comment", StringComparison.Ordinal))
        {
            return false;
        }

        var orBranches = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "||");
        return orBranches.Count > 0 && orBranches.All(BranchRequiresPushOrDispatch);
    }

    private static bool BranchRequiresPushOrDispatch(string branch)
    {
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(branch);
        var orClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(BranchRequiresPushOrDispatch);
        }

        var andClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(BranchRequiresPushOrDispatch);
        }

        string normalized = WorkflowExpressionPrimitives.CollapseWhitespace(unwrapped);
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
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(jobIf);
        if (unwrapped.Length == 0)
        {
            return false;
        }

        var orBranches = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "||");
        return orBranches.Count > 0 && orBranches.All(BranchExcludesWorkflowDispatch);
    }

    private static bool BranchExcludesWorkflowDispatch(string branch)
    {
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(branch);
        var orClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(BranchExcludesWorkflowDispatch);
        }

        var andClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(BranchExcludesWorkflowDispatch);
        }

        string normalized = WorkflowExpressionPrimitives.CollapseWhitespace(unwrapped);
        return normalized is "github.event_name == 'push'"
            or "github.event_name == \"push\""
            or "'push' == github.event_name"
            or "\"push\" == github.event_name"
            or "github.event_name == 'pull_request'"
            or "github.event_name == \"pull_request\""
            or "'pull_request' == github.event_name"
            or "\"pull_request\" == github.event_name"
            or "github.event_name == 'merge_group'"
            or "github.event_name == \"merge_group\""
            or "'merge_group' == github.event_name"
            or "\"merge_group\" == github.event_name"
            or "github.event_name != 'workflow_dispatch'"
            or "github.event_name != \"workflow_dispatch\"";
    }

    private static bool HasProtectedDispatchRefGuard(string jobIf)
    {
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(jobIf);
        if (unwrapped.Length == 0)
        {
            return false;
        }

        var orBranches = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "||");
        if (orBranches.Count > 1)
        {
            return orBranches.All(HasProtectedDispatchRefGuard);
        }

        var andClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(HasProtectedDispatchRefGuard);
        }

        return BranchExcludesWorkflowDispatch(unwrapped) || BranchRequiresProtectedRef(unwrapped);
    }

    private static bool BranchRequiresProtectedRef(string branch)
    {
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(branch);
        var orClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(IsProtectedRefClause);
        }

        var andClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(BranchRequiresProtectedRef);
        }

        return IsProtectedRefClause(unwrapped);
    }

    private static bool IsProtectedRefClause(string clause)
    {
        string unwrapped = WorkflowExpressionPrimitives.UnwrapExpression(clause);
        var orClauses = WorkflowExpressionPrimitives.SplitTopLevelLogical(unwrapped, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(IsProtectedRefClause);
        }

        string normalized = WorkflowExpressionPrimitives.CollapseWhitespace(unwrapped);
        return normalized is "github.ref == 'refs/heads/main'"
            or "github.ref == \"refs/heads/main\""
            or "startsWith(github.ref, 'refs/heads/release/')"
            or "startsWith(github.ref, \"refs/heads/release/\")"
            or "startsWith(github.ref, 'refs/tags/v')"
            or "startsWith(github.ref, \"refs/tags/v\")";
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
}
