using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.Cli.Services;

public static partial class RulesetService
{
    [GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OwnerRepoRegex();

    [GeneratedRegex(@"github\.com[:/]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GitRemoteRepoRegex();

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

    private static ProcessOutcome DefaultProcessRunner(string workingDirectory, string fileName, IReadOnlyList<string> arguments)
    {
        try
        {
            var psi = ProcessPrimitives.CreateStartInfo(fileName, workingDirectory, arguments);
            var output = ProcessPrimitives.RunCaptured(psi);
            return output.ExitCode < 0
                ? new ProcessOutcome(1, "", $"Could not start process '{fileName}'.")
                : new ProcessOutcome(output.ExitCode, output.Stdout, output.Stderr);
        }
        catch (Win32Exception ex)
        {
            return new ProcessOutcome(1, "", $"Could not execute '{fileName}': {ex.Message}");
        }
    }
}
