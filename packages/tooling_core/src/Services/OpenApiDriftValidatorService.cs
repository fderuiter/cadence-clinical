using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates that committed <c>docs/openapi/*.json</c> specifications match live .NET endpoints, declare non-empty
/// <c>operationId</c> and <c>summary</c> metadata on every operation, and stay in sync with <c>docs/api/*.md</c>.
/// </summary>
public static class OpenApiDriftValidatorService
{
    private static readonly string[] HttpMethods =
    [
        "get",
        "put",
        "post",
        "delete",
        "options",
        "head",
        "patch",
        "trace",
    ];

    public static int ValidateDrift(string? repoRoot = null)
    {
        repoRoot ??= PreCommitOpenApiService.FindRepoRoot();
        string specDir = Path.Combine(repoRoot, "docs", "openapi");

        Console.WriteLine("--- Running Native OpenAPI Specification & Documentation Drift Sentinel ---");

        Console.WriteLine("\n[GATE 1/3] Checking docs/openapi against the .NET services...");
        int resSpec = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot);

        Console.WriteLine("\n[GATE 2/3] Checking OpenAPI operationId and summary completeness...");
        int resMetadata = ValidateContractCompleteness(specDir);

        Console.WriteLine("\n[GATE 3/3] Checking docs/api against docs/openapi...");
        int resDocs = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);

        if (resSpec != 0 || resMetadata != 0 || resDocs != 0)
        {
            Console.WriteLine("\n[ERROR] OpenAPI Specification Drift or Incomplete Contract Detected!");
            return 1;
        }

        Console.WriteLine("\n[SUCCESS] OpenAPI Specification Drift Sentinel passed cleanly.");
        return 0;
    }

    public static int ValidateContractCompleteness(string specDir)
    {
        var gaps = FindOperationMetadataGaps(specDir);
        if (gaps.Count == 0)
        {
            Console.WriteLine("✔ Every OpenAPI operation declares a non-empty operationId and summary.");
            return 0;
        }

        Console.WriteLine($"[ERROR] OpenAPI operations missing operationId or summary ({gaps.Count}):");
        foreach (string gap in gaps)
        {
            Console.WriteLine($"  - {gap}");
        }

        Console.WriteLine("Add .WithName(...) and .WithSummary(...) on each endpoint and run 'pnpm cadence sync-openapi-spec'.");
        return 1;
    }

    /// <summary>
    /// Scans top-level OpenAPI JSON specifications in <paramref name="specDir"/> (excluding <c>legacy/</c>) and reports
    /// any HTTP operation missing a non-whitespace <c>operationId</c> or <c>summary</c>.
    /// </summary>
    public static List<string> FindOperationMetadataGaps(string specDir)
    {
        var gaps = new List<string>();
        if (!Directory.Exists(specDir))
        {
            gaps.Add("docs/openapi directory is missing");
            return gaps;
        }

        var specFiles = Directory
            .GetFiles(specDir, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (string specPath in specFiles)
        {
            string fileName = Path.GetFileName(specPath);
            JsonObject? doc;
            try
            {
                doc = JsonNode.Parse(File.ReadAllText(specPath)) as JsonObject;
            }
            catch (Exception ex)
            {
                gaps.Add($"{fileName}: invalid JSON ({ex.Message})");
                continue;
            }

            if (doc?["paths"] is not JsonObject paths)
            {
                continue;
            }

            foreach (var (routePath, pathItemNode) in paths.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (pathItemNode is not JsonObject pathItem)
                {
                    continue;
                }

                foreach (string method in HttpMethods)
                {
                    if (pathItem[method] is not JsonObject op)
                    {
                        continue;
                    }

                    string methodUpper = method.ToUpperInvariant();
                    string? operationId = op["operationId"]?.GetValue<string>();
                    string? summary = op["summary"]?.GetValue<string>();

                    if (string.IsNullOrWhiteSpace(operationId))
                    {
                        gaps.Add($"{fileName}: {methodUpper} {routePath} is missing operationId");
                    }

                    if (string.IsNullOrWhiteSpace(summary))
                    {
                        string opLabel = string.IsNullOrWhiteSpace(operationId) ? "<unnamed>" : operationId.Trim();
                        gaps.Add($"{fileName}: {methodUpper} {routePath} ({opLabel}) is missing summary");
                    }
                }
            }
        }

        return gaps;
    }
}
