using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Cadence.Cli.Services;

/// <summary>
/// Read-only live GitHub Repository Ruleset drift verifier (<c>cadence rulesets --verify-live</c>).
/// Queries <c>gh api repos/&lt;owner&gt;/&lt;repo&gt;/rulesets</c> and compares live ruleset targets,
/// enforcement, ref conditions, bypass actors, and rule parameters against the profile-projected
/// declarative <c>.github/rulesets/*.json</c> payloads without reflection (Native AOT safe).
/// </summary>
internal static class RulesetLiveVerifier
{
    internal static int VerifyLiveRulesetsViaGhApi(
        string repoRoot,
        string repo,
        string profile,
        IReadOnlyList<RulesetService.RulesetDefinition> definitions,
        RulesetService.ProcessRunner runner,
        TextWriter output)
    {
        var listOutcome = runner(repoRoot, "gh", ["api", "--paginate", $"repos/{repo}/rulesets?per_page=100"]);
        if (listOutcome.ExitCode != 0)
        {
            output.WriteLine(
                $"{RulesetService.Tag} [ERROR] Failed to fetch existing rulesets from 'repos/{repo}/rulesets' (exit code {listOutcome.ExitCode}).");
            RulesetService.WriteProcessDetails(listOutcome, output);
            output.WriteLine($"{RulesetService.Tag} Ensure 'gh' is authenticated with read access to rulesets on '{repo}'.");
            return 1;
        }

        if (!RulesetService.TryParseExistingRulesetEntries(listOutcome.Stdout, out var remoteEntries, out string? parseError))
        {
            output.WriteLine($"{RulesetService.Tag} [ERROR] Could not parse GitHub rulesets API response: {parseError}");
            return 1;
        }

        var driftErrors = new List<string>();
        var idsByName = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        foreach ((string name, long id) in remoteEntries)
        {
            if (!idsByName.TryGetValue(name, out var ids))
            {
                ids = [];
                idsByName[name] = ids;
            }

            ids.Add(id);
        }

        foreach (var def in definitions)
        {
            string relPath = Path.GetRelativePath(repoRoot, def.FullPath).Replace('\\', '/');
            if (!idsByName.TryGetValue(def.Name, out var matchingIds) || matchingIds.Count == 0)
            {
                driftErrors.Add($"Live GitHub repository '{repo}' is missing ruleset '{def.Name}' (declared in '{relPath}').");
                continue;
            }

            if (matchingIds.Count > 1)
            {
                driftErrors.Add(
                    $"Live GitHub repository '{repo}' has {matchingIds.Count} duplicate rulesets named '{def.Name}' (ids: {string.Join(", ", matchingIds)}).");
                continue;
            }

            long rulesetId = matchingIds[0];
            string endpoint = $"repos/{repo}/rulesets/{rulesetId}";
            var detailOutcome = runner(repoRoot, "gh", ["api", endpoint]);
            if (detailOutcome.ExitCode != 0)
            {
                output.WriteLine(
                    $"{RulesetService.Tag} [ERROR] Failed to fetch live ruleset '{def.Name}' (id {rulesetId}) from '{endpoint}' (exit code {detailOutcome.ExitCode}).");
                RulesetService.WriteProcessDetails(detailOutcome, output);
                return 1;
            }

            JsonDocument remoteDoc;
            try
            {
                remoteDoc = JsonDocument.Parse(detailOutcome.Stdout);
            }
            catch (JsonException ex)
            {
                output.WriteLine(
                    $"{RulesetService.Tag} [ERROR] Could not parse live ruleset '{def.Name}' (id {rulesetId}) JSON from '{endpoint}': {ex.Message}");
                return 1;
            }

            using (remoteDoc)
            {
                if (remoteDoc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    output.WriteLine(
                        $"{RulesetService.Tag} [ERROR] Expected JSON object for live ruleset '{def.Name}' (id {rulesetId}), got {remoteDoc.RootElement.ValueKind}.");
                    return 1;
                }

                string projectedJson = RulesetService.ProjectRulesetPayloadForProfile(File.ReadAllText(def.FullPath), profile);
                using var expectedDoc = JsonDocument.Parse(projectedJson);
                CompareProjectedAndLiveRuleset(def.Name, rulesetId, profile, expectedDoc.RootElement, remoteDoc.RootElement, driftErrors);
            }
        }

        if (driftErrors.Count > 0)
        {
            RulesetService.WriteErrors(driftErrors, output);
            return 1;
        }

        output.WriteLine($"{RulesetService.Tag} ✔ Verified {definitions.Count} live GitHub rulesets on '{repo}' match local profile '{profile}'.");
        return 0;
    }

    private static void CompareProjectedAndLiveRuleset(
        string name,
        long rulesetId,
        string profile,
        JsonElement expected,
        JsonElement actual,
        List<string> driftErrors)
    {
        string prefix = $"Live ruleset '{name}' (id {rulesetId})";

        string expectedTarget = RulesetService.GetStringProperty(expected, "target") ?? "";
        string actualTarget = RulesetService.GetStringProperty(actual, "target") ?? "";
        if (!string.Equals(expectedTarget, actualTarget, StringComparison.Ordinal))
        {
            driftErrors.Add($"{prefix}: 'target' is '{actualTarget}', expected '{expectedTarget}'.");
        }

        string expectedEnforcement = RulesetService.GetStringProperty(expected, "enforcement") ?? "";
        string actualEnforcement = RulesetService.GetStringProperty(actual, "enforcement") ?? "";
        if (!string.Equals(expectedEnforcement, actualEnforcement, StringComparison.Ordinal))
        {
            driftErrors.Add($"{prefix}: 'enforcement' is '{actualEnforcement}', expected '{expectedEnforcement}'.");
        }

        CompareStringSet(
            $"{prefix}: 'conditions.ref_name.include'",
            ReadRefConditionList(expected, "include"),
            ReadRefConditionList(actual, "include"),
            driftErrors);
        CompareStringSet(
            $"{prefix}: 'conditions.ref_name.exclude'",
            ReadRefConditionList(expected, "exclude"),
            ReadRefConditionList(actual, "exclude"),
            driftErrors);

        var parseBypassErrors = new List<string>();
        string[] expectedActors = RulesetService.ReadBypassActors(expected, prefix, parseBypassErrors)
            .Select(a => $"{a.ActorType}:{a.ActorId?.ToString(CultureInfo.InvariantCulture) ?? "null"}:{a.BypassMode}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] actualActors = RulesetService.ReadBypassActors(actual, prefix, parseBypassErrors)
            .Select(a => $"{a.ActorType}:{a.ActorId?.ToString(CultureInfo.InvariantCulture) ?? "null"}:{a.BypassMode}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (parseBypassErrors.Count > 0)
        {
            driftErrors.AddRange(parseBypassErrors);
        }
        else if (!expectedActors.SequenceEqual(actualActors, StringComparer.Ordinal))
        {
            driftErrors.Add(
                $"{prefix}: 'bypass_actors' is [{string.Join(", ", actualActors)}], expected [{string.Join(", ", expectedActors)}].");
        }

        var expectedRules = IndexRulesByType(expected);
        var actualRules = IndexRulesByType(actual);

        string[] missingRules = expectedRules.Keys
            .Where(t => !actualRules.ContainsKey(t))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missingRules.Length > 0)
        {
            driftErrors.Add(
                $"{prefix}: missing required rule(s) [{string.Join(", ", missingRules)}] for profile '{profile}'.");
        }

        string[] extraRules = actualRules.Keys
            .Where(t => !expectedRules.ContainsKey(t))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (extraRules.Length > 0)
        {
            driftErrors.Add(
                $"{prefix}: enables unexpected rule(s) [{string.Join(", ", extraRules)}] not present in profile '{profile}'.");
        }

        foreach ((string ruleType, JsonElement expectedRule) in expectedRules.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!actualRules.TryGetValue(ruleType, out JsonElement actualRule))
            {
                continue;
            }

            if (!expectedRule.TryGetProperty("parameters", out JsonElement expectedParams) ||
                expectedParams.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!actualRule.TryGetProperty("parameters", out JsonElement actualParams) ||
                actualParams.ValueKind != JsonValueKind.Object)
            {
                driftErrors.Add($"{prefix}: rule '{ruleType}' is missing 'parameters' object.");
                continue;
            }

            foreach (JsonProperty expectedProp in expectedParams.EnumerateObject())
            {
                if (ruleType == "required_status_checks" && expectedProp.NameEquals("required_status_checks"))
                {
                    CompareStatusCheckLists(prefix, expectedProp.Value, actualParams, driftErrors);
                    continue;
                }

                if (!actualParams.TryGetProperty(expectedProp.Name, out JsonElement actualVal))
                {
                    if (expectedProp.NameEquals("do_not_enforce_on_create") && expectedProp.Value.ValueKind == JsonValueKind.False)
                    {
                        continue;
                    }

                    driftErrors.Add($"{prefix}: rule '{ruleType}' is missing parameter '{expectedProp.Name}'.");
                    continue;
                }

                if (!JsonScalarEquals(expectedProp.Value, actualVal))
                {
                    driftErrors.Add(
                        $"{prefix}: rule '{ruleType}' parameter '{expectedProp.Name}' is '{FormatJsonScalar(actualVal)}', expected '{FormatJsonScalar(expectedProp.Value)}'.");
                }
            }
        }
    }

    private static Dictionary<string, JsonElement> IndexRulesByType(JsonElement root)
    {
        var byType = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.TryGetProperty("rules", out JsonElement rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement rule in rulesEl.EnumerateArray())
            {
                if (rule.ValueKind == JsonValueKind.Object && RulesetService.GetStringProperty(rule, "type") is { Length: > 0 } type)
                {
                    byType[type] = rule;
                }
            }
        }

        return byType;
    }

    private static List<string> ReadRefConditionList(JsonElement root, string listProperty)
    {
        var refs = new List<string>();
        if (root.TryGetProperty("conditions", out JsonElement cond)
            && cond.ValueKind == JsonValueKind.Object
            && cond.TryGetProperty("ref_name", out JsonElement refName)
            && refName.ValueKind == JsonValueKind.Object
            && refName.TryGetProperty(listProperty, out JsonElement arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                {
                    refs.Add(value);
                }
            }
        }

        return refs;
    }

    private static void CompareStringSet(
        string label,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual,
        List<string> driftErrors)
    {
        string[] expectedSorted = [.. expected.Order(StringComparer.Ordinal)];
        string[] actualSorted = [.. actual.Order(StringComparer.Ordinal)];
        if (!expectedSorted.SequenceEqual(actualSorted, StringComparer.Ordinal))
        {
            driftErrors.Add(
                $"{label} is [{string.Join(", ", actualSorted)}], expected [{string.Join(", ", expectedSorted)}].");
        }
    }

    private static void CompareStatusCheckLists(
        string prefix,
        JsonElement expectedChecksArray,
        JsonElement actualParams,
        List<string> driftErrors)
    {
        if (!actualParams.TryGetProperty("required_status_checks", out JsonElement actualChecksArray) ||
            actualChecksArray.ValueKind != JsonValueKind.Array)
        {
            driftErrors.Add($"{prefix}: rule 'required_status_checks' is missing 'required_status_checks' array.");
            return;
        }

        static string[] FormatChecks(JsonElement arr)
        {
            var items = new List<string>();
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement check in arr.EnumerateArray())
                {
                    if (check.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string ctx = RulesetService.GetStringProperty(check, "context") ?? "";
                    string intId = check.TryGetProperty("integration_id", out JsonElement idEl) && idEl.TryGetInt64(out long id)
                        ? id.ToString(CultureInfo.InvariantCulture)
                        : "null";
                    items.Add($"{ctx}@{intId}");
                }
            }

            return [.. items.Order(StringComparer.Ordinal)];
        }

        string[] expectedFormatted = FormatChecks(expectedChecksArray);
        string[] actualFormatted = FormatChecks(actualChecksArray);
        if (!expectedFormatted.SequenceEqual(actualFormatted, StringComparer.Ordinal))
        {
            driftErrors.Add(
                $"{prefix}: 'required_status_checks' entries are [{string.Join(", ", actualFormatted)}], expected [{string.Join(", ", expectedFormatted)}].");
        }
    }

    private static bool JsonScalarEquals(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind)
        {
            return false;
        }

        return expected.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            JsonValueKind.String => string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => expected.TryGetInt64(out long expLong) && actual.TryGetInt64(out long actLong)
                ? expLong == actLong
                : string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal),
            _ => string.Equals(expected.GetRawText(), actual.GetRawText(), StringComparison.Ordinal),
        };
    }

    private static string FormatJsonScalar(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : element.GetRawText();
}
