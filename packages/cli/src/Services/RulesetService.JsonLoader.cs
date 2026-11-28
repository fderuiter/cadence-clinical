using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Cadence.ToolingCore.Services;

namespace Cadence.Cli.Services;

public static partial class RulesetService
{
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
}
