using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    private static readonly string[] RequiredRulesetFiles =
    [
        "main.json",
        "release_branches.json",
        "version_tags.json",
    ];

    private static readonly string[] RequiredConflictCheckTypes =
    [
        "opened",
        "synchronize",
        "reopened",
        "edited",
    ];

    private static readonly string[] RequiredTagRuleTypes =
    [
        "creation",
        "required_signatures",
        "deletion",
        "non_fast_forward",
    ];

    private static readonly HashSet<string> ExcludedPrOrchestrationJobKeys = new(StringComparer.Ordinal)
    {
        "classify",
    };

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
        repoRoot ??= QualityGateOrchestrator.FindRepoRoot();
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
        repoRoot ??= QualityGateOrchestrator.FindRepoRoot();
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

    /// <summary>
    /// Projects a canonical <c>.github/rulesets/*.json</c> document for <paramref name="profile"/> without reflection:
    /// <c>paused-actions</c> strips <c>required_status_checks</c> and <c>merge_queue</c> rules while runners are paused,
    /// whereas <c>active-actions</c> emits the full ruleset unchanged.
    /// </summary>
    public static string ProjectRulesetPayloadForProfile(string sourceJson, string profile)
    {
        if (string.Equals(profile, ActiveActionsProfile, StringComparison.Ordinal))
        {
            return sourceJson;
        }

        using var doc = JsonDocument.Parse(sourceJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("rules") && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    writer.WritePropertyName("rules");
                    writer.WriteStartArray();
                    foreach (JsonElement rule in prop.Value.EnumerateArray())
                    {
                        string? ruleType = rule.ValueKind == JsonValueKind.Object ? GetStringProperty(rule, "type") : null;
                        if (ruleType is "required_status_checks" or "merge_queue")
                        {
                            continue;
                        }

                        rule.WriteTo(writer);
                    }

                    writer.WriteEndArray();
                    continue;
                }

                prop.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static IReadOnlyList<string> GetProjectedRuleTypes(RulesetDefinition def, string profile) =>
        string.Equals(profile, PausedActionsProfile, StringComparison.Ordinal)
            ? [.. def.RuleTypes.Where(t => t is not ("required_status_checks" or "merge_queue"))]
            : def.RuleTypes;

    private static void WriteDryRunPreview(
        string repoRoot,
        string repo,
        string profile,
        IReadOnlyList<RulesetDefinition> definitions,
        TextWriter output)
    {
        output.WriteLine($"{Tag} Dry-run reconciliation preview for '{repo}' (profile={profile}):");
        foreach (var def in definitions)
        {
            string relPath = Path.GetRelativePath(repoRoot, def.FullPath).Replace('\\', '/');
            string refs = string.Join(", ", def.IncludeRefs);
            string rules = string.Join(", ", GetProjectedRuleTypes(def, profile));
            string checks;
            if (def.RequiredStatusChecks.Count == 0)
            {
                checks = "none";
            }
            else if (string.Equals(profile, PausedActionsProfile, StringComparison.Ordinal))
            {
                checks = $"staged for {ActiveActionsProfile} ({string.Join(", ", def.RequiredStatusChecks.Select(c => c.Context))})";
            }
            else
            {
                checks = string.Join(", ", def.RequiredStatusChecks.Select(c => $"{c.Context} (integration_id={c.IntegrationId})"));
            }
            output.WriteLine($"  - {def.Name} ({relPath}): target={def.Target}, enforcement={def.Enforcement}, refs=[{refs}], rules=[{rules}], checks=[{checks}]");
            output.WriteLine($"    gh api repos/{repo}/rulesets (PUT existing id or POST --input <{profile}:{relPath}>)");
        }

        output.WriteLine($"{Tag} ✔ Dry-run complete: {definitions.Count} rulesets validated for {repo} under profile '{profile}' (no GitHub API mutations performed).");
    }

    private static int ApplyRulesetsViaGhApi(
        string repoRoot,
        string repo,
        string profile,
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
            string payloadJson = ProjectRulesetPayloadForProfile(File.ReadAllText(def.FullPath), profile);
            string tempPayloadPath = Path.Combine(Path.GetTempPath(), $"cadence-ruleset-{Guid.NewGuid():N}-{def.FileName}");

            try
            {
                File.WriteAllText(tempPayloadPath, payloadJson);
                var syncOutcome = runner(repoRoot, "gh", ["api", "--method", method, endpoint, "--input", tempPayloadPath]);
                if (syncOutcome.ExitCode != 0)
                {
                    output.WriteLine($"{Tag} [ERROR] Failed to {method} ruleset '{def.Name}' from '{relPath}' (profile '{profile}') to '{endpoint}' (exit code {syncOutcome.ExitCode}).");
                    WriteProcessDetails(syncOutcome, output);
                    return 1;
                }
            }
            finally
            {
                if (File.Exists(tempPayloadPath))
                {
                    File.Delete(tempPayloadPath);
                }
            }

            string action = exists ? $"updated id {existingId}" : "created";
            output.WriteLine($"{Tag} ✔ Reconciled ruleset '{def.Name}' ({action}, profile={profile}) on '{repo}'.");
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

    private static List<RulesetDefinition> LoadAndValidateRulesetFiles(
        string repoRoot,
        GitIndexReader? reader,
        List<string> errors,
        bool requireRulesetsDirectory = true)
    {
        var definitions = new List<RulesetDefinition>();
        string rulesetsDir = Path.Combine(repoRoot, ".github", "rulesets");
        bool useIndex = reader is { HasAuthoritativeIndex: true };

        bool dirExists = useIndex
            ? reader!.DirectoryExists(".github/rulesets")
            : Directory.Exists(rulesetsDir);
        if (!dirExists)
        {
            if (requireRulesetsDirectory)
            {
                errors.Add("Directory '.github/rulesets' does not exist.");
            }

            return definitions;
        }

        foreach (string requiredFile in RequiredRulesetFiles)
        {
            string relRequired = $".github/rulesets/{requiredFile}";
            bool fileExists = useIndex
                ? reader!.FileExists(relRequired)
                : File.Exists(Path.Combine(rulesetsDir, requiredFile));
            if (!fileExists)
            {
                errors.Add($"Required ruleset file '{relRequired}' is missing.");
            }
        }

        string[] fileNames = useIndex
            ? reader!.Paths
                .Where(IsRulesetJsonPath)
                .Select(p => Path.GetFileName(p))
                .Where(n => !string.IsNullOrEmpty(n))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : Directory.GetFiles(rulesetsDir, "*.json", SearchOption.TopDirectoryOnly)
                .Select(p => Path.GetFileName(p))
                .Order(StringComparer.Ordinal)
                .ToArray();

        if (fileNames.Length == 0)
        {
            errors.Add("No '*.json' ruleset files found in '.github/rulesets'.");
            return definitions;
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string fileName in fileNames)
        {
            string fullPath = Path.Combine(rulesetsDir, fileName);
            var parsed = ParseSingleRulesetFile(fileName, fullPath, useIndex ? reader : null, errors);
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

    private static bool IsRulesetJsonPath(string relativePath)
    {
        const string prefix = ".github/rulesets/";
        if (!relativePath.StartsWith(prefix, StringComparison.Ordinal)
            || !relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> remainder = relativePath.AsSpan(prefix.Length);
        return remainder.Length > ".json".Length && !remainder.Contains('/');
    }

    private static RulesetDefinition? ParseSingleRulesetFile(
        string fileName,
        string fullPath,
        GitIndexReader? reader,
        List<string> errors)
    {
        string prefix = $".github/rulesets/{fileName}";

        string jsonText;
        if (reader is not null)
        {
            if (!reader.TryReadText(prefix, out jsonText))
            {
                errors.Add($"{prefix}: could not read staged ruleset blob.");
                return null;
            }
        }
        else
        {
            try
            {
                jsonText = File.ReadAllText(fullPath);
            }
            catch (IOException ex)
            {
                errors.Add($"{prefix}: invalid JSON ({ex.Message}).");
                return null;
            }
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonText);
        }
        catch (JsonException ex)
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
            var bypassActors = ReadBypassActors(root, prefix, errors);

            var ruleTypes = new List<string>();
            int? requiredReviews = null;
            bool requireCodeOwners = false;
            bool? strictPolicy = null;
            var statusChecks = new List<StatusCheckEntry>();
            MergeQueueConfig? mergeQueue = null;

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
                    else if (type == "merge_queue")
                    {
                        mergeQueue = ReadMergeQueueRule(rule, prefix, errors);
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
                bypassActors,
                requiredReviews,
                requireCodeOwners,
                strictPolicy,
                statusChecks,
                mergeQueue);
        }
    }

    private static List<BypassActorEntry> ReadBypassActors(
        JsonElement root,
        string prefix,
        List<string> errors)
    {
        var actors = new List<BypassActorEntry>();
        if (!root.TryGetProperty("bypass_actors", out JsonElement bypassEl))
        {
            return actors;
        }

        if (bypassEl.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{prefix}: 'bypass_actors' must be an array.");
            return actors;
        }

        foreach (JsonElement actorEl in bypassEl.EnumerateArray())
        {
            if (actorEl.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{prefix}: every entry in 'bypass_actors' must be an object.");
                actors.Add(new BypassActorEntry(null, "", "<invalid>"));
                continue;
            }

            long? actorId = null;
            if (actorEl.TryGetProperty("actor_id", out JsonElement idEl) && idEl.ValueKind != JsonValueKind.Null)
            {
                if (idEl.TryGetInt64(out long parsedId))
                {
                    actorId = parsedId;
                }
                else
                {
                    errors.Add($"{prefix}: 'bypass_actors.actor_id' must be an integer.");
                }
            }

            string actorType = GetStringProperty(actorEl, "actor_type") ?? "";
            string bypassMode = GetStringProperty(actorEl, "bypass_mode") ?? "";
            actors.Add(new BypassActorEntry(actorId, actorType, bypassMode));
        }

        return actors;
    }

    private static MergeQueueConfig? ReadMergeQueueRule(JsonElement rule, string prefix, List<string> errors)
    {
        if (!rule.TryGetProperty("parameters", out JsonElement parameters) || parameters.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{prefix}: 'merge_queue' rule is missing 'parameters'.");
            return null;
        }

        return new MergeQueueConfig(
            GetInt32Property(parameters, "check_response_timeout_minutes"),
            GetStringProperty(parameters, "grouping_strategy") ?? string.Empty,
            GetInt32Property(parameters, "max_entries_to_build"),
            GetInt32Property(parameters, "max_entries_to_merge"),
            GetStringProperty(parameters, "merge_method") ?? string.Empty,
            GetInt32Property(parameters, "min_entries_to_merge"),
            GetInt32Property(parameters, "min_entries_to_merge_wait_minutes"));
    }

    private static int? GetInt32Property(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out JsonElement prop) && prop.TryGetInt32(out int value)
            ? value
            : null;

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

    private static readonly string[] PushOrDispatchEvents = ["push", "workflow_dispatch"];

    private static readonly string[] PullRequestEvents = ["pull_request"];

    private static bool IsPushOnlyJob(WorkflowJobInfo job) =>
        job.IfCondition is not null && RequiresPushEvent(job.IfCondition);

    private static bool IsPullRequestOnlyJob(WorkflowJobInfo job) =>
        job.IfCondition is not null && RequiresAnyEvent(job.IfCondition, PullRequestEvents);

    /// <summary>True when a GitHub Actions <c>if:</c> expression restricts execution to <c>push</c> or <c>workflow_dispatch</c> events.</summary>
    public static bool RequiresPushEvent(string expression) =>
        RequiresAnyEvent(expression, PushOrDispatchEvents);

    private static bool RequiresAnyEvent(string expression, IReadOnlyList<string> eventNames)
    {
        string trimmed = UnwrapExpression(expression);
        if (trimmed.Length == 0)
        {
            return false;
        }

        var orClauses = SplitTopLevelLogical(trimmed, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.All(clause => RequiresAnyEvent(clause, eventNames));
        }

        var andClauses = SplitTopLevelLogical(trimmed, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.Any(clause => RequiresAnyEvent(clause, eventNames));
        }

        string normalized = WhitespaceRegex().Replace(trimmed, " ");
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

        string trimmed = UnwrapExpression(expression);
        if (trimmed.Length == 0)
        {
            return true;
        }

        var orClauses = SplitTopLevelLogical(trimmed, "||");
        if (orClauses.Count > 1)
        {
            return orClauses.Any(clause => EvaluatesJobIf(clause, eventName, classifyOutputTrue, gitRef));
        }

        var andClauses = SplitTopLevelLogical(trimmed, "&&");
        if (andClauses.Count > 1)
        {
            return andClauses.All(clause => EvaluatesJobIf(clause, eventName, classifyOutputTrue, gitRef));
        }

        if (trimmed.StartsWith('!'))
        {
            return !EvaluatesJobIf(trimmed[1..], eventName, classifyOutputTrue, gitRef);
        }

        return EvaluateAtomicIfClause(WhitespaceRegex().Replace(trimmed, " "), eventName, classifyOutputTrue, gitRef);
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

        var needsResult = NeedsResultComparisonRegex().Match(normalized);
        if (needsResult.Success)
        {
            string neededJob = needsResult.Groups["job"].Value;
            string op = needsResult.Groups["op"].Value;
            string target = needsResult.Groups["val"].Value;
            string simulatedResult = "success";
            if (string.Equals(neededJob, "classify", StringComparison.Ordinal)
                && eventName is not ("pull_request" or "merge_group"))
            {
                simulatedResult = "skipped";
            }

            bool matches = string.Equals(simulatedResult, target, StringComparison.Ordinal);
            return op == "==" ? matches : !matches;
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
        var prTrigger = ParseOnPullRequest(lines);
        bool onMergeGroup = ParseWorkflowTrigger(lines, MergeGroupTriggerRegex());
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

            jobs.Add(BuildJobInfo(workflowFile, prTrigger, onMergeGroup, jobKey, lines[i..jobEnd]));
            i = jobEnd;
        }

        return jobs;
    }

    private static bool ParseWorkflowTrigger(string[] lines, Regex triggerPattern)
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
                if (triggerPattern.IsMatch(line))
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
                else if (triggerPattern.IsMatch(line))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static PullRequestTriggerInfo ParseOnPullRequest(string[] lines)
    {
        bool inOnBlock = false;
        bool onPullRequest = false;
        bool hasExplicitTypes = false;
        var types = new HashSet<string>(StringComparer.Ordinal);
        int pullRequestIndent = -1;
        int typesIndent = -1;

        foreach (string raw in lines)
        {
            string line = StripYamlComment(raw);
            if (line.TrimEnd() == "jobs:")
            {
                break;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            int indent = CountLeadingSpaces(line);
            if (line.StartsWith("on:", StringComparison.Ordinal))
            {
                inOnBlock = true;
                pullRequestIndent = -1;
                typesIndent = -1;
                if (PullRequestTriggerRegex().IsMatch(line))
                {
                    onPullRequest = true;
                }

                continue;
            }

            if (!inOnBlock)
            {
                continue;
            }

            if (indent == 0)
            {
                inOnBlock = false;
                pullRequestIndent = -1;
                typesIndent = -1;
                continue;
            }

            if (typesIndent >= 0)
            {
                if (indent > typesIndent)
                {
                    AddTriggerTypesFromLine(trimmed, types);
                    continue;
                }

                typesIndent = -1;
            }

            if (pullRequestIndent >= 0)
            {
                if (indent <= pullRequestIndent)
                {
                    pullRequestIndent = -1;
                }
                else if (trimmed.StartsWith("types:", StringComparison.Ordinal))
                {
                    hasExplicitTypes = true;
                    string afterTypes = trimmed["types:".Length..].Trim();
                    if (afterTypes.Length > 0 && afterTypes != "[")
                    {
                        AddTriggerTypesFromLine(afterTypes, types);
                        if (afterTypes.StartsWith('[') && !afterTypes.EndsWith(']'))
                        {
                            typesIndent = indent;
                        }
                    }
                    else
                    {
                        typesIndent = indent;
                    }

                    continue;
                }
            }

            if (trimmed.StartsWith("pull_request:", StringComparison.Ordinal))
            {
                onPullRequest = true;
                pullRequestIndent = indent;
            }
            else if (PullRequestTriggerRegex().IsMatch(trimmed) && pullRequestIndent < 0)
            {
                onPullRequest = true;
            }
        }

        return new PullRequestTriggerInfo(onPullRequest, hasExplicitTypes, types);
    }

    private static void AddTriggerTypesFromLine(string text, HashSet<string> types)
    {
        if (text.StartsWith("- ", StringComparison.Ordinal))
        {
            string single = UnquoteYaml(text[2..]);
            if (single.Length > 0)
            {
                types.Add(single);
            }

            return;
        }

        string cleaned = text.Trim().TrimStart('[').TrimEnd(']');
        foreach (string part in cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string item = UnquoteYaml(part);
            if (item.Length > 0)
            {
                types.Add(item);
            }
        }
    }

    private static int CountLeadingSpaces(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static WorkflowJobInfo BuildJobInfo(
        string workflowFile,
        PullRequestTriggerInfo prTrigger,
        bool onMergeGroup,
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
                ifCond = ReadScalarOrBlock(rest, bodyLines, ref i, parentIndent: 4);
            }
            else if (prop == "needs")
            {
                ReadNeedsList(rest, bodyLines, ref i, needs);
            }
        }

        return new WorkflowJobInfo(
            workflowFile,
            prTrigger,
            onMergeGroup,
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
        int stepPropIndent = 8;
        int envIndent = -1;
        while (i < stepLines.Length)
        {
            string line = stepLines[i];
            if (line.Trim().Length == 0)
            {
                i++;
                continue;
            }

            int indent = CountLeadingSpaces(line);
            string trimmedStart = line[indent..];
            string stepPropText;
            if (trimmedStart.StartsWith("- ", StringComparison.Ordinal))
            {
                stepPropIndent = indent + 2;
                envIndent = -1;
                stepPropText = trimmedStart[2..];
            }
            else
            {
                if (envIndent >= 0)
                {
                    if (indent > envIndent)
                    {
                        var envMatch = StepEnvEntryRegex().Match(trimmedStart);
                        if (envMatch.Success)
                        {
                            stepEnv[envMatch.Groups[1].Value] = envMatch.Groups[2].Value.Trim();
                        }

                        i++;
                        continue;
                    }

                    envIndent = -1;
                }

                if (indent != stepPropIndent)
                {
                    i++;
                    continue;
                }

                stepPropText = trimmedStart;
            }

            if (stepPropText.TrimEnd() == "env:")
            {
                envIndent = stepPropIndent;
                i++;
                continue;
            }

            if (stepPropText.StartsWith("run:", StringComparison.Ordinal))
            {
                string inline = stepPropText["run:".Length..].Trim();
                i++;
                stepRuns.Add(ReadScalarOrBlock(inline, stepLines, ref i, parentIndent: stepPropIndent));
                continue;
            }

            i++;
        }
    }

    private static string ReadScalarOrBlock(string inlineValue, string[] lines, ref int index, int parentIndent)
    {
        if (inlineValue is not ("|" or "|-" or "|+" or ">" or ">-" or ">+"))
        {
            return UnquoteYaml(inlineValue);
        }

        int blockIndent = -1;
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

            int indent = CountLeadingSpaces(line);
            if (blockIndent < 0)
            {
                if (indent <= parentIndent)
                {
                    break;
                }

                blockIndent = indent;
            }
            else if (indent < blockIndent)
            {
                break;
            }

            collected.Add(line[blockIndent..]);
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

    [GeneratedRegex(@"^([A-Za-z0-9_]+):\s*(.+)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StepEnvEntryRegex();

    [GeneratedRegex(@"\bpull_request\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PullRequestTriggerRegex();

    [GeneratedRegex(@"\bgh\s+pr\s+merge\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GhPrMergeRegex();

    [GeneratedRegex(@"\bmerge_group\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MergeGroupTriggerRegex();

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^(?:github\.event_name\s*(?<op>==|!=)\s*['""](?<val>[A-Za-z0-9_]+)['""]|['""](?<val2>[A-Za-z0-9_]+)['""]\s*(?<op>==|!=)\s*github\.event_name)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EventNameComparisonRegex();

    [GeneratedRegex(@"^needs\.classify\.outputs\.[A-Za-z0-9_-]+\s*==\s*['""]true['""]$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClassifyOutputTrueRegex();

    [GeneratedRegex(@"^needs\.(?<job>[A-Za-z0-9_-]+)\.result\s*(?<op>==|!=)\s*['""](?<val>[A-Za-z0-9_-]+)['""]$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NeedsResultComparisonRegex();

    [GeneratedRegex(@"^github\.ref\s*==\s*['""](?<ref>[^'""]+)['""]$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitRefEqualsRegex();

    [GeneratedRegex(@"^startsWith\(\s*github\.ref\s*,\s*['""](?<prefix>[^'""]+)['""]\s*\)$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitRefStartsWithRegex();
}
