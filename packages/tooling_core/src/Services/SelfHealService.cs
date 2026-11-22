using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Executes the <c>cadence fix</c> self-healing pipeline from any repository directory, running fast formatters,
/// baseline normalizers, and index synchronizers by default while regenerating EF Core schema documentation, OpenAPI
/// specifications, API Markdown pages, and web TypeScript API client types on demand or when relevant changes exist.
/// </summary>
public static class SelfHealService
{
    public static int ExecuteSelfHealing(
        string? repoRoot = null,
        bool full = false,
        bool openApi = false,
        Func<string, int>? syncCliDocs = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine($"Executing Self-Healing Pipeline in '{repoRoot}'...\n");

        bool ok = true;
        var changedFiles = PreCommitOpenApiService.GetChangedFilesForSelfHeal(repoRoot);

        Console.WriteLine("  1. Auto-formatting C# Codebase...");
        string slnPath = Path.Combine(repoRoot, "Cadence.sln");
        if (File.Exists(slnPath))
        {
            ok &= FormatCSharp(repoRoot, changedFiles, full);
        }
        else
        {
            Console.WriteLine($"     Cadence.sln not found in '{repoRoot}', skipping C# format.");
        }

        Console.WriteLine("\n  2. Auto-formatting Prettier Workspace Files...");
        if (File.Exists(Path.Combine(repoRoot, "package.json")))
        {
            ok &= FrontendQualityGateService.FormatPrettier(repoRoot) == 0;
        }
        else
        {
            Console.WriteLine($"     package.json not found in '{repoRoot}', skipping Prettier.");
        }

        Console.WriteLine("\n  3. Normalizing Secrets Baseline & Requirement Tags...");
        ok &= SecretsBaselineCleanerService.CleanSecretsBaseline(repoRoot: repoRoot, check: false) == 0;
        if (Directory.Exists(Path.Combine(repoRoot, "docs")))
        {
            int sanitized = ComplianceUtilityService.SanitizeRequirementTags(repoRoot, targetFiles: null, docsDir: null);
            Console.WriteLine($"✔ Requirement tags verified ({sanitized} Markdown file(s) updated).");
        }

        Console.WriteLine("\n  4. Synchronizing Documentation, CLI, Demo & ADR Indexes...");
        if (Directory.Exists(Path.Combine(repoRoot, "docs", "adr")))
        {
            ok &= AdrValidatorService.SyncAdrIndex(repoRoot) == 0;
            ok &= AdrValidatorService.ValidateAdrs(checkGitDiff: false, repoRoot: repoRoot);
        }

        if (syncCliDocs is not null &&
            File.Exists(Path.Combine(repoRoot, "docs", "cli.md")) &&
            File.Exists(Path.Combine(repoRoot, "docs", "CI.md")))
        {
            ok &= syncCliDocs(repoRoot) == 0;
        }

        if (Directory.Exists(Path.Combine(repoRoot, "docs")))
        {
            ok &= DocIndexSyncService.SyncDocIndex(check: false, repoRoot: repoRoot) == 0;
        }

        if (File.Exists(Path.Combine(repoRoot, DemoManifestGeneratorService.KernelSeedRelativePath)) &&
            File.Exists(Path.Combine(repoRoot, DemoManifestGeneratorService.ExecutionSeedRelativePath)) &&
            File.Exists(Path.Combine(repoRoot, DemoManifestGeneratorService.RealmRelativePath)))
        {
            ok &= DemoManifestGeneratorService.GenerateDemoManifest(validateOnly: false, repoRoot: repoRoot) == 0;
        }

        Console.WriteLine("\n  5. Synchronizing EF Core Schema Documentation...");
        bool shouldExportSchemaDocs = full || changedFiles.Any(PreCommitOpenApiService.AffectsEfCoreSchema);
        if (shouldExportSchemaDocs)
        {
            ok &= SchemaDocGeneratorService.GenerateSchemaDocumentation(validate: false, repoRoot: repoRoot) == 0;
        }
        else
        {
            Console.WriteLine("     No EF Core entity, configuration, or migration changes detected; skipping schema export (pass --full to force).");
        }

        Console.WriteLine("\n  6. Synchronizing OpenAPI Specifications, API Docs & Web Client Types...");
        var (backendContractsChanged, targetServices, _) = PreCommitOpenApiService.AnalyzeBackendContractChanges(changedFiles);
        bool openApiSpecsChangedOnDisk = changedFiles.Any(f =>
            f.StartsWith("docs/openapi/", StringComparison.OrdinalIgnoreCase) &&
            f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        bool exportedOpenApi = false;
        if (full || openApi)
        {
            ok &= OpenApiSyncService.SyncOpenApiSpec(validate: false, repoRoot) == 0;
            ok &= ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot) == 0;
            exportedOpenApi = true;
        }
        else if (backendContractsChanged)
        {
            ok &= OpenApiSyncService.SyncOpenApiSpec(validate: false, repoRoot, targetServices) == 0;
            ok &= ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot) == 0;
            exportedOpenApi = true;
        }
        else
        {
            Console.WriteLine("     No backend API contract changes detected; skipping OpenAPI service export (pass --openapi or --full to force).");
            if (Directory.Exists(Path.Combine(repoRoot, "docs", "openapi")))
            {
                ok &= ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot) == 0;
            }
        }

        if (File.Exists(Path.Combine(repoRoot, "apps", "web", "package.json")) &&
            (exportedOpenApi || openApiSpecsChangedOnDisk || WebApiTypesNeedRefresh(repoRoot)))
        {
            Console.WriteLine("     Regenerating web OpenAPI TypeScript client types...");
            string pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";
            ok &= FrontendQualityGateService.RunProcess(repoRoot, pnpm, ["--filter", "web", "run", "generate:api"]) == 0;
        }

        Console.WriteLine(ok ? "\nSelf-Healing Pipeline Complete." : "\n[ERROR] Self-Healing Pipeline finished with failures (see above).");
        return ok ? 0 : 1;
    }

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();

    private static bool FormatCSharp(string repoRoot, IReadOnlyList<string> changedFiles, bool full)
    {
        bool isGitRepo = Directory.Exists(Path.Combine(repoRoot, ".git")) || File.Exists(Path.Combine(repoRoot, ".git"));
        if (!full && isGitRepo)
        {
            var csFiles = changedFiles
                .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.IsPathRooted(f) ? Path.GetRelativePath(repoRoot, f) : f)
                .Where(f => File.Exists(Path.Combine(repoRoot, f)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (csFiles.Count == 0)
            {
                Console.WriteLine("     No changed C# files detected; skipping C# format (pass --full to format Cadence.sln).");
                return true;
            }

            var whitespaceArgs = new List<string> { "format", "whitespace", ".", "--folder", "--include" };
            whitespaceArgs.AddRange(csFiles);
            bool ok = FrontendQualityGateService.RunProcess(repoRoot, "dotnet", whitespaceArgs) == 0;

            var groups = csFiles.GroupBy(f => PreCommitOpenApiService.FindNearestCsproj(repoRoot, f), StringComparer.Ordinal).ToList();
            if (groups.Count >= 3)
            {
                var slnArgs = new List<string> { "format", "style", "Cadence.sln", "--no-restore", "--include" };
                slnArgs.AddRange(csFiles);
                ok &= FrontendQualityGateService.RunProcess(repoRoot, "dotnet", slnArgs) == 0;
                return ok;
            }

            foreach (var group in groups)
            {
                string workspace = group.Key ?? "Cadence.sln";
                var styleArgs = new List<string> { "format", "style", workspace, "--no-restore", "--include" };
                styleArgs.AddRange(group);
                ok &= FrontendQualityGateService.RunProcess(repoRoot, "dotnet", styleArgs) == 0;
            }

            return ok;
        }

        bool whitespaceOk = FrontendQualityGateService.RunProcess(repoRoot, "dotnet", ["format", "whitespace", "Cadence.sln", "--no-restore"]) == 0;
        bool styleOk = FrontendQualityGateService.RunProcess(repoRoot, "dotnet", ["format", "style", "Cadence.sln", "--no-restore"]) == 0;
        return whitespaceOk && styleOk;
    }

    private static bool WebApiTypesNeedRefresh(string repoRoot)
    {
        string pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";
        return FrontendQualityGateService.RunProcess(repoRoot, pnpm, ["--filter", "web", "run", "generate:api", "--", "--check"], suppressOutput: true) != 0;
    }
}
