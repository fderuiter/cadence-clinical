using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class PreCommitOpenApiService
{
    private const string OpenApiExporterProject = "packages/tooling_core/src/Cadence.OpenApiExporter/Cadence.OpenApiExporter.csproj";

    /// <summary>
    /// The <c>openapi</c> gate. When <paramref name="targetFiles"/> is provided or <paramref name="quick"/> is <c>true</c>,
    /// exports specs only for affected services and skips service host startup when no C# backend contracts are touched.
    /// </summary>
    public static int ExecutePreCommitOpenApi(
        string? repoRoot = null,
        bool quick = false,
        IEnumerable<string>? targetFiles = null,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();

        if (!quick && targetFiles is null)
        {
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

        bool useIndexScope = quick || indexReader is not null;
        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit: useIndexScope, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var existingFiles = files
                .Select(f => GitIndexReader.TryNormalizeRelativePath(repoRoot, f))
                .Where(rel => !string.IsNullOrEmpty(rel) &&
                              (reader is not null ? reader.FileExists(rel) : File.Exists(Path.Combine(repoRoot, rel))))
                .Select(rel => rel!)
                .ToList();

            string scopeLabel = useIndexScope ? "staged" : "targeted";
            var (shouldExport, targetServices, hasCsharpFiles) = AnalyzeBackendContractChanges(existingFiles, repoRoot, reader);
            if (!hasCsharpFiles)
            {
                Console.WriteLine($"No {scopeLabel} C# backend changes detected. Skipping service startup (lightweight docs check)...");
                return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot, preCommit: useIndexScope, indexReader: reader);
            }

            if (!shouldExport)
            {
                Console.WriteLine($"{char.ToUpperInvariant(scopeLabel[0])}{scopeLabel[1..]} C# changes do not affect microservice API contracts. Skipping service startup...");
                return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot, preCommit: useIndexScope, indexReader: reader);
            }

            if (targetServices is not null)
            {
                Console.WriteLine($"{char.ToUpperInvariant(scopeLabel[0])}{scopeLabel[1..]} C# changes detected for microservice(s): {string.Join(", ", targetServices)}");
            }
            else
            {
                Console.WriteLine($"{char.ToUpperInvariant(scopeLabel[0])}{scopeLabel[1..]} global MSBuild/solution changes detected. Triggering full 17-service OpenAPI export...");
            }

            if (useIndexScope)
            {
                var closureRoots = ResolveTargetClosureProjects(repoRoot, existingFiles, targetServices, out bool requireRepoWideCheck);
                List<string> overlappingUnstaged = requireRepoWideCheck
                    ? GitIndexReader.GetOverlappingUnstagedPaths(
                        repoRoot,
                        static p => GitIndexReader.IsMsBuildInputPath(p) || IsOpenApiDocPath(p))
                    : GitIndexReader.GetOverlappingUnstagedProjectClosurePaths(
                        repoRoot,
                        closureRoots,
                        reader,
                        IsOpenApiDocPath);
                if (overlappingUnstaged.Count > 0)
                {
                    string sample = string.Join(", ", overlappingUnstaged.Take(5));
                    Console.WriteLine($"[ERROR] Unstaged or untracked working-tree edits overlap MSBuild/OpenAPI inputs ({sample}). Stage or stash them before committing so staged OpenAPI contracts can be verified fail-closed.");
                    return 1;
                }
            }

            int syncResult = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot, targetServices);
            int docsResult = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot, preCommit: useIndexScope, indexReader: reader);
            return syncResult != 0 || docsResult != 0 ? 1 : 0;
        }
    }

    public static (bool ShouldExport, List<string>? TargetServices, bool HasCsharpFiles) AnalyzeBackendContractChanges(
        IEnumerable<string> files,
        string? repoRoot = null,
        GitIndexReader? indexReader = null)
    {
        var backendFiles = files.Where(IsCsharpFile).ToList();
        if (backendFiles.Count == 0)
        {
            return (false, null, false);
        }

        if (backendFiles.Any(IsGlobalOpenApiBuildFile))
        {
            return (true, null, true);
        }

        string effectiveRepoRoot = Path.GetFullPath(repoRoot ?? FindRepoRoot());
        var allProjects = DiscoverAllProjects(effectiveRepoRoot, indexReader);
        var reverseRefs = BuildReverseProjectReferenceGraph(effectiveRepoRoot, allProjects, indexReader);

        StringComparer pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seedProjects = new HashSet<string>(pathComparer);
        var detectedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in backendFiles)
        {
            string? normalized = GitIndexReader.TryNormalizeRelativePath(effectiveRepoRoot, file);
            string candidate = string.IsNullOrEmpty(normalized) ? file.Replace('\\', '/') : normalized;
            string? nearestCsproj = FindNearestCsprojInSet(effectiveRepoRoot, candidate, allProjects);
            if (!string.IsNullOrEmpty(nearestCsproj))
            {
                seedProjects.Add(nearestCsproj);
                continue;
            }

            string? fallbackService = ResolveServiceFromPath(candidate);
            if (!string.IsNullOrEmpty(fallbackService))
            {
                detectedServices.Add(fallbackService);
            }
        }

        var visited = new HashSet<string>(pathComparer);
        var queue = new Queue<string>();
        foreach (string seed in seedProjects)
        {
            if (visited.Add(seed))
            {
                queue.Enqueue(seed);
            }
        }

        while (queue.Count > 0)
        {
            string currentProject = queue.Dequeue();
            string? service = ResolveServiceFromPath(currentProject);
            if (!string.IsNullOrEmpty(service))
            {
                detectedServices.Add(service);
            }

            if (!reverseRefs.TryGetValue(currentProject, out List<string>? dependents))
            {
                continue;
            }

            bool currentIsKernelModule = IsKernelHostedModuleProject(currentProject);
            foreach (string dependent in dependents)
            {
                if (currentIsKernelModule && IsKernelHostProject(dependent))
                {
                    continue;
                }

                if (visited.Add(dependent))
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (detectedServices.Count == 0)
        {
            return (false, null, true);
        }

        var orderedServices = detectedServices.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        return (true, orderedServices, true);
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

    private static bool IsOpenApiDocPath(string relativePath) =>
        relativePath.StartsWith("docs/openapi/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("docs/api/", StringComparison.OrdinalIgnoreCase);

    public static List<string>? TryGetTrackedFiles(string repoRoot) =>
        GitRevisionScopeService.TryGetTrackedFiles(repoRoot);

    public static List<string>? TryGetChangedFilesSinceBase(
        string repoRoot,
        string baseRef = GitRevisionScopeService.DefaultBaseRef,
        string? pushBefore = null) =>
        GitRevisionScopeService.TryGetChangedFilesSinceBase(repoRoot, baseRef, pushBefore);

    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();
}
