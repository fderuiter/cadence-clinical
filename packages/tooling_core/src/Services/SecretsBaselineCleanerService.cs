using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class SecretsBaselineCleanerService
{
    public const string SecretsDirectory = "baselines/secrets"; // pragma: allowlist secret
    public const string ConfigFile = "baselines/secrets/config.json";
    public const string ShardSuffix = ".secrets.baseline.json";
    public const string LegacyBaselineFile = ".secrets.baseline";

    /// <summary>
    /// Canonical file-exclusion regex shared by <c>baselines/secrets/config.json</c> (<c>should_exclude_file</c>),
    /// <see cref="DetectSecretsGateService"/>, <c>CiPlan</c>, and <c>.github/workflows/ci.yml</c>.
    /// </summary>
    public const string CanonicalExcludePattern =
        @"(pnpm-lock\.yaml|package-lock\.json|yarn\.lock|uv\.lock|docs/CDISC/|docs/NCI/|docs/openapi/|docs/schema/|docs/SDLC/runs/|Migrations/[^/]+\.Designer\.cs$|report.*\.xml|coverage.*\.xml|\.cadence/|baselines/|\.(pdf|xlsx|xls|docx|doc|png|jpg|jpeg|gif|svg|ico|woff|woff2|eot|ttf|otf|mp4|webm|mp3|wav|zip|tar\.gz|gz|sqlite|db)$)";

    [GeneratedRegex(CanonicalExcludePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExcludePatternRegex();

    public static string GetShardRelativePath(string filePath) =>
        $"{SecretsDirectory}/{BaselineUnitPath.ResolveUnit(filePath)}{ShardSuffix}";

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
        bool readOnly = check || (preCommit && customPath is null);

        GitIndexReader? ownedReader = null;
        GitIndexReader? reader = customPath is null
            ? GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out ownedReader)
            : null;
        using (ownedReader)
        {
            try
            {
                if (readOnly)
                {
                    return ValidateShardedBaseline(repoRoot, customPath, reader);
                }

                return NormalizeAndWriteShards(repoRoot, customPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Error cleaning secrets baseline: {ex.Message}");
                return 1;
            }
        }
    }

    public static string FormatJsonObject(JsonObject obj)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            obj.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n";
    }

    private static int ValidateShardedBaseline(string repoRoot, string? customPath, GitIndexReader? reader)
    {
        string legacyPath = customPath ?? Path.Combine(repoRoot, LegacyBaselineFile);
        bool hasLegacy = reader is { HasAuthoritativeIndex: true }
            ? reader.FileExists(LegacyBaselineFile)
            : File.Exists(legacyPath);
        if (hasLegacy)
        {
            Console.WriteLine($"[ERROR] Legacy monolithic secrets baseline at '{legacyPath}' must be split into '{SecretsDirectory}/'. Run 'pnpm cadence clean-secrets-baseline' or 'pnpm cadence fix' to normalize it.");
            return 1;
        }

        var existingFiles = ListSecretsFiles(repoRoot, reader);
        if (existingFiles.Count == 0)
        {
            Console.WriteLine($"✔ Secrets baseline directory '{SecretsDirectory}' not found at {repoRoot}, skipping.");
            return 0;
        }

        if (!TryReadSecretsFile(repoRoot, ConfigFile, reader, out string configText))
        {
            Console.WriteLine($"[ERROR] Missing '{ConfigFile}'. Run 'pnpm cadence clean-secrets-baseline' to create it.");
            return 1;
        }

        if (JsonNode.Parse(configText) is not JsonObject configObj)
        {
            Console.WriteLine($"[ERROR] '{ConfigFile}' must be a JSON object.");
            return 1;
        }

        var normalizedConfig = BuildNormalizedConfig(configObj);
        string expectedConfig = FormatJsonObject(normalizedConfig);
        if (!string.Equals(configText.ReplaceLineEndings("\n"), expectedConfig, StringComparison.Ordinal))
        {
            Console.WriteLine($"[ERROR] Secrets baseline config '{ConfigFile}' is not normalized. Run 'pnpm cadence clean-secrets-baseline' or 'pnpm cadence fix'.");
            return 1;
        }

        foreach (string relPath in existingFiles.Order(StringComparer.Ordinal))
        {
            if (string.Equals(relPath, ConfigFile, StringComparison.Ordinal))
            {
                continue;
            }

            if (!relPath.EndsWith(ShardSuffix, StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] Unexpected file '{relPath}' under '{SecretsDirectory}/'; expected '{ConfigFile}' or '*{ShardSuffix}'.");
                return 1;
            }

            string expectedUnit = relPath[(SecretsDirectory.Length + 1)..^ShardSuffix.Length];
            if (!TryReadSecretsFile(repoRoot, relPath, reader, out string shardText))
            {
                Console.WriteLine($"[ERROR] Could not read secrets baseline shard '{relPath}'.");
                return 1;
            }

            if (JsonNode.Parse(shardText) is not JsonObject shardObj)
            {
                Console.WriteLine($"[ERROR] Secrets baseline shard '{relPath}' must be a JSON object.");
                return 1;
            }

            if (shardObj.Count != 1 || shardObj["results"] is not JsonObject resultsObj)
            {
                Console.WriteLine($"[ERROR] Secrets baseline shard '{relPath}' must contain only a 'results' object.");
                return 1;
            }

            if (resultsObj.Count == 0)
            {
                Console.WriteLine($"[ERROR] Empty secrets baseline shard '{relPath}' must be deleted.");
                return 1;
            }

            var cleanedShardResults = new JsonObject();
            foreach (var prop in resultsObj.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string filename = prop.Key;
                if (IsExcludedFile(filename))
                {
                    Console.WriteLine($"[ERROR] Secrets baseline shard '{relPath}' contains excluded file '{filename}'.");
                    return 1;
                }

                if (!RepositoryFileExists(repoRoot, filename, reader))
                {
                    Console.WriteLine($"[ERROR] Secrets baseline shard '{relPath}' references missing file '{filename}'. Run 'pnpm cadence clean-secrets-baseline' or 'pnpm cadence fix'.");
                    return 1;
                }

                string fileUnit = BaselineUnitPath.ResolveUnit(filename);
                if (!string.Equals(fileUnit, expectedUnit, StringComparison.Ordinal))
                {
                    Console.WriteLine($"[ERROR] Entry '{filename}' in '{relPath}' belongs in '{GetShardRelativePath(filename)}'.");
                    return 1;
                }

                if (prop.Value is not JsonArray arr || arr.Count == 0)
                {
                    Console.WriteLine($"[ERROR] Entry '{filename}' in '{relPath}' must have a non-empty secret array.");
                    return 1;
                }

                cleanedShardResults[filename] = CleanSecretItems(arr);
            }

            var expectedShardObj = new JsonObject { ["results"] = cleanedShardResults };
            string expectedShardText = FormatJsonObject(expectedShardObj);
            if (!string.Equals(shardText.ReplaceLineEndings("\n"), expectedShardText, StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] Secrets baseline shard '{relPath}' is not normalized. Run 'pnpm cadence clean-secrets-baseline' or 'pnpm cadence fix'.");
                return 1;
            }
        }

        Console.WriteLine($"✔ Secrets baseline shards under '{SecretsDirectory}/' are normalized.");
        return 0;
    }

    private static int NormalizeAndWriteShards(string repoRoot, string? customPath)
    {
        string legacyPath = customPath ?? Path.Combine(repoRoot, LegacyBaselineFile);
        string configFullPath = Path.Combine(repoRoot, ConfigFile);
        string secretsDirFullPath = Path.Combine(repoRoot, SecretsDirectory);

        bool hasLegacy = File.Exists(legacyPath);
        bool hasConfig = File.Exists(configFullPath);
        var existingShardPaths = Directory.Exists(secretsDirFullPath)
            ? Directory.GetFiles(secretsDirFullPath, "*" + ShardSuffix, SearchOption.AllDirectories)
            : [];

        if (!hasLegacy && !hasConfig && existingShardPaths.Length == 0)
        {
            Console.WriteLine($"✔ Secrets baseline not found at {repoRoot}, skipping.");
            return 0;
        }

        JsonObject? baseConfigSource = null;
        var mergedResults = new Dictionary<string, JsonArray>(StringComparer.Ordinal);

        if (hasConfig)
        {
            if (JsonNode.Parse(File.ReadAllText(configFullPath)) is not JsonObject parsedConfig)
            {
                Console.WriteLine($"[ERROR] '{ConfigFile}' must be a JSON object.");
                return 1;
            }

            baseConfigSource = parsedConfig;
            CollectResults(repoRoot, parsedConfig, mergedResults);
        }

        foreach (string shardFullPath in existingShardPaths.Order(StringComparer.Ordinal))
        {
            if (JsonNode.Parse(File.ReadAllText(shardFullPath)) is not JsonObject parsedShard)
            {
                Console.WriteLine($"[ERROR] Secrets baseline shard '{shardFullPath}' must be a JSON object.");
                return 1;
            }

            CollectResults(repoRoot, parsedShard, mergedResults);
        }

        if (hasLegacy)
        {
            if (JsonNode.Parse(File.ReadAllText(legacyPath)) is not JsonObject parsedLegacy)
            {
                Console.WriteLine($"[ERROR] Secrets baseline at '{legacyPath}' must be a JSON object.");
                return 1;
            }

            baseConfigSource ??= parsedLegacy;
            CollectResults(repoRoot, parsedLegacy, mergedResults);
        }

        baseConfigSource ??= new JsonObject
        {
            ["version"] = DetectSecretsGateService.PinnedVersion,
            ["plugins_used"] = new JsonArray(),
            ["filters_used"] = new JsonArray(),
        };

        var normalizedConfig = BuildNormalizedConfig(baseConfigSource);
        Directory.CreateDirectory(secretsDirFullPath);
        File.WriteAllText(configFullPath, FormatJsonObject(normalizedConfig));

        var groupedByShard = mergedResults
            .Where(kv => kv.Value.Count > 0)
            .GroupBy(kv => GetShardRelativePath(kv.Key), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        foreach (string existingFull in existingShardPaths)
        {
            string rel = Path.GetRelativePath(repoRoot, existingFull).Replace('\\', '/');
            if (!groupedByShard.ContainsKey(rel))
            {
                File.Delete(existingFull);
                BaselineUnitPath.DeleteEmptyParentDirectories(existingFull, secretsDirFullPath);
            }
        }

        foreach (var (shardRelPath, entries) in groupedByShard.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var shardResults = new JsonObject();
            foreach (var (filename, arr) in entries)
            {
                shardResults[filename] = arr;
            }

            string shardFullPath = Path.Combine(repoRoot, shardRelPath);
            Directory.CreateDirectory(Path.GetDirectoryName(shardFullPath)!);
            File.WriteAllText(shardFullPath, FormatJsonObject(new JsonObject { ["results"] = shardResults }));
        }

        if (hasLegacy)
        {
            File.Delete(legacyPath);
        }

        string defaultLegacyInRoot = Path.Combine(repoRoot, LegacyBaselineFile);
        if (File.Exists(defaultLegacyInRoot))
        {
            File.Delete(defaultLegacyInRoot);
        }

        Console.WriteLine($"✔ Normalized secrets baseline into '{ConfigFile}' and {groupedByShard.Count} shard(s).");
        return 0;
    }

    private static JsonObject BuildNormalizedConfig(JsonObject source)
    {
        var clone = source.DeepClone().AsObject();
        clone.Remove("generated_at");
        clone.Remove("exclude");
        clone.Remove("results");
        EnsureCanonicalExcludeFilter(clone);
        return clone;
    }

    private static void CollectResults(string repoRoot, JsonObject container, Dictionary<string, JsonArray> destination)
    {
        if (container["results"] is not JsonObject resultsObj)
        {
            return;
        }

        foreach (var prop in resultsObj)
        {
            string filename = prop.Key.Replace('\\', '/');
            if (filename.StartsWith("./", StringComparison.Ordinal))
            {
                filename = filename[2..];
            }

            if (IsExcludedFile(filename) ||
                !RepositoryFileExists(repoRoot, filename, reader: null) ||
                prop.Value is not JsonArray arr)
            {
                continue;
            }

            var cleaned = CleanSecretItems(arr);
            if (cleaned.Count > 0)
            {
                destination[filename] = cleaned;
            }
        }
    }

    private static bool RepositoryFileExists(string repoRoot, string relativePath, GitIndexReader? reader)
    {
        string? normalized = GitIndexReader.TryNormalizeRelativePath(repoRoot, relativePath);
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return reader is { HasAuthoritativeIndex: true }
            ? reader.FileExists(normalized)
            : File.Exists(Path.Combine(repoRoot, normalized));
    }

    private static JsonArray CleanSecretItems(JsonArray arr)
    {
        var cleanedArr = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in arr)
        {
            if (item is not JsonObject itemObj)
            {
                continue;
            }

            var clone = itemObj.DeepClone().AsObject();
            clone.Remove("line_number");
            string dedupKey = $"{clone["type"]?.GetValue<string>()}|{clone["hashed_secret"]?.GetValue<string>()}";
            if (seen.Add(dedupKey))
            {
                cleanedArr.Add((JsonNode?)clone);
            }
        }

        return cleanedArr;
    }

    private static List<string> ListSecretsFiles(string repoRoot, GitIndexReader? reader)
    {
        if (reader is { HasAuthoritativeIndex: true })
        {
            return reader.Paths
                .Where(p => p.StartsWith(SecretsDirectory + "/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        string fullDir = Path.Combine(repoRoot, SecretsDirectory);
        if (!Directory.Exists(fullDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(fullDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryReadSecretsFile(string repoRoot, string relativePath, GitIndexReader? reader, out string content)
    {
        if (reader is { HasAuthoritativeIndex: true })
        {
            return reader.TryReadText(relativePath, out content);
        }

        string fullPath = Path.Combine(repoRoot, relativePath);
        if (!File.Exists(fullPath))
        {
            content = "";
            return false;
        }

        content = File.ReadAllText(fullPath);
        return true;
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

    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();
}
