using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Cadence.ToolingCore.Services;

public static partial class DetectSecretsGateService
{
    private static bool TryBuildMergedBaselineJson(string repoRoot, GitIndexReader? reader, out string mergedJson)
    {
        mergedJson = "";
        string configRelPath = SecretsBaselineCleanerService.ConfigFile;
        string configFullPath = Path.Combine(repoRoot, configRelPath);
        string configContent;

        if (reader is { HasAuthoritativeIndex: true })
        {
            if (!reader.TryReadText(configRelPath, out configContent))
            {
                Console.WriteLine($"[ERROR] Secrets baseline config '{configRelPath}' was not found in the Git index at '{repoRoot}'.");
                return false;
            }
        }
        else
        {
            if (!File.Exists(configFullPath))
            {
                Console.WriteLine($"[ERROR] Secrets baseline config file not found at '{configFullPath}'.");
                return false;
            }

            try
            {
                configContent = File.ReadAllText(configFullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Secrets baseline config '{configFullPath}' could not be read: {ex.Message}");
                return false;
            }
        }

        JsonObject mergedRoot;
        try
        {
            if (JsonNode.Parse(configContent) is not JsonObject parsedConfig)
            {
                Console.WriteLine($"[ERROR] Secrets baseline config '{configRelPath}' must be a JSON object.");
                return false;
            }

            mergedRoot = parsedConfig.DeepClone().AsObject();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Secrets baseline config '{configRelPath}' is invalid JSON: {ex.Message}");
            return false;
        }

        var mergedResults = new JsonObject();
        if (mergedRoot["results"] is JsonObject inlineResults)
        {
            foreach (var prop in inlineResults)
            {
                if (prop.Value is JsonArray arr)
                {
                    mergedResults[prop.Key] = arr.DeepClone();
                }
            }
        }

        IEnumerable<string> shardPaths;
        if (reader is { HasAuthoritativeIndex: true })
        {
            shardPaths = reader.Paths
                .Where(p => p.StartsWith(SecretsBaselineCleanerService.SecretsDirectory + "/", StringComparison.Ordinal) &&
                            p.EndsWith(SecretsBaselineCleanerService.ShardSuffix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);
        }
        else
        {
            string secretsDir = Path.Combine(repoRoot, SecretsBaselineCleanerService.SecretsDirectory);
            shardPaths = Directory.Exists(secretsDir)
                ? Directory.EnumerateFiles(secretsDir, "*" + SecretsBaselineCleanerService.ShardSuffix, SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(repoRoot, p).Replace('\\', '/'))
                    .Order(StringComparer.Ordinal)
                : [];
        }

        foreach (string shardRelPath in shardPaths)
        {
            string shardText;
            if (reader is { HasAuthoritativeIndex: true })
            {
                if (!reader.TryReadText(shardRelPath, out shardText))
                {
                    Console.WriteLine($"[ERROR] Secrets baseline shard '{shardRelPath}' could not be read from the Git index.");
                    return false;
                }
            }
            else
            {
                try
                {
                    shardText = File.ReadAllText(Path.Combine(repoRoot, shardRelPath));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Secrets baseline shard '{shardRelPath}' could not be read: {ex.Message}");
                    return false;
                }
            }

            try
            {
                if (JsonNode.Parse(shardText) is not JsonObject shardObj ||
                    shardObj["results"] is not JsonObject shardResults)
                {
                    Console.WriteLine($"[ERROR] Secrets baseline shard '{shardRelPath}' must contain a 'results' JSON object.");
                    return false;
                }

                foreach (var prop in shardResults.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (prop.Value is JsonArray arr)
                    {
                        mergedResults[prop.Key] = arr.DeepClone();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Secrets baseline shard '{shardRelPath}' is invalid JSON: {ex.Message}");
                return false;
            }
        }

        mergedRoot["results"] = mergedResults;
        mergedJson = SecretsBaselineCleanerService.FormatJsonObject(mergedRoot);
        return true;
    }
}
