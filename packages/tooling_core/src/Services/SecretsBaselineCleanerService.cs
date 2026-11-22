using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class SecretsBaselineCleanerService
{
    /// <summary>
    /// Canonical file-exclusion regex shared by <c>.secrets.baseline</c> (<c>should_exclude_file</c>),
    /// <see cref="DetectSecretsGateService"/>, <c>CiPlan</c>, and <c>.github/workflows/ci.yml</c>.
    /// </summary>
    public const string CanonicalExcludePattern =
        @"(pnpm-lock\.yaml|package-lock\.json|yarn\.lock|uv\.lock|docs/CDISC/|docs/NCI/|docs/openapi/|docs/schema/|docs/SDLC/runs/|Migrations/[^/]+\.Designer\.cs$|Migrations/idempotent_migration\.sql$|report.*\.xml|coverage.*\.xml|\.cadence/|dist/|\.(pdf|xlsx|xls|docx|doc|png|jpg|jpeg|gif|svg|ico|woff|woff2|eot|ttf|otf|mp4|webm|mp3|wav|zip|tar\.gz|gz|sqlite|db)$)";

    [GeneratedRegex(CanonicalExcludePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExcludePatternRegex();

    public static bool IsExcludedFile(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return ExcludePatternRegex().IsMatch(normalized);
    }

    public static int CleanSecretsBaseline(
        string? customPath = null,
        string? repoRoot = null,
        bool check = false,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        string filepath = customPath ?? Path.Combine(repoRoot, ".secrets.baseline");

        GitIndexReader? ownedReader = null;
        GitIndexReader? reader = customPath is null
            ? GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out ownedReader)
            : null;
        using (ownedReader)
        {
            string jsonText;
            if (reader is { HasAuthoritativeIndex: true })
            {
                if (!reader.FileExists(".secrets.baseline"))
                {
                    Console.WriteLine($"✔ Secrets baseline file not found at {filepath}, skipping.");
                    return 0;
                }

                if (!reader.TryReadText(".secrets.baseline", out jsonText))
                {
                    Console.WriteLine($"[ERROR] Could not read staged secrets baseline at {filepath}.");
                    return 1;
                }
            }
            else
            {
                if (!File.Exists(filepath))
                {
                    Console.WriteLine($"✔ Secrets baseline file not found at {filepath}, skipping.");
                    return 0;
                }

                jsonText = File.ReadAllText(filepath);
            }

            try
            {
                var node = JsonNode.Parse(jsonText);
                if (node is not JsonObject obj)
                {
                    Console.WriteLine($"[ERROR] Secrets baseline at {filepath} must be a JSON object.");
                    return 1;
                }

                obj.Remove("generated_at");
                obj.Remove("exclude");

                EnsureCanonicalExcludeFilter(obj);

                if (obj["results"] is JsonObject resultsObj)
                {
                    var cleanedResults = new JsonObject();
                    foreach (var prop in resultsObj)
                    {
                        string filename = prop.Key;
                        if (IsExcludedFile(filename))
                        {
                            continue;
                        }

                        if (prop.Value is JsonArray arr)
                        {
                            var cleanedArr = new JsonArray();
                            foreach (var item in arr)
                            {
                                if (item is JsonObject itemObj)
                                {
                                    itemObj.Remove("line_number");
                                    cleanedArr.Add(itemObj.DeepClone());
                                }
                            }

                            cleanedResults[filename] = cleanedArr;
                        }
                    }

                    obj["results"] = cleanedResults;
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string expected = obj.ToJsonString(options).ReplaceLineEndings("\n") + "\n";
                string actual = jsonText.ReplaceLineEndings("\n");

                if (check || (preCommit && customPath is null))
                {
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"[ERROR] Secrets baseline at {filepath} is not normalized. Run 'pnpm cadence clean-secrets-baseline' or 'pnpm cadence fix' to normalize it.");
                        return 1;
                    }

                    Console.WriteLine($"✔ Secrets baseline at {filepath} is normalized.");
                    return 0;
                }

                File.WriteAllText(filepath, expected);

                Console.WriteLine($"✔ Successfully cleaned secrets baseline at {filepath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error cleaning secrets baseline: {ex.Message}");
                return 1;
            }
        }
    }

    private static void EnsureCanonicalExcludeFilter(JsonObject root)
    {
        const string filterPath = "detect_secrets.filters.regex.should_exclude_file";

        if (root["filters_used"] is not JsonArray filters)
        {
            filters = new JsonArray();
            root["filters_used"] = filters;
        }

        JsonObject? targetFilter = null;
        foreach (var entry in filters)
        {
            if (entry is JsonObject filterObj &&
                filterObj["path"]?.GetValue<string>() == filterPath)
            {
                targetFilter = filterObj;
                break;
            }
        }

        var patternArray = new JsonArray(JsonValue.Create(CanonicalExcludePattern));
        if (targetFilter is not null)
        {
            targetFilter["pattern"] = patternArray;
        }
        else
        {
            JsonNode filterNode = new JsonObject
            {
                ["path"] = JsonValue.Create(filterPath),
                ["pattern"] = patternArray,
            };
            filters.Add(filterNode);
        }
    }

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
