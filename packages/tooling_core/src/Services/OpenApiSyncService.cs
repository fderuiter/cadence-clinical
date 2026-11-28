using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Keeps <c>docs/openapi/*.json</c> equal to the OpenAPI documents the .NET services actually serve.
/// The specs are produced by <c>Cadence.OpenApiExporter</c>, which boots each service in memory.
/// </summary>
public static class OpenApiSyncService
{
    public const int DefaultMaxParallelism = 4;

    private const string ExporterProject = "packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj";

    public static int SyncOpenApiSpec(bool validate = false, string? repoRoot = null, IEnumerable<string>? targetServices = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        string specDir = Path.Combine(repoRoot, "docs", "openapi");
        string exportDir = Path.Combine(Path.GetTempPath(), "cadence-openapi-" + Guid.NewGuid().ToString("N"));

        var serviceList = targetServices?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool isTargeted = serviceList != null && serviceList.Count > 0;

        Console.WriteLine("Exporting OpenAPI specifications from the .NET services...");
        try
        {
            if (!RunExporter(repoRoot, exportDir, serviceList))
            {
                return 1;
            }

            var drift = FindDrift(exportDir, specDir, isTargeted);
            if (drift.Count == 0)
            {
                Console.WriteLine("✔ docs/openapi matches the specifications the services serve.");
                return 0;
            }

            if (validate)
            {
                Console.WriteLine("[ERROR] docs/openapi differs from the specifications the services serve:");
                foreach (string line in drift)
                {
                    Console.WriteLine($"  - {line}");
                }
                Console.WriteLine("Run 'pnpm cadence sync-openapi-spec' to update docs/openapi, then 'pnpm --filter web generate:api' to update web API types.");
                return 1;
            }

            Directory.CreateDirectory(specDir);
            foreach (string file in Directory.GetFiles(exportDir, "*.json"))
            {
                File.Copy(file, Path.Combine(specDir, Path.GetFileName(file)), overwrite: true);
            }
            if (!isTargeted)
            {
                foreach (string stale in StaleSpecs(exportDir, specDir))
                {
                    File.Delete(Path.Combine(specDir, stale));
                }
            }
            foreach (string line in drift)
            {
                Console.WriteLine($"  updated {line}");
            }
            Console.WriteLine("✔ docs/openapi synchronized. Run 'pnpm cadence generate-api-docs' to refresh docs/api and 'pnpm --filter web generate:api' to refresh the web client types.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Compares freshly exported specs with the committed ones. JSON is compared structurally, not textually.
    /// </summary>
    public static IReadOnlyList<string> FindDrift(string exportedDir, string committedDir, bool isTargetedExport = false)
    {
        var drift = new List<string>();
        foreach (string exported in Directory.GetFiles(exportedDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(exported);
            string committed = Path.Combine(committedDir, name);
            if (!File.Exists(committed))
            {
                drift.Add($"{name} (missing)");
            }
            else if (!JsonNode.DeepEquals(ParseOrNull(File.ReadAllText(exported)), ParseOrNull(File.ReadAllText(committed))))
            {
                drift.Add($"{name} (out of date)");
            }
        }

        if (!isTargetedExport)
        {
            drift.AddRange(StaleSpecs(exportedDir, committedDir).Select(name => $"{name} (no service serves it)"));
        }
        return drift;
    }

    public static bool TryParseMaxParallelism(string[] args, out int maxParallelism, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        maxParallelism = DefaultMaxParallelism;
        error = null;

        bool expectValueNext = false;
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? rawValue;
            if (expectValueNext)
            {
                rawValue = arg;
                expectValueNext = false;
            }
            else if (string.Equals(arg, "--max-parallelism", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = "Option '--max-parallelism' requires a positive integer value.";
                    return false;
                }

                expectValueNext = true;
                continue;
            }
            else if (arg.StartsWith("--max-parallelism=", StringComparison.OrdinalIgnoreCase))
            {
                rawValue = arg["--max-parallelism=".Length..];
            }
            else
            {
                continue;
            }

            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
            {
                error = $"Invalid '--max-parallelism' value '{rawValue}'. Expected an integer >= 1.";
                return false;
            }

            maxParallelism = parsed;
        }

        return true;
    }

    public static JsonObject AggregateOpenApiSpecs(
        string outputDir,
        string repoRoot,
        IReadOnlyDictionary<string, JsonObject> newlyExported)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentNullException.ThrowIfNull(newlyExported);

        var aggregatedPaths = new JsonObject();
        var aggregatedSchemas = new JsonObject();

        string fallbackDir = Path.Combine(repoRoot, "docs", "openapi");

        var specFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(outputDir))
        {
            foreach (string f in Directory.GetFiles(outputDir, "*_openapi.json"))
            {
                specFiles.Add(Path.GetFileName(f));
            }
        }

        if (Directory.Exists(fallbackDir))
        {
            foreach (string f in Directory.GetFiles(fallbackDir, "*_openapi.json"))
            {
                specFiles.Add(Path.GetFileName(f));
            }
        }

        var orderedFiles = specFiles
            .Where(f => !f.Equals("cdisc_openapi.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        foreach (string fileName in orderedFiles)
        {
            string serviceName = fileName.Replace("_openapi.json", "", StringComparison.OrdinalIgnoreCase);
            JsonObject? doc = null;

            if (newlyExported.TryGetValue(serviceName, out JsonObject? exportedDoc))
            {
                doc = exportedDoc;
            }
            else
            {
                string outputFilePath = Path.Combine(outputDir, fileName);
                string fallbackFilePath = Path.Combine(fallbackDir, fileName);
                string? targetReadPath = null;
                if (File.Exists(outputFilePath))
                {
                    targetReadPath = outputFilePath;
                }
                else if (File.Exists(fallbackFilePath))
                {
                    targetReadPath = fallbackFilePath;
                }

                if (targetReadPath != null)
                {
                    doc = ParseOrNull(File.ReadAllText(targetReadPath)) as JsonObject;
                }
            }

            if (doc == null)
            {
                continue;
            }

            if (doc["paths"] is JsonObject paths)
            {
                foreach (var pathKv in paths)
                {
                    if (!aggregatedPaths.ContainsKey(pathKv.Key))
                    {
                        aggregatedPaths[pathKv.Key] = pathKv.Value?.DeepClone();
                    }
                }
            }

            if (doc["components"] is JsonObject components && components["schemas"] is JsonObject schemas)
            {
                foreach (var schemaKv in schemas)
                {
                    if (!aggregatedSchemas.ContainsKey(schemaKv.Key))
                    {
                        aggregatedSchemas[schemaKv.Key] = schemaKv.Value?.DeepClone();
                    }
                }
            }
        }

        return new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = "Cadence Clinical Unified Gateway API",
                ["description"] = "Unified microservices API contract for Cadence Clinical Platform.\nEnforces OIDC/Keycloak authentication, RFC 7807 problem details, and ISO 14155:2020 regulatory compliance.",
                ["version"] = "1.0.0-PROD"
            },
            ["servers"] = new JsonArray
            {
                new JsonObject
                {
                    ["url"] = "https://api.ccrsoft.com/api/v1",
                    ["description"] = "Production API Gateway"
                },
                new JsonObject
                {
                    ["url"] = "http://localhost:8000/api/v1",
                    ["description"] = "Local Dev Gateway Proxy"
                }
            },
            ["paths"] = aggregatedPaths,
            ["components"] = new JsonObject
            {
                ["schemas"] = aggregatedSchemas
            }
        };
    }

    private static IEnumerable<string> StaleSpecs(string exportedDir, string committedDir)
    {
        if (!Directory.Exists(committedDir))
        {
            return [];
        }

        var exported = Directory.GetFiles(exportedDir, "*.json").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        return Directory.GetFiles(committedDir, "*.json")
            .Select(f => Path.GetFileName(f))
            .Where(name => !exported.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static JsonNode? ParseOrNull(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static bool RunExporter(string repoRoot, string exportDir, IReadOnlyList<string>? targetServices = null)
    {
        var exporterArgs = new List<string> { "--output-dir", exportDir };
        if (targetServices != null && targetServices.Count > 0)
        {
            exporterArgs.Add("--services");
            exporterArgs.Add(string.Join(",", targetServices));
        }

        return ExporterBinaryResolver.RunExporter(repoRoot, ExporterProject, exporterArgs, "OpenAPI exporter");
    }
}
