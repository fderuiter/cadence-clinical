using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cadence.Cli.Services;

/// <summary>
/// Validates declarative GitHub Repository Rulesets (<c>.github/rulesets/*.json</c>) against active GitHub Actions
/// workflow definitions (<c>.github/workflows/*.yml</c>) and reconciles them to GitHub via <c>gh api</c>.
/// </summary>
public static partial class RulesetService
{
    /// <summary>GitHub App integration ID for GitHub Actions status checks.</summary>
    public const long GitHubActionsIntegrationId = 15368; // deid-ignore: GitHub Actions app integration_id

    /// <summary>The aggregated pull-request validation gate job key in <c>.github/workflows/ci.yml</c>.</summary>
    public const string CiGateJobKey = "ci-gate";

    /// <summary>The status check display name emitted by <see cref="CiGateJobKey"/>.</summary>
    public const string CiGateCheckName = "CI Gate";

    /// <summary>The merge-conflict status check display name emitted by <c>.github/workflows/conflict-check.yml</c>.</summary>
    public const string ConflictCheckName = "Check for Merge Conflicts";

    private const string Tag = "[RULESETS]";

    private static readonly string[] RequiredRulesetFiles =
    [
        "main.json",
        "release_branches.json",
        "version_tags.json",
    ];

    private static readonly HashSet<string> ExcludedPrOrchestrationJobKeys = new(StringComparer.Ordinal)
    {
        "classify",
        "automerge",
    };

    /// <summary>Outcome of a child command invocation (<c>gh</c> or <c>git</c>).</summary>
    public sealed record ProcessOutcome(int ExitCode, string Stdout, string Stderr);

    /// <summary>Delegate used to execute external CLI commands so unit tests can run hermetically.</summary>
    public delegate ProcessOutcome ProcessRunner(string workingDirectory, string fileName, IReadOnlyList<string> arguments);

    /// <summary>One entry inside <c>required_status_checks.parameters.required_status_checks</c>.</summary>
    public sealed record StatusCheckEntry(string Context, long? IntegrationId);

    /// <summary>Parsed representation of one declarative ruleset JSON file.</summary>
    public sealed record RulesetDefinition(
        string FileName,
        string FullPath,
        string Name,
        string Target,
        string Enforcement,
        IReadOnlyList<string> IncludeRefs,
        IReadOnlyList<string> RuleTypes,
        bool HasBypassActors,
        int? RequiredApprovingReviews,
        bool RequireCodeOwnerReview,
        bool? StrictStatusChecks,
        IReadOnlyList<StatusCheckEntry> RequiredStatusChecks);

    private sealed record WorkflowJobInfo(
        string WorkflowFile,
        bool OnPullRequest,
        string Key,
        string Name,
        string? IfCondition,
        string? Uses,
        bool HasMatrix,
        IReadOnlyList<string> Needs,
        IReadOnlyDictionary<string, string> StepEnv,
        string StepRun);

    /// <summary>
    /// Validates <c>.github/rulesets/*.json</c>, strict status check policy, <c>CI Gate</c> upstream job coverage,
    /// and workflow check-context parity. Returns <c>0</c> when valid and <c>1</c> when any check fails.
    /// </summary>
    public static int Validate(string? repoRoot = null, TextWriter? output = null)
    {
        repoRoot ??= QualityGateOrchestrator.FindRepoRoot();
        output ??= Console.Out;

        var errors = new List<string>();
        var definitions = LoadAndValidateRulesetFiles(repoRoot, errors);
        var workflowJobs = LoadAndValidateWorkflows(repoRoot, errors);

        if (definitions.Count > 0 && workflowJobs.Count > 0)
        {
            ValidateWorkflowParity(definitions, workflowJobs, errors);
            ValidateCiGateCoverage(workflowJobs, errors);
        }

        if (errors.Count > 0)
        {
            foreach (string error in errors)
            {
                output.WriteLine($"{Tag} [ERROR] {error}");
            }

            return 1;
        }

        output.WriteLine($"{Tag} ✔ Validated {definitions.Count} declarative rulesets and CI Gate workflow parity.");
        return 0;
    }

    /// <summary>
    /// Validates declarative rulesets and either previews (<paramref name="dryRun"/>) or reconciles them to GitHub
    /// Repository Rulesets using <c>gh api</c>. Fails with exit code <c>1</c> on validation or API errors.
    /// </summary>
    public static int Sync(
        string? repoRoot = null,
        bool dryRun = false,
        string? repoOverride = null,
        ProcessRunner? runner = null,
        Func<string, string?>? environment = null,
        TextWriter? output = null)
    {
        repoRoot ??= QualityGateOrchestrator.FindRepoRoot();
        runner ??= DefaultProcessRunner;
        environment ??= Environment.GetEnvironmentVariable;
        output ??= Console.Out;

        var errors = new List<string>();
        var definitions = LoadAndValidateRulesetFiles(repoRoot, errors);
        var workflowJobs = LoadAndValidateWorkflows(repoRoot, errors);
        if (definitions.Count > 0 && workflowJobs.Count > 0)
        {
            ValidateWorkflowParity(definitions, workflowJobs, errors);
            ValidateCiGateCoverage(workflowJobs, errors);
        }

        if (errors.Count > 0)
        {
            foreach (string error in errors)
            {
                output.WriteLine($"{Tag} [ERROR] {error}");
            }

            return 1;
        }

        string? repo = ResolveRepository(repoRoot, repoOverride, runner, environment);
        if (repo is null)
        {
            output.WriteLine($"{Tag} [ERROR] Could not determine target GitHub repository (expected 'owner/repo' via --repo, GITHUB_REPOSITORY, or git remote origin).");
            return 1;
        }

        if (dryRun)
        {
            WriteDryRunPreview(repoRoot, repo, definitions, output);
            return 0;
        }

        return ApplyRulesetsViaGhApi(repoRoot, repo, definitions, runner, output);
    }

    /// <summary>Extracts <c>owner/repo</c> from a GitHub remote URL or <c>owner/repo</c> identifier.</summary>
    public static bool TryParseRepository(string? rawValue, out string repository)
    {
        repository = "";
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        string trimmed = rawValue.Trim();
        if (OwnerRepoRegex().IsMatch(trimmed))
        {
            repository = trimmed;
            return true;
        }

        var match = GitRemoteRepoRegex().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        string owner = match.Groups[1].Value;
        string name = match.Groups[2].Value;
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (owner.Length == 0 || name.Length == 0)
        {
            return false;
        }

        string candidate = $"{owner}/{name}";
        if (!OwnerRepoRegex().IsMatch(candidate))
        {
            return false;
        }

        repository = candidate;
        return true;
    }

    private static string? ResolveRepository(
        string repoRoot,
        string? repoOverride,
        ProcessRunner runner,
        Func<string, string?> environment)
    {
        if (!string.IsNullOrWhiteSpace(repoOverride))
        {
            return TryParseRepository(repoOverride, out string explicitRepo) ? explicitRepo : null;
        }

        string? envRepo = environment("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(envRepo))
        {
            return TryParseRepository(envRepo, out string parsedEnvRepo) ? parsedEnvRepo : null;
        }

        var remoteOutcome = runner(repoRoot, "git", ["remote", "get-url", "origin"]);
        if (remoteOutcome.ExitCode == 0 && TryParseRepository(remoteOutcome.Stdout, out string remoteRepo))
        {
            return remoteRepo;
        }

        return null;
    }

    private static void WriteDryRunPreview(
        string repoRoot,
        string repo,
        IReadOnlyList<RulesetDefinition> definitions,
        TextWriter output)
    {
        output.WriteLine($"{Tag} Dry-run reconciliation preview for '{repo}':");
        foreach (var def in definitions)
        {
            string relPath = Path.GetRelativePath(repoRoot, def.FullPath).Replace('\\', '/');
            string refs = string.Join(", ", def.IncludeRefs);
            string checks = def.RequiredStatusChecks.Count == 0
                ? "none"
                : string.Join(", ", def.RequiredStatusChecks.Select(c => $"{c.Context} (integration_id={c.IntegrationId})"));
            output.WriteLine($"  - {def.Name} ({relPath}): target={def.Target}, enforcement={def.Enforcement}, refs=[{refs}], checks=[{checks}]");
            output.WriteLine($"    gh api repos/{repo}/rulesets (PUT existing id or POST --input {relPath})");
        }

        output.WriteLine($"{Tag} ✔ Dry-run complete: {definitions.Count} rulesets validated for {repo} (no GitHub API mutations performed).");
    }

    private static int ApplyRulesetsViaGhApi(
        string repoRoot,
        string repo,
        IReadOnlyList<RulesetDefinition> definitions,
        ProcessRunner runner,
        TextWriter output)
    {
        var listOutcome = runner(repoRoot, "gh", ["api", "--paginate", $"repos/{repo}/rulesets?per_page=100"]);
        if (listOutcome.ExitCode != 0)
        {
            output.WriteLine($"{Tag} [ERROR] Failed to fetch existing rulesets from 'repos/{repo}/rulesets' (exit code {listOutcome.ExitCode}).");
            WriteProcessDetails(listOutcome, output);
            output.WriteLine($"{Tag} Ensure 'gh' is authenticated with a token granting 'Administration: write' on '{repo}'.");
            return 1;
        }

        if (!TryParseExistingRulesets(listOutcome.Stdout, out var existingByName, out string? parseError))
        {
            output.WriteLine($"{Tag} [ERROR] Could not parse GitHub rulesets API response: {parseError}");
            return 1;
        }

        foreach (var def in definitions)
        {
            string relPath = Path.GetRelativePath(repoRoot, def.FullPath).Replace('\\', '/');
            bool exists = existingByName.TryGetValue(def.Name, out long existingId);
            string method = exists ? "PUT" : "POST";
            string endpoint = exists ? $"repos/{repo}/rulesets/{existingId}" : $"repos/{repo}/rulesets";

            var syncOutcome = runner(repoRoot, "gh", ["api", "--method", method, endpoint, "--input", def.FullPath]);
            if (syncOutcome.ExitCode != 0)
            {
                output.WriteLine($"{Tag} [ERROR] Failed to {method} ruleset '{def.Name}' from '{relPath}' to '{endpoint}' (exit code {syncOutcome.ExitCode}).");
                WriteProcessDetails(syncOutcome, output);
                return 1;
            }

            string action = exists ? $"updated id {existingId}" : "created";
            output.WriteLine($"{Tag} ✔ Reconciled ruleset '{def.Name}' ({action}) on '{repo}'.");
        }

        return 0;
    }

    private static void WriteProcessDetails(ProcessOutcome outcome, TextWriter output)
    {
        if (!string.IsNullOrWhiteSpace(outcome.Stderr))
        {
            output.WriteLine($"  stderr: {outcome.Stderr.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(outcome.Stdout))
        {
            output.WriteLine($"  stdout: {outcome.Stdout.Trim()}");
        }
    }

    private static bool TryParseExistingRulesets(
        string json,
        out Dictionary<string, long> rulesetsByName,
        out string? error)
    {
        rulesetsByName = new Dictionary<string, long>(StringComparer.Ordinal);
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty response body";
            return false;
        }

        try
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(json);
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { AllowMultipleValues = true });
            bool parsedAnyArray = false;

            while (reader.Read())
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    error = $"expected JSON array of rulesets, got {doc.RootElement.ValueKind}";
                    return false;
                }

                parsedAnyArray = true;
                CollectRulesetIds(doc.RootElement, rulesetsByName);
            }

            if (!parsedAnyArray)
            {
                error = "no JSON array found in response";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void CollectRulesetIds(JsonElement arrayElement, Dictionary<string, long> rulesetsByName)
    {
        foreach (JsonElement item in arrayElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Array)
            {
                CollectRulesetIds(item, rulesetsByName);
                continue;
            }

            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("name", out JsonElement nameEl)
                && nameEl.ValueKind == JsonValueKind.String
                && item.TryGetProperty("id", out JsonElement idEl)
                && idEl.TryGetInt64(out long id)
                && nameEl.GetString() is { Length: > 0 } name)
            {
                rulesetsByName[name] = id;
            }
        }
    }

    private static List<RulesetDefinition> LoadAndValidateRulesetFiles(string repoRoot, List<string> errors)
    {
        var definitions = new List<RulesetDefinition>();
        string rulesetsDir = Path.Combine(repoRoot, ".github", "rulesets");
        if (!Directory.Exists(rulesetsDir))
        {
            errors.Add("Directory '.github/rulesets' does not exist.");
            return definitions;
        }

        foreach (string requiredFile in RequiredRulesetFiles)
        {
            if (!File.Exists(Path.Combine(rulesetsDir, requiredFile)))
            {
                errors.Add($"Required ruleset file '.github/rulesets/{requiredFile}' is missing.");
            }
        }

        string[] files = Directory.GetFiles(rulesetsDir, "*.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (files.Length == 0)
        {
            errors.Add("No '*.json' ruleset files found in '.github/rulesets'.");
            return definitions;
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string fullPath in files)
        {
            var parsed = ParseSingleRulesetFile(fullPath, errors);
            if (parsed is null)
            {
                continue;
            }

            if (!seenNames.Add(parsed.Name))
            {
                errors.Add($".github/rulesets/{parsed.FileName}: duplicate ruleset name '{parsed.Name}'.");
            }

            ValidateRulesetPolicy(parsed, errors);
            definitions.Add(parsed);
        }

        return definitions;
    }

    private static RulesetDefinition? ParseSingleRulesetFile(string fullPath, List<string> errors)
    {
        string fileName = Path.GetFileName(fullPath);
        string prefix = $".github/rulesets/{fileName}";

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(fullPath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            errors.Add($"{prefix}: invalid JSON ({ex.Message}).");
            return null;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{prefix}: root JSON value must be an object.");
                return null;
            }

            string name = GetStringProperty(root, "name") ?? "";
            string target = GetStringProperty(root, "target") ?? "";
            string enforcement = GetStringProperty(root, "enforcement") ?? "";

            var includeRefs = ReadIncludeRefs(root);
            bool hasBypassActors = root.TryGetProperty("bypass_actors", out JsonElement bypassEl)
                && bypassEl.ValueKind == JsonValueKind.Array
                && bypassEl.GetArrayLength() > 0;

            var ruleTypes = new List<string>();
            int? requiredReviews = null;
            bool requireCodeOwners = false;
            bool? strictPolicy = null;
            var statusChecks = new List<StatusCheckEntry>();

            if (root.TryGetProperty("rules", out JsonElement rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement rule in rulesEl.EnumerateArray())
                {
                    if (rule.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string? type = GetStringProperty(rule, "type");
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        continue;
                    }

                    ruleTypes.Add(type);
                    if (type == "pull_request" && rule.TryGetProperty("parameters", out JsonElement prParams) && prParams.ValueKind == JsonValueKind.Object)
                    {
                        if (prParams.TryGetProperty("required_approving_review_count", out JsonElement countEl) && countEl.TryGetInt32(out int count))
                        {
                            requiredReviews = count;
                        }

                        if (prParams.TryGetProperty("require_code_owner_review", out JsonElement ownerEl) && ownerEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            requireCodeOwners = ownerEl.GetBoolean();
                        }
                    }
                    else if (type == "required_status_checks")
                    {
                        ReadStatusChecksRule(rule, prefix, ref strictPolicy, statusChecks, errors);
                    }
                }
            }

            return new RulesetDefinition(
                fileName,
                fullPath,
                name,
                target,
                enforcement,
                includeRefs,
                ruleTypes,
                hasBypassActors,
                requiredReviews,
                requireCodeOwners,
                strictPolicy,
                statusChecks);
        }
    }

    private static void ReadStatusChecksRule(
        JsonElement rule,
        string prefix,
        ref bool? strictPolicy,
        List<StatusCheckEntry> statusChecks,
        List<string> errors)
    {
        if (!rule.TryGetProperty("parameters", out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{prefix}: 'required_status_checks' rule is missing 'parameters'.");
            return;
        }

        if (parameters.TryGetProperty("strict_required_status_checks_policy", out JsonElement strictEl)
            && strictEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            strictPolicy = strictEl.GetBoolean();
        }

        if (!parameters.TryGetProperty("required_status_checks", out JsonElement checksEl) || checksEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{prefix}: 'required_status_checks.parameters.required_status_checks' must be an array.");
            return;
        }

        foreach (JsonElement check in checksEl.EnumerateArray())
        {
            if (check.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{prefix}: every entry in 'required_status_checks' must be an object.");
                continue;
            }

            string context = GetStringProperty(check, "context") ?? "";
            long? integrationId = null;
            if (check.TryGetProperty("integration_id", out JsonElement intEl) && intEl.TryGetInt64(out long parsedId))
            {
                integrationId = parsedId;
            }

            statusChecks.Add(new StatusCheckEntry(context, integrationId));
        }
    }

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
        else if (def.Target != "tag")
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

        if (def.HasBypassActors)
        {
            errors.Add($"{prefix}: version tag protections must not configure 'bypass_actors'.");
        }
    }

    private static void ValidateBranchRuleset(RulesetDefinition def, string prefix, List<string> errors)
    {
        if (!def.RuleTypes.Contains("pull_request", StringComparer.Ordinal))
        {
            errors.Add($"{prefix}: branch ruleset must include a 'pull_request' rule.");
        }
        else if (def.RequiredApprovingReviews is null or < 1 || !def.RequireCodeOwnerReview)
        {
            errors.Add($"{prefix}: 'pull_request' rule must require at least 1 approving review and CODEOWNERS review.");
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

    private static List<WorkflowJobInfo> LoadAndValidateWorkflows(string repoRoot, List<string> errors)
    {
        var allJobs = new List<WorkflowJobInfo>();
        string workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
        if (!Directory.Exists(workflowsDir))
        {
            errors.Add("Directory '.github/workflows' does not exist.");
            return allJobs;
        }

        string ciPath = Path.Combine(workflowsDir, "ci.yml");
        if (!File.Exists(ciPath))
        {
            errors.Add("Required workflow '.github/workflows/ci.yml' is missing.");
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

    private static void ValidateWorkflowParity(
        IReadOnlyList<RulesetDefinition> definitions,
        IReadOnlyList<WorkflowJobInfo> workflowJobs,
        List<string> errors)
    {
        var eligiblePrCheckNames = workflowJobs
            .Where(IsEligibleBranchStatusCheckJob)
            .Select(j => j.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var def in definitions.Where(d => d.Target == "branch"))
        {
            string prefix = $".github/rulesets/{def.FileName}";
            foreach (string context in def.RequiredStatusChecks
                .Select(check => check.Context)
                .Where(ctx => !string.IsNullOrWhiteSpace(ctx) && !eligiblePrCheckNames.Contains(ctx)))
            {
                errors.Add(
                    $"{prefix}: required status check '{context}' does not match any unconditional pull-request job in '.github/workflows/*.yml'.");
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
        ValidateAutomergeWaitsOnCiGate(ciJobs, errors);
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

            bool isPathConditionalOnPr = job.IfCondition?.Contains("needs.classify.outputs.", StringComparison.Ordinal) is true;
            if (!isPathConditionalOnPr && matchingEnv.Any(envVar => AllowsSkippedForEnvVar(ciGate.StepRun, envVar)))
            {
                errors.Add($".github/workflows/ci.yml: '{CiGateJobKey}' must fail when required job '{job.Key}' is skipped.");
            }
        }
    }

    private static bool AllowsSkippedForEnvVar(string stepRun, string envVar)
    {
        foreach (string line in stepRun.Split('\n'))
        {
            if ((line.Contains($"${envVar}", StringComparison.Ordinal) || line.Contains($"${{{envVar}}}", StringComparison.Ordinal))
                && line.Contains("skipped", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateAutomergeWaitsOnCiGate(IReadOnlyList<WorkflowJobInfo> ciJobs, List<string> errors)
    {
        var automerge = ciJobs.FirstOrDefault(j => j.Key == "automerge");
        if (automerge is null)
        {
            return;
        }

        if (!automerge.Needs.Contains(CiGateJobKey, StringComparer.Ordinal))
        {
            errors.Add($".github/workflows/ci.yml: 'automerge' must include '{CiGateJobKey}' in 'needs'.");
        }

        if (automerge.IfCondition is null || !automerge.IfCondition.Contains($"needs.{CiGateJobKey}.result == 'success'", StringComparison.Ordinal))
        {
            errors.Add($".github/workflows/ci.yml: 'automerge' must check \"needs.{CiGateJobKey}.result == 'success'\" in 'if'.");
        }
    }

    private static bool IsPushOnlyJob(WorkflowJobInfo job) =>
        job.IfCondition is not null && RequiresPushEvent(job.IfCondition);

    private static bool RequiresPushEvent(string expression)
    {
        string trimmed = UnwrapExpression(expression);
        if (trimmed.Length == 0)
        {
            return false;
        }

        var orClauses = SplitTopLevelLogical(trimmed, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(RequiresPushEvent);
        }

        var andClauses = SplitTopLevelLogical(trimmed, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(RequiresPushEvent);
        }

        string normalized = WhitespaceRegex().Replace(trimmed, " ");
        return normalized is "github.event_name == 'push'"
            or "github.event_name == \"push\""
            or "'push' == github.event_name"
            or "\"push\" == github.event_name";
    }

    private static string UnwrapExpression(string expression)
    {
        string current = expression.Trim();
        if (current.StartsWith("${{", StringComparison.Ordinal) && current.EndsWith("}}", StringComparison.Ordinal))
        {
            current = current[3..^2].Trim();
        }

        while (current.Length >= 2 && current[0] == '(' && current[^1] == ')' && HasBalancedOuterParentheses(current))
        {
            current = current[1..^1].Trim();
        }

        return current;
    }

    private static bool HasBalancedOuterParentheses(string text)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            UpdateQuoteAndDepth(text[i], ref quote, ref depth);
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

        int index = 0;
        while (index < text.Length)
        {
            UpdateQuoteAndDepth(text[index], ref quote, ref depth);
            if (quote == '\0' && depth == 0 && index + op.Length <= text.Length && text.AsSpan(index, op.Length).SequenceEqual(op))
            {
                parts.Add(text[start..index].Trim());
                index += op.Length;
                start = index;
            }
            else
            {
                index++;
            }
        }

        parts.Add(text[start..].Trim());
        return parts;
    }

    private static void UpdateQuoteAndDepth(char ch, ref char quote, ref int depth)
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

    private static bool IsAlwaysOnlyCondition(string? condition) =>
        condition is not null && NormalizeCondition(condition) is "always()" or "${{ always() }}";

    private static string NormalizeCondition(string condition) =>
        WhitespaceRegex().Replace(condition, " ").Trim();

    private static List<WorkflowJobInfo> ParseWorkflowJobs(string workflowFile, string[] lines)
    {
        bool onPullRequest = ParseOnPullRequest(lines);
        var jobs = new List<WorkflowJobInfo>();

        int jobsStart = Array.FindIndex(lines, l => l.TrimEnd() == "jobs:");
        if (jobsStart < 0)
        {
            return jobs;
        }

        int i = jobsStart + 1;
        while (i < lines.Length)
        {
            string line = lines[i];
            if (line.Length > 0 && line[0] != ' ' && line[0] != '#')
            {
                break;
            }

            var jobMatch = JobHeaderRegex().Match(line);
            if (!jobMatch.Success)
            {
                i++;
                continue;
            }

            string jobKey = jobMatch.Groups[1].Value;
            i++;
            int jobEnd = i;
            while (jobEnd < lines.Length)
            {
                string next = lines[jobEnd];
                if ((next.Length > 0 && next[0] != ' ' && next[0] != '#') || JobHeaderRegex().IsMatch(next))
                {
                    break;
                }

                jobEnd++;
            }

            jobs.Add(BuildJobInfo(workflowFile, onPullRequest, jobKey, lines[i..jobEnd]));
            i = jobEnd;
        }

        return jobs;
    }

    private static bool ParseOnPullRequest(string[] lines)
    {
        bool inOnBlock = false;
        foreach (string raw in lines)
        {
            string line = StripYamlComment(raw);
            if (line.TrimEnd() == "jobs:")
            {
                break;
            }

            if (line.StartsWith("on:", StringComparison.Ordinal))
            {
                inOnBlock = true;
                if (PullRequestTriggerRegex().IsMatch(line))
                {
                    return true;
                }

                continue;
            }

            if (inOnBlock)
            {
                if (line.Length > 0 && line[0] != ' ')
                {
                    inOnBlock = false;
                }
                else if (PullRequestTriggerRegex().IsMatch(line))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static WorkflowJobInfo BuildJobInfo(
        string workflowFile,
        bool onPullRequest,
        string jobKey,
        string[] bodyLines)
    {
        string? name = null;
        string? ifCond = null;
        string? uses = null;
        bool hasMatrix = false;
        var needs = new List<string>();
        var stepEnv = new Dictionary<string, string>(StringComparer.Ordinal);
        var stepRuns = new List<string>();

        int i = 0;
        while (i < bodyLines.Length)
        {
            string line = bodyLines[i];
            if (line.StartsWith("      matrix:", StringComparison.Ordinal))
            {
                hasMatrix = true;
                i++;
                continue;
            }

            if (line.TrimEnd() == "    steps:")
            {
                ParseStepsBlock(bodyLines[(i + 1)..], stepEnv, stepRuns);
                break;
            }

            var propMatch = JobPropertyRegex().Match(line);
            if (!propMatch.Success)
            {
                i++;
                continue;
            }

            string prop = propMatch.Groups[1].Value;
            string rest = StripYamlComment(propMatch.Groups[2].Value).Trim();
            i++;

            if (prop == "name")
            {
                name = UnquoteYaml(rest);
            }
            else if (prop == "uses")
            {
                uses = UnquoteYaml(rest);
            }
            else if (prop == "if")
            {
                ifCond = ReadScalarOrBlock(rest, bodyLines, ref i, minIndent: 6);
            }
            else if (prop == "needs")
            {
                ReadNeedsList(rest, bodyLines, ref i, needs);
            }
        }

        return new WorkflowJobInfo(
            workflowFile,
            onPullRequest,
            jobKey,
            string.IsNullOrWhiteSpace(name) ? jobKey : name,
            ifCond,
            uses,
            hasMatrix,
            needs,
            stepEnv,
            string.Join('\n', stepRuns));
    }

    private static void ParseStepsBlock(
        string[] stepLines,
        Dictionary<string, string> stepEnv,
        List<string> stepRuns)
    {
        int i = 0;
        bool inEnv = false;
        while (i < stepLines.Length)
        {
            string line = stepLines[i];
            if (line.StartsWith("      - ", StringComparison.Ordinal))
            {
                inEnv = false;
            }

            if (line.TrimEnd() == "        env:")
            {
                inEnv = true;
                i++;
                continue;
            }

            if (inEnv)
            {
                if (line.StartsWith("          ", StringComparison.Ordinal))
                {
                    var envMatch = StepEnvEntryRegex().Match(line);
                    if (envMatch.Success)
                    {
                        stepEnv[envMatch.Groups[1].Value] = envMatch.Groups[2].Value.Trim();
                    }

                    i++;
                    continue;
                }

                inEnv = false;
            }

            var runMatch = StepRunRegex().Match(line);
            if (runMatch.Success)
            {
                string inline = runMatch.Groups[1].Value.Trim();
                i++;
                stepRuns.Add(ReadScalarOrBlock(inline, stepLines, ref i, minIndent: 10));
                continue;
            }

            i++;
        }
    }

    private static string ReadScalarOrBlock(string inlineValue, string[] lines, ref int index, int minIndent)
    {
        if (inlineValue is not ("|" or "|-" or ">" or ">-"))
        {
            return UnquoteYaml(inlineValue);
        }

        string indentPrefix = new(' ', minIndent);
        var collected = new List<string>();
        while (index < lines.Length)
        {
            string line = lines[index];
            if (line.Trim().Length == 0)
            {
                collected.Add("");
                index++;
                continue;
            }

            if (!line.StartsWith(indentPrefix, StringComparison.Ordinal))
            {
                break;
            }

            collected.Add(line[minIndent..]);
            index++;
        }

        string separator = inlineValue.StartsWith('>') ? " " : "\n";
        return string.Join(separator, collected).Trim();
    }

    private static void ReadNeedsList(string inlineValue, string[] lines, ref int index, List<string> needs)
    {
        if (inlineValue.StartsWith('[') && inlineValue.EndsWith(']'))
        {
            AddCommaSeparatedNeeds(inlineValue[1..^1], needs);
            return;
        }

        if (inlineValue.Length > 0 && inlineValue != "[")
        {
            needs.Add(UnquoteYaml(inlineValue));
            return;
        }

        var buffer = new StringBuilder(inlineValue);
        while (index < lines.Length)
        {
            string raw = StripYamlComment(lines[index]);
            if (raw.Trim().Length == 0)
            {
                index++;
                continue;
            }

            if (!raw.StartsWith("      ", StringComparison.Ordinal) && !raw.StartsWith("    - ", StringComparison.Ordinal))
            {
                break;
            }

            string trimmed = raw.Trim();
            index++;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                needs.Add(UnquoteYaml(trimmed[2..]));
                continue;
            }

            buffer.Append(' ').Append(trimmed);
            if (trimmed.EndsWith(']'))
            {
                break;
            }
        }

        string combined = buffer.ToString().Trim().TrimStart('[').TrimEnd(']');
        AddCommaSeparatedNeeds(combined, needs);
    }

    private static void AddCommaSeparatedNeeds(string csv, List<string> needs)
    {
        foreach (string part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string cleaned = UnquoteYaml(part);
            if (cleaned.Length > 0)
            {
                needs.Add(cleaned);
            }
        }
    }

    private static List<string> ReadIncludeRefs(JsonElement root)
    {
        var refs = new List<string>();
        if (root.TryGetProperty("conditions", out JsonElement cond)
            && cond.ValueKind == JsonValueKind.Object
            && cond.TryGetProperty("ref_name", out JsonElement refName)
            && refName.ValueKind == JsonValueKind.Object
            && refName.TryGetProperty("include", out JsonElement inc)
            && inc.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in inc.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                {
                    refs.Add(value);
                }
            }
        }

        return refs;
    }

    private static string? GetStringProperty(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static string StripYamlComment(string line)
    {
        int hashIndex = line.IndexOf(" #", StringComparison.Ordinal);
        if (hashIndex >= 0)
        {
            return line[..hashIndex];
        }

        return line.TrimStart().StartsWith('#') ? "" : line;
    }

    private static string UnquoteYaml(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2
            && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static ProcessOutcome DefaultProcessRunner(string workingDirectory, string fileName, IReadOnlyList<string> arguments)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return new ProcessOutcome(1, "", $"Could not start process '{fileName}'.");
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = stderrTask.GetAwaiter().GetResult();
            process.WaitForExit();
            return new ProcessOutcome(process.ExitCode, stdout, stderr);
        }
        catch (Win32Exception ex)
        {
            return new ProcessOutcome(1, "", $"Could not execute '{fileName}': {ex.Message}");
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OwnerRepoRegex();

    [GeneratedRegex(@"github\.com[:/]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitRemoteRepoRegex();

    [GeneratedRegex(@"^  ([A-Za-z0-9_-]+):\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JobHeaderRegex();

    [GeneratedRegex(@"^    ([A-Za-z0-9_-]+):(.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JobPropertyRegex();

    [GeneratedRegex(@"^          ([A-Za-z0-9_]+):\s*(.+)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StepEnvEntryRegex();

    [GeneratedRegex(@"^        run:\s*(.*)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StepRunRegex();

    [GeneratedRegex(@"\bpull_request\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PullRequestTriggerRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();
}
