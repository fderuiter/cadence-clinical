using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class PreCommitOpenApiService
{
    /// <summary>
    /// The <c>openapi</c> gate. When staged C# files exist, exports specs for affected services.
    /// When no C# files are staged, skips service host startup for sub-second execution.
    /// </summary>
    public static int ExecutePreCommitOpenApi(
        string? repoRoot = null,
        bool quick = false,
        IEnumerable<string>? targetFiles = null,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();

        if (!quick)
        {
            if (targetFiles is not null)
            {
                var explicitList = targetFiles.ToList();
                if (explicitList.Count == 0)
                {
                    return 0;
                }
            }

            return OpenApiDriftValidatorService.ValidateDrift(repoRoot);
        }

        List<string> files;
        if (targetFiles is not null)
        {
            files = targetFiles.ToList();
            if (files.Count == 0)
            {
                return 0;
            }
        }
        else
        {
            files = GetStagedFiles(repoRoot);
            if (files.Count == 0)
            {
                return 0;
            }
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit: true, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var existingFiles = files
                .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                .Where(rel => !string.IsNullOrEmpty(rel) &&
                              (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
                .Select(rel => rel!)
                .ToList();

            var (shouldExport, targetServices, hasCsharpFiles) = AnalyzeBackendContractChanges(existingFiles);
            if (!hasCsharpFiles)
            {
                Console.WriteLine("No staged C# backend changes detected. Skipping service startup (lightweight docs check)...");
                return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot, preCommit: true, indexReader: reader);
            }

            if (!shouldExport)
            {
                Console.WriteLine("Staged C# changes do not affect microservice API contracts. Skipping service startup...");
                return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot, preCommit: true, indexReader: reader);
            }

            if (targetServices is not null)
            {
                Console.WriteLine($"Staged C# changes detected for microservice(s): {string.Join(", ", targetServices)}");
            }
            else
            {
                Console.WriteLine("Staged C# changes detected in shared packages/solution files. Triggering multi-service OpenAPI export...");
            }

            var overlappingUnstaged = GitIndexReader.GetOverlappingUnstagedPaths(
                repoRoot,
                p => GitIndexReader.IsMsBuildInputPath(p) ||
                     p.StartsWith("docs/openapi/", StringComparison.OrdinalIgnoreCase) ||
                     p.StartsWith("docs/api/", StringComparison.OrdinalIgnoreCase));
            if (overlappingUnstaged.Count > 0)
            {
                string sample = string.Join(", ", overlappingUnstaged.Take(5));
                Console.WriteLine($"[ERROR] Unstaged or untracked working-tree edits overlap MSBuild/OpenAPI inputs ({sample}). Stage or stash them before committing so staged OpenAPI contracts can be verified fail-closed.");
                return 1;
            }

            int syncResult = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot, targetServices);
            int docsResult = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot, preCommit: true, indexReader: reader);
            return syncResult != 0 || docsResult != 0 ? 1 : 0;
        }
    }

    public static (bool ShouldExport, List<string>? TargetServices, bool HasCsharpFiles) AnalyzeBackendContractChanges(IEnumerable<string> files)
    {
        var csharpFiles = files.Where(IsCsharpFile).ToList();
        if (csharpFiles.Count == 0)
        {
            return (false, null, false);
        }

        if (csharpFiles.Any(IsSharedOrSolutionFile))
        {
            return (true, null, true);
        }

        var detectedServices = csharpFiles
            .Select(ResolveServiceFromPath)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return detectedServices.Count > 0
            ? (true, detectedServices, true)
            : (false, null, true);
    }

    public static bool AffectsEfCoreSchema(string file)
    {
        string normalized = file.Replace('\\', '/');
        if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileNameWithoutExtension(normalized);

        return normalized.Contains("/migrations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("migrations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/configurations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("configurations/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/entities/", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("entities/", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("migration", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("configuration", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("entities", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("entity", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains("dbcontext", StringComparison.OrdinalIgnoreCase);
    }

    public static List<string> GetChangedFilesForSelfHeal(string repoRoot)
    {
        if (!GitRevisionScopeService.HasGitRepository(repoRoot))
        {
            return [];
        }

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in GitRevisionScopeService.GetWorkingTreeChanges(repoRoot, includeDeletions: false, includeUntracked: true))
        {
            changed.Add(file);
        }

        foreach (string file in GetStagedFiles(repoRoot))
        {
            changed.Add(file);
        }

        foreach (string file in TryGetChangedFilesSinceBase(repoRoot) ?? [])
        {
            changed.Add(file);
        }

        return [.. changed];
    }

    public static List<string> GetStagedFiles(string repoRoot) =>
        GitRevisionScopeService.GetStagedFiles(repoRoot, includeDeletions: false);

    private static bool IsCsharpFile(string file)
    {
        string normalized = file.Replace('\\', '/');
        return normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedOrSolutionFile(string file)
    {
        string normalized = file.Replace('\\', '/');

        if (normalized.StartsWith("packages/cli/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("packages/tooling_core/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("packages/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/apphost/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/gateway/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalized.Contains('/') ||
            normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static string? ResolveServiceFromPath(string file)
    {
        string normalized = file.Replace('\\', '/');
        if (!normalized.StartsWith("apps/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        string appFolder = parts[1].ToLowerInvariant();
        return appFolder switch
        {
            "ctms" => "ctms",
            "designer" => "designer",
            "econsent" => "econsent",
            "eisf" => "eisf",
            "etmf" => "etmf",
            "execution" => "execution",
            "fileshare" => "fileshare",
            "imaging" => "imaging",
            "interop" => "interop",
            "kernel-api" or "kernel" or "intelligence" => "kernel",
            "knowledge" => "knowledge",
            "notifications" => "notifications",
            "org" => "org",
            "payments" => "payments",
            "quality" => "quality",
            "safety" => "safety",
            "tickets" => "tickets",
            _ => null
        };
    }

    public static List<string>? TryGetTrackedFiles(string repoRoot) =>
        GitRevisionScopeService.TryGetTrackedFiles(repoRoot);

    public static List<string>? TryGetChangedFilesSinceBase(
        string repoRoot,
        string baseRef = GitRevisionScopeService.DefaultBaseRef,
        string? pushBefore = null) =>
        GitRevisionScopeService.TryGetChangedFilesSinceBase(repoRoot, baseRef, pushBefore);

    public static string? FindNearestCsproj(string repoRoot, string relativeFile)
    {
        string fullRepoRoot = Path.GetFullPath(repoRoot);
        string? dir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(fullRepoRoot, relativeFile)));
        while (!string.IsNullOrEmpty(dir) && dir.StartsWith(fullRepoRoot, StringComparison.Ordinal))
        {
            string[] projects = Directory.GetFiles(dir, "*.csproj");
            if (projects.Length == 1)
            {
                return Path.GetRelativePath(fullRepoRoot, projects[0]);
            }

            if (string.Equals(dir, fullRepoRoot, StringComparison.Ordinal))
            {
                break;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    public static string FindRepoRoot()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            string current = start;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == current) break;
                current = parent ?? "";
            }
        }

        return Directory.GetCurrentDirectory();
    }
}
