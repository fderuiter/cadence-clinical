using System;
using System.Collections.Generic;
using System.IO;

using Cadence.ToolingCore.Primitives;
using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

/// <summary>
/// Validates declarative GitHub Repository Rulesets (<c>.github/rulesets/*.json</c>) against active GitHub Actions
/// workflow definitions (<c>.github/workflows/*.yml</c>) and reconciles them to GitHub via <c>gh api</c>.
/// </summary>
public static partial class RulesetService
{
    /// <summary>GitHub App integration ID for GitHub Actions status checks.</summary>
    public const long GitHubActionsIntegrationId = 15368; // deid-ignore: GitHub Actions app integration_id

    /// <summary>GitHub RepositoryRole actor ID for Repository Admins (release administrators).</summary>
    public const long RepositoryAdminRoleId = 5;

    /// <summary>The aggregated pull-request validation gate job key in <c>.github/workflows/ci.yml</c>.</summary>
    public const string CiGateJobKey = "ci-gate";

    /// <summary>The status check display name emitted by <see cref="CiGateJobKey"/>.</summary>
    public const string CiGateCheckName = "CI Gate";

    /// <summary>The merge-conflict status check display name emitted by <c>.github/workflows/conflict-check.yml</c>.</summary>
    public const string ConflictCheckName = "Check for Merge Conflicts";

    private const string ConflictCheckWorkflowFile = "conflict-check.yml";
    private const string ConflictCheckJobKey = "conflict-check";

    /// <summary>Reconciliation profile used while hosted GitHub Actions runners are paused.</summary>
    public const string PausedActionsProfile = "paused-actions";

    /// <summary>Reconciliation profile used when hosted GitHub Actions runners are active.</summary>
    public const string ActiveActionsProfile = "active-actions";

    /// <summary>The only allowed <c>bypass_mode</c> value on branch rulesets (<c>pull_request</c>, i.e. pull-request-only).</summary>
    public const string PullRequestOnlyBypassMode = "pull_request";

    private const string Tag = "[RULESETS]";

    /// <summary>Outcome of a child command invocation (<c>gh</c> or <c>git</c>).</summary>
    public sealed record ProcessOutcome(int ExitCode, string Stdout, string Stderr);

    /// <summary>Delegate used to execute external CLI commands so unit tests can run hermetically.</summary>
    public delegate ProcessOutcome ProcessRunner(string workingDirectory, string fileName, IReadOnlyList<string> arguments);

    /// <summary>One entry inside <c>bypass_actors</c>.</summary>
    public sealed record BypassActorEntry(long? ActorId, string ActorType, string BypassMode);

    /// <summary>One entry inside <c>required_status_checks.parameters.required_status_checks</c>.</summary>
    public sealed record StatusCheckEntry(string Context, long? IntegrationId);

    /// <summary>Parsed <c>merge_queue.parameters</c> block on a branch ruleset.</summary>
    public sealed record MergeQueueConfig(
        int? CheckResponseTimeoutMinutes,
        string GroupingStrategy,
        int? MaxEntriesToBuild,
        int? MaxEntriesToMerge,
        string MergeMethod,
        int? MinEntriesToMerge,
        int? MinEntriesToMergeWaitMinutes);

    /// <summary>Parsed representation of one declarative ruleset JSON file.</summary>
    public sealed record RulesetDefinition(
        string FileName,
        string FullPath,
        string Name,
        string Target,
        string Enforcement,
        IReadOnlyList<string> IncludeRefs,
        IReadOnlyList<string> RuleTypes,
        IReadOnlyList<BypassActorEntry> BypassActors,
        int? RequiredApprovingReviews,
        bool RequireCodeOwnerReview,
        bool? StrictStatusChecks,
        IReadOnlyList<StatusCheckEntry> RequiredStatusChecks,
        MergeQueueConfig? MergeQueue)
    {
        /// <summary>True when at least one <c>bypass_actors</c> entry is configured on the ruleset.</summary>
        public bool HasBypassActors => BypassActors.Count > 0;
    }

    private sealed record PullRequestTriggerInfo(
        bool OnPullRequest,
        bool HasExplicitTypes,
        IReadOnlySet<string> Types);

    private sealed record WorkflowJobInfo(
        string WorkflowFile,
        PullRequestTriggerInfo PullRequestTrigger,
        bool OnMergeGroup,
        string Key,
        string Name,
        string? IfCondition,
        string? Uses,
        bool HasMatrix,
        IReadOnlyList<string> Needs,
        IReadOnlyDictionary<string, string> StepEnv,
        string StepRun)
    {
        public bool OnPullRequest => PullRequestTrigger.OnPullRequest;
    }

    /// <summary>Normalizes a ruleset sync profile (<c>paused-actions</c> or <c>active-actions</c>).</summary>
    public static bool TryNormalizeProfile(string? rawProfile, out string normalizedProfile)
    {
        if (string.IsNullOrWhiteSpace(rawProfile))
        {
            normalizedProfile = PausedActionsProfile;
            return true;
        }

        string candidate = rawProfile.Trim().ToLowerInvariant();
        if (candidate is PausedActionsProfile or ActiveActionsProfile)
        {
            normalizedProfile = candidate;
            return true;
        }

        normalizedProfile = string.Empty;
        return false;
    }

    /// <summary>
    /// Validates declarative <c>.github/rulesets/*.json</c>, <c>pull_request</c>-only branch bypass actors,
    /// release-admin-only signed <c>v*</c> tag rulesets, <c>merge_queue</c> policy, strict status check policy,
    /// <c>CI Gate</c> upstream job coverage, fail-closed <c>conflict-check.yml</c> parity, and
    /// <c>pull_request</c> / <c>merge_group</c> workflow parity. Reads staged Git index blobs when
    /// <paramref name="indexReader"/> or <paramref name="preCommit"/> is active. Returns <c>0</c> when valid and
    /// <c>1</c> when any check fails.
    /// </summary>
    public static int Validate(
        string? repoRoot = null,
        TextWriter? output = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        output ??= Console.Out;

        GitIndexReader? ownedReader = null;
        GitIndexReader? reader = indexReader
            ?? GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, null, out ownedReader);
        using (ownedReader)
        {
            var errors = new List<string>();
            var definitions = ValidateCore(repoRoot, reader, errors);

            if (errors.Count > 0)
            {
                WriteErrors(errors, output);
                return 1;
            }

            output.WriteLine($"{Tag} ✔ Validated {definitions.Count} declarative rulesets and CI Gate / merge_group workflow parity.");
            return 0;
        }
    }

    /// <summary>
    /// Validates declarative rulesets (or <c>CI Gate</c> workflow coverage when <c>.github/rulesets</c> is absent in
    /// <paramref name="dryRun"/> mode) and either previews (<paramref name="dryRun"/>) or reconciles them to GitHub
    /// Repository Rulesets using <c>gh api</c> under <paramref name="profile"/> (<c>paused-actions</c> by default, or
    /// <c>active-actions</c>). Fails with exit code <c>1</c> on validation or API errors.
    /// </summary>
    public static int Sync(
        string? repoRoot = null,
        bool dryRun = false,
        string? repoOverride = null,
        ProcessRunner? runner = null,
        Func<string, string?>? environment = null,
        TextWriter? output = null,
        string? profile = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        runner ??= DefaultProcessRunner;
        environment ??= Environment.GetEnvironmentVariable;
        output ??= Console.Out;

        if (!TryNormalizeProfile(profile, out string normalizedProfile))
        {
            output.WriteLine(
                $"{Tag} [ERROR] Unsupported ruleset profile '{profile}'. Expected '{PausedActionsProfile}' or '{ActiveActionsProfile}'.");
            return 1;
        }

        var errors = new List<string>();
        var definitions = ValidateCore(repoRoot, reader: null, errors, requireRulesetsDirectory: !dryRun);

        if (errors.Count > 0)
        {
            WriteErrors(errors, output);
            return 1;
        }

        if (dryRun && definitions.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(repoOverride) && !TryParseRepository(repoOverride, out _))
            {
                output.WriteLine($"{Tag} [ERROR] Could not determine target GitHub repository (expected 'owner/repo' via --repo, GITHUB_REPOSITORY, or git remote origin).");
                return 1;
            }

            output.WriteLine($"{Tag} ✔ Dry-run validated CI Gate workflow coverage (no declarative .github/rulesets directory present; nothing to sync).");
            return 0;
        }

        string? repo = ResolveRepository(repoRoot, repoOverride, runner, environment);
        if (repo is null)
        {
            output.WriteLine($"{Tag} [ERROR] Could not determine target GitHub repository (expected 'owner/repo' via --repo, GITHUB_REPOSITORY, or git remote origin).");
            return 1;
        }

        if (dryRun)
        {
            WriteDryRunPreview(repoRoot, repo, normalizedProfile, definitions, output);
            return 0;
        }

        return ApplyRulesetsViaGhApi(repoRoot, repo, normalizedProfile, definitions, runner, output);
    }

    private static List<RulesetDefinition> ValidateCore(
        string repoRoot,
        GitIndexReader? reader,
        List<string> errors,
        bool requireRulesetsDirectory = true)
    {
        var definitions = LoadAndValidateRulesetFiles(repoRoot, reader, errors, requireRulesetsDirectory);
        var workflowJobs = LoadAndValidateWorkflows(repoRoot, reader, errors);

        if (workflowJobs.Count > 0)
        {
            if (definitions.Count > 0)
            {
                ValidateWorkflowParity(definitions, workflowJobs, errors);
            }

            ValidateCiGateCoverage(workflowJobs, errors);
            ValidateConflictCheckAndMergePolicy(workflowJobs, errors);
        }
        else
        {
            bool useIndex = reader is { HasAuthoritativeIndex: true };
            bool ciExists = useIndex
                ? reader!.FileExists(".github/workflows/ci.yml")
                : File.Exists(Path.Combine(repoRoot, ".github", "workflows", "ci.yml"));
            if (ciExists)
            {
                errors.Add(".github/workflows/ci.yml: contains no workflow jobs.");
            }
        }

        return definitions;
    }

    private static void WriteErrors(IReadOnlyList<string> errors, TextWriter output)
    {
        foreach (string error in errors)
        {
            output.WriteLine($"{Tag} [ERROR] {error}");
        }
    }
}
