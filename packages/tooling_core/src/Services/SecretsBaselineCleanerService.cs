using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static class SecretsBaselineCleanerService
{
    private static readonly Regex ExcludeRegex = new(
        @"(pnpm-lock\.yaml|uv\.lock|docs/CDISC/|docs/NCI/|docs/openapi/|docs/schema/|docs/SDLC/runs/|report.*\.xml|coverage.*\.xml|\.cadence/|\.(pdf|xlsx|xls|docx|doc|png|jpg|jpeg|gif|svg|ico|woff|woff2|eot|ttf|otf|mp4|webm|mp3|wav|zip|tar\.gz|gz|sqlite|db)$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static int CleanSecretsBaseline(string? customPath = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        string filepath = customPath ?? Path.Combine(repoRoot, ".secrets.baseline");

        if (!File.Exists(filepath))
        {
            Console.WriteLine($"✔ Secrets baseline file not found at {filepath}, skipping.");
            return 0;
        }

        try
        {
            string jsonText = File.ReadAllText(filepath);
            var node = JsonNode.Parse(jsonText);
            if (node is JsonObject obj)
            {
                obj.Remove("generated_at");

                if (!obj.ContainsKey("exclude") || obj["exclude"] == null)
                {
                    var excludeObj = new JsonObject
                    {
                        ["files"] = @"(pnpm-lock\.yaml|uv\.lock|docs/CDISC/|docs/NCI/|docs/openapi/|docs/schema/|docs/SDLC/runs/|report.*\.xml|coverage.*\.xml|\.cadence/|\.(pdf|xlsx|xls|docx|doc|png|jpg|jpeg|gif|svg|ico|woff|woff2|eot|ttf|otf|mp4|webm|mp3|wav|zip|tar\.gz|gz|sqlite|db)$)",
                        ["lines"] = null
                    };
                    obj["exclude"] = excludeObj;
                }

                if (obj["results"] is JsonObject resultsObj)
                {
                    var cleanedResults = new JsonObject();
                    foreach (var prop in resultsObj)
                    {
                        string filename = prop.Key;
                        if (ExcludeRegex.IsMatch(filename))
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
                string formatted = obj.ToJsonString(options);
                File.WriteAllText(filepath, formatted + "\n");

                Console.WriteLine($"✔ Successfully cleaned secrets baseline at {filepath}");
                return 0;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Error cleaning secrets baseline: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
