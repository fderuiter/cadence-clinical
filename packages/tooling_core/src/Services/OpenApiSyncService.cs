using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Keeps <c>docs/openapi/*.json</c> equal to the OpenAPI documents the .NET services actually serve.
/// The specs are produced by <c>Cadence.OpenApiExporter</c>, which boots each service in memory.
/// Files under <c>docs/openapi/legacy/</c> are the Python-era contract and are not touched.
/// </summary>
public static class OpenApiSyncService
{
    private const string ExporterProject = "packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj";

    public static int SyncOpenApiSpec(bool validate = false, string? repoRoot = null, IEnumerable<string>? targetServices = null)
    {
        repoRoot ??= FindRepoRoot();
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
    /// Compares freshly exported specs with the committed ones. Only top-level files are compared,
    /// so <c>docs/openapi/legacy/</c> is ignored. JSON is compared structurally, not textually.
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

        return ExporterBuildFreshness.RunDotnetExporter(repoRoot, ExporterProject, exporterArgs, "OpenAPI exporter");
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
