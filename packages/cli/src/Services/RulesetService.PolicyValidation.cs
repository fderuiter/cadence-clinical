using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Cli.Services;

public static partial class RulesetService
{
    private static readonly string[] RequiredRulesetFiles =
    [
        "main.json",
        "release_branches.json",
        "version_tags.json",
    ];

    private static readonly string[] RequiredTagRuleTypes =
    [
        "creation",
        "required_signatures",
        "deletion",
        "non_fast_forward",
    ];

    private static void ValidateRulesetPolicy(RulesetDefinition def, List<string> errors)
    {
        string prefix = $".github/rulesets/{def.FileName}";

        if (string.IsNullOrWhiteSpace(def.Name))
        {
            errors.Add($"{prefix}: missing non-empty 'name'.");
        }

        if (def.Enforcement != "active")
        {
            errors.Add($"{prefix}: 'enforcement' must be 'active' (found '{def.Enforcement}').");
        }

        if (def.IncludeRefs.Count == 0)
        {
            errors.Add($"{prefix}: 'conditions.ref_name.include' must specify at least one ref pattern.");
        }

        if (!def.RuleTypes.Contains("deletion", StringComparer.Ordinal) || !def.RuleTypes.Contains("non_fast_forward", StringComparer.Ordinal))
        {
            errors.Add($"{prefix}: 'rules' must include both 'deletion' and 'non_fast_forward'.");
        }

        if (def.FileName == "main.json" && (def.Target != "branch" || !def.IncludeRefs.Contains("refs/heads/main", StringComparer.Ordinal)))
        {
            errors.Add($"{prefix}: must target branch 'refs/heads/main'.");
        }

        if (def.FileName == "release_branches.json" && (def.Target != "branch" || !def.IncludeRefs.Contains("refs/heads/release/*", StringComparer.Ordinal)))
        {
            errors.Add($"{prefix}: must target branch 'refs/heads/release/*'.");
        }

        if (def.FileName == "version_tags.json")
        {
            ValidateTagRuleset(def, prefix, errors);
            return;
        }

        if (def.Target == "branch")
        {
            ValidateBranchRuleset(def, prefix, errors);
        }
        else if (def.Target == "tag")
        {
            if (def.HasBypassActors)
            {
                errors.Add($"{prefix}: tag ruleset must not configure 'bypass_actors'.");
            }
        }
        else
        {
            errors.Add($"{prefix}: 'target' must be 'branch' or 'tag' (found '{def.Target}').");
        }
    }

    private static void ValidateTagRuleset(RulesetDefinition def, string prefix, List<string> errors)
    {
        if (def.Target != "tag" || !def.IncludeRefs.Contains("refs/tags/v*", StringComparer.Ordinal))
        {
            errors.Add($"{prefix}: must target tag pattern 'refs/tags/v*'.");
        }

        var missingTagRules = RequiredTagRuleTypes
            .Where(required => !def.RuleTypes.Contains(required, StringComparer.Ordinal))
            .ToList();
        if (missingTagRules.Count > 0)
        {
            errors.Add($"{prefix}: version tag ruleset must require {string.Join(", ", missingTagRules.Select(r => $"'{r}'"))}.");
        }

        if (def.BypassActors.Count == 0)
        {
            errors.Add($"{prefix}: version tag ruleset must pin 'bypass_actors' to release admins (RepositoryRole actor_id {RepositoryAdminRoleId}, bypass_mode 'always').");
            return;
        }

        foreach (var actor in def.BypassActors)
        {
            if (actor.ActorId != RepositoryAdminRoleId
                || actor.ActorType != "RepositoryRole"
                || actor.BypassMode != "always")
            {
                errors.Add($"{prefix}: 'bypass_actors' on version tags may only grant release admins (actor_type 'RepositoryRole', actor_id {RepositoryAdminRoleId}, bypass_mode 'always').");
            }
        }
    }

    private static void ValidateBranchRuleset(RulesetDefinition def, string prefix, List<string> errors)
    {
        foreach (var actor in def.BypassActors)
        {
            if (string.IsNullOrWhiteSpace(actor.ActorType))
            {
                errors.Add($"{prefix}: branch ruleset 'bypass_actors' entry is missing 'actor_type'.");
            }
            else if (actor.ActorId != RepositoryAdminRoleId || actor.ActorType != "RepositoryRole")
            {
                errors.Add($"{prefix}: branch ruleset 'bypass_actors' may only grant repository admins (actor_type 'RepositoryRole', actor_id {RepositoryAdminRoleId}, bypass_mode '{PullRequestOnlyBypassMode}').");
            }

            if (actor.BypassMode != PullRequestOnlyBypassMode)
            {
                string actualMode = string.IsNullOrWhiteSpace(actor.BypassMode) ? "<missing>" : actor.BypassMode;
                errors.Add($"{prefix}: branch ruleset 'bypass_actors' must restrict 'bypass_mode' to '{PullRequestOnlyBypassMode}' (found '{actualMode}').");
            }
        }

        if (!def.RuleTypes.Contains("pull_request", StringComparer.Ordinal))
        {
            errors.Add($"{prefix}: branch ruleset must include a 'pull_request' rule.");
        }
        else if (def.RequiredApprovingReviews is null or < 1 || !def.RequireCodeOwnerReview)
        {
            errors.Add($"{prefix}: 'pull_request' rule must require at least 1 approving review and CODEOWNERS review.");
        }

        if (def.FileName == "main.json" && !def.RuleTypes.Contains("merge_queue", StringComparer.Ordinal))
        {
            errors.Add($"{prefix}: main branch ruleset must include a 'merge_queue' rule.");
        }

        if (def.RuleTypes.Contains("merge_queue", StringComparer.Ordinal))
        {
            ValidateMergeQueueRule(def, prefix, errors);
        }

        if (!def.RuleTypes.Contains("required_status_checks", StringComparer.Ordinal))
        {
            errors.Add($"{prefix}: branch ruleset must include a 'required_status_checks' rule.");
            return;
        }

        if (def.StrictStatusChecks is not true)
        {
            errors.Add($"{prefix}: 'strict_required_status_checks_policy' must be true.");
        }

        if (def.RequiredStatusChecks.Count == 0)
        {
            errors.Add($"{prefix}: 'required_status_checks' must not be empty.");
            return;
        }

        var seenContexts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in def.RequiredStatusChecks)
        {
            if (string.IsNullOrWhiteSpace(check.Context))
            {
                errors.Add($"{prefix}: required status check entry has an empty 'context'.");
                continue;
            }

            if (!seenContexts.Add(check.Context))
            {
                errors.Add($"{prefix}: duplicate required status check context '{check.Context}'.");
            }

            if (check.IntegrationId != GitHubActionsIntegrationId)
            {
                errors.Add($"{prefix}: status check '{check.Context}' must pin 'integration_id' to {GitHubActionsIntegrationId} (GitHub Actions).");
            }
        }

        if (!seenContexts.Contains(CiGateCheckName) || !seenContexts.Contains(ConflictCheckName))
        {
            errors.Add($"{prefix}: branch ruleset must require '{CiGateCheckName}' and '{ConflictCheckName}'.");
        }
    }

    private static void ValidateMergeQueueRule(RulesetDefinition def, string prefix, List<string> errors)
    {
        if (def.IncludeRefs.Any(r => r.Contains('*', StringComparison.Ordinal)))
        {
            errors.Add($"{prefix}: 'merge_queue' cannot target wildcard ref patterns ('*').");
        }

        if (def.MergeQueue is null)
        {
            return;
        }

        var mq = def.MergeQueue;
        if (mq.MergeMethod is not ("SQUASH" or "MERGE" or "REBASE"))
        {
            errors.Add($"{prefix}: 'merge_queue.parameters.merge_method' must be 'SQUASH', 'MERGE', or 'REBASE' (found '{mq.MergeMethod}').");
        }

        if (mq.GroupingStrategy is not ("ALLGREEN" or "HEADGREEN"))
        {
            errors.Add($"{prefix}: 'merge_queue.parameters.grouping_strategy' must be 'ALLGREEN' or 'HEADGREEN' (found '{mq.GroupingStrategy}').");
        }

        if (mq.CheckResponseTimeoutMinutes is null or < 1 or > 360)
        {
            errors.Add($"{prefix}: 'merge_queue.parameters.check_response_timeout_minutes' must be between 1 and 360.");
        }

        if (mq.MinEntriesToMerge is null or < 1 or > 100 ||
            mq.MaxEntriesToMerge is null or < 1 or > 100 ||
            mq.MaxEntriesToBuild is null or < 1 or > 100 ||
            mq.MinEntriesToMerge > mq.MaxEntriesToMerge ||
            mq.MaxEntriesToMerge > mq.MaxEntriesToBuild)
        {
            errors.Add($"{prefix}: 'merge_queue' entry bounds must satisfy 1 <= min_entries_to_merge <= max_entries_to_merge <= max_entries_to_build <= 100.");
        }

        if (mq.MinEntriesToMergeWaitMinutes is null or < 0 or > 360)
        {
            errors.Add($"{prefix}: 'merge_queue.parameters.min_entries_to_merge_wait_minutes' must be between 0 and 360.");
        }
    }
}
