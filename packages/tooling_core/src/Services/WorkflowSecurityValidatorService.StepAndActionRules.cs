using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class WorkflowSecurityValidatorService
{
    [GeneratedRegex(@"^[0-9a-fA-F]{40}$")]
    private static partial Regex CommitShaRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*/[A-Za-z0-9._-]+(?:/[A-Za-z0-9._-]+)*$")]
    private static partial Regex ValidActionSlugRegex();

    [GeneratedRegex(@"^docker://[A-Za-z0-9][A-Za-z0-9._/-]*(?::[A-Za-z0-9._-]+)?@sha256:[0-9a-fA-F]{64}$")]
    private static partial Regex DockerSha256DigestRegex();

    [GeneratedRegex(@"^\$\{\{\s*github\.repository\s*\}\}$")]
    private static partial Regex SelfRepositoryExpressionRegex();

    [GeneratedRegex(@"\$\{\{.*?\}\}")]
    private static partial Regex GitHubExpressionRegex();

    [GeneratedRegex(@"\|\|\s*(?:true\b|:(?:\s|$|;|\))|exit\s+0\b|echo\b|printf\b)")]
    private static partial Regex MaskedShellErrorRegex();

    [GeneratedRegex(@"\$\{\{\s*(?:inputs\.|github\.event\.inputs\.)")]
    private static partial Regex DirectInputsInterpolationRegex();

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

    private static string StripShellComment(string line) =>
        YamlPrimitives.StripInlineComment(line);

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
}
