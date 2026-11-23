using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Resolves fresh prebuilt exporter assemblies under <c>bin/Debug/net10.0</c> or <c>bin/Release/net10.0</c>
/// by comparing compiled binary timestamps against C# and MSBuild inputs across <c>apps/</c>, <c>packages/</c>,
/// and repository-root MSBuild configuration files, falling back to <c>dotnet run --project</c> when missing or stale.
/// </summary>
public static class ExporterBinaryResolver
{
    private const string TargetFramework = "net10.0";

    private static readonly string[] Configurations = ["Debug", "Release"];

    private static readonly string[] RootMsBuildFiles =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "global.json",
        "NuGet.Config",
        "BannedSymbols.txt",
        ".editorconfig",
        ".globalconfig",
    ];

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".cadence",
        ".claude",
        ".venv",
        "venv",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "coverage",
        "TestResults",
        "test-results",
        "playwright-report",
    };

    public sealed record ExporterInvocation(
        string FileName,
        IReadOnlyList<string> Arguments,
        bool ReusedPrebuilt,
        string? PrebuiltDllPath);

    public static ExporterInvocation ResolveInvocation(
        string repoRoot,
        string exporterProjectRelativePath,
        IReadOnlyList<string> exporterArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(exporterProjectRelativePath);
        ArgumentNullException.ThrowIfNull(exporterArguments);

        if (TryFindFreshExporterDll(repoRoot, exporterProjectRelativePath, out string? dllPath) &&
            !string.IsNullOrEmpty(dllPath))
        {
            var directArgs = new List<string>(exporterArguments.Count + 1) { dllPath };
            directArgs.AddRange(exporterArguments);
            return new ExporterInvocation("dotnet", directArgs, ReusedPrebuilt: true, PrebuiltDllPath: dllPath);
        }

        var fallbackArgs = new List<string>(exporterArguments.Count + 4)
        {
            "run",
            "--project",
            exporterProjectRelativePath,
            "--",
        };
        fallbackArgs.AddRange(exporterArguments);
        return new ExporterInvocation("dotnet", fallbackArgs, ReusedPrebuilt: false, PrebuiltDllPath: null);
    }

    public static bool TryFindFreshExporterDll(
        string repoRoot,
        string exporterProjectRelativePath,
        out string? freshDllPath)
    {
        freshDllPath = null;
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(exporterProjectRelativePath))
        {
            return false;
        }

        string fullRepoRoot = Path.GetFullPath(repoRoot);
        string? normalizedExporterCsproj = GitIndexReader.TryNormalizeRelativePath(fullRepoRoot, exporterProjectRelativePath);
        if (string.IsNullOrEmpty(normalizedExporterCsproj))
        {
            return false;
        }

        string fullCsprojPath = Path.Combine(fullRepoRoot, normalizedExporterCsproj);
        if (!File.Exists(fullCsprojPath))
        {
            return false;
        }

        string exporterAssemblyName = ReadAssemblyName(fullCsprojPath);
        string exporterProjectDir = Path.GetDirectoryName(fullCsprojPath)!;

        var candidates = new List<(string BinDir, string DllPath, DateTime ExporterWriteUtc, DateTime MaxDllWriteUtc)>();
        foreach (string configuration in Configurations)
        {
            string binDir = Path.Combine(exporterProjectDir, "bin", configuration, TargetFramework);
            string dllPath = Path.Combine(binDir, $"{exporterAssemblyName}.dll");
            string depsPath = Path.Combine(binDir, $"{exporterAssemblyName}.deps.json");
            string runtimeConfigPath = Path.Combine(binDir, $"{exporterAssemblyName}.runtimeconfig.json");

            if (!File.Exists(dllPath) || !File.Exists(depsPath) || !File.Exists(runtimeConfigPath))
            {
                continue;
            }

            DateTime exporterWriteUtc = File.GetLastWriteTimeUtc(dllPath);
            DateTime maxDllWriteUtc = exporterWriteUtc;
            foreach (string dll in Directory.EnumerateFiles(binDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                DateTime writeUtc = File.GetLastWriteTimeUtc(dll);
                if (writeUtc > maxDllWriteUtc)
                {
                    maxDllWriteUtc = writeUtc;
                }
            }

            candidates.Add((binDir, dllPath, exporterWriteUtc, maxDllWriteUtc));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        DateTime latestRootConfigWriteUtc = GetLatestRootConfigWriteTimeUtc(fullRepoRoot);
        bool hasClosure = GitIndexReader.TryResolveProjectClosure(
            fullRepoRoot,
            [normalizedExporterCsproj],
            indexReader: null,
            out HashSet<string> closureProjects) && closureProjects.Count > 1;

        List<ClosureProjectMetadata> closureMetadata = hasClosure
            ? BuildClosureMetadata(fullRepoRoot, closureProjects)
            : [];

        InputTimestamps inputs = ScanSourceTreeInputs(fullRepoRoot, closureMetadata);

        string? selectedDll = null;
        DateTime selectedTimestamp = DateTime.MinValue;

        foreach (var candidate in candidates)
        {
            if (!IsCandidateFresh(candidate.BinDir, candidate.ExporterWriteUtc, candidate.MaxDllWriteUtc, latestRootConfigWriteUtc, closureMetadata, inputs))
            {
                continue;
            }

            if (selectedDll is null || candidate.MaxDllWriteUtc > selectedTimestamp)
            {
                selectedDll = candidate.DllPath;
                selectedTimestamp = candidate.MaxDllWriteUtc;
            }
        }

        if (selectedDll is null)
        {
            return false;
        }

        freshDllPath = selectedDll;
        return true;
    }

    private static bool IsCandidateFresh(
        string binDir,
        DateTime exporterWriteUtc,
        DateTime maxDllWriteUtc,
        DateTime latestRootConfigWriteUtc,
        IReadOnlyList<ClosureProjectMetadata> closureMetadata,
        InputTimestamps inputs)
    {
        if (latestRootConfigWriteUtc > exporterWriteUtc)
        {
            return false;
        }

        if (inputs.LatestAncestorConfigWriteUtc > exporterWriteUtc)
        {
            return false;
        }

        if (closureMetadata.Count > 0)
        {
            foreach (ClosureProjectMetadata project in closureMetadata)
            {
                string projectDll = Path.Combine(binDir, $"{project.AssemblyName}.dll");
                if (!File.Exists(projectDll))
                {
                    return false;
                }

                DateTime projectDllWriteUtc = File.GetLastWriteTimeUtc(projectDll);
                if (inputs.LatestByClosureProject.TryGetValue(project.RelativeProjectDir, out DateTime latestProjectInputUtc) &&
                    latestProjectInputUtc > projectDllWriteUtc)
                {
                    return false;
                }
            }

            return inputs.LatestUnmappedInputWriteUtc <= maxDllWriteUtc;
        }

        return inputs.LatestUnmappedInputWriteUtc <= exporterWriteUtc;
    }

    private static DateTime GetLatestRootConfigWriteTimeUtc(string fullRepoRoot)
    {
        DateTime latest = DateTime.MinValue;
        foreach (string fileName in RootMsBuildFiles)
        {
            string fullPath = Path.Combine(fullRepoRoot, fileName);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            DateTime writeUtc = File.GetLastWriteTimeUtc(fullPath);
            if (writeUtc > latest)
            {
                latest = writeUtc;
            }
        }

        return latest;
    }

    private static List<ClosureProjectMetadata> BuildClosureMetadata(string fullRepoRoot, HashSet<string> closureProjects)
    {
        var list = new List<ClosureProjectMetadata>(closureProjects.Count);
        foreach (string relCsproj in closureProjects)
        {
            string fullCsproj = Path.Combine(fullRepoRoot, relCsproj);
            string relDir = Path.GetDirectoryName(relCsproj)?.Replace('\\', '/') ?? string.Empty;
            string assemblyName = ReadAssemblyName(fullCsproj);
            list.Add(new ClosureProjectMetadata(relDir, assemblyName));
        }

        // Order longest directory prefix first so nested projects (e.g. packages/tooling_core/src/Cadence.OpenApiExporter
        // inside packages/tooling_core/src) bind to their most specific owning project.
        list.Sort(static (a, b) => b.RelativeProjectDir.Length.CompareTo(a.RelativeProjectDir.Length));
        return list;
    }

    private static InputTimestamps ScanSourceTreeInputs(
        string fullRepoRoot,
        IReadOnlyList<ClosureProjectMetadata> closureMetadata)
    {
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var latestByProject = new Dictionary<string, DateTime>(comparer);
        DateTime latestUnmapped = DateTime.MinValue;
        DateTime latestAncestorConfig = DateTime.MinValue;

        foreach (string rootSubdir in new[] { "apps", "packages" })
        {
            string startDir = Path.Combine(fullRepoRoot, rootSubdir);
            if (!Directory.Exists(startDir))
            {
                continue;
            }

            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(startDir));

            while (stack.Count > 0)
            {
                DirectoryInfo current = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try
                {
                    entries = current.EnumerateFileSystemInfos();
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (FileSystemInfo entry in entries)
                {
                    if (entry is DirectoryInfo subDir)
                    {
                        if (!ShouldSkipDirectory(subDir.Name))
                        {
                            stack.Push(subDir);
                        }

                        continue;
                    }

                    string relativePath = Path.GetRelativePath(fullRepoRoot, entry.FullName).Replace('\\', '/');
                    if (!GitIndexReader.IsMsBuildInputPath(relativePath))
                    {
                        continue;
                    }

                    DateTime writeUtc = entry.LastWriteTimeUtc;
                    if (closureMetadata.Count > 0)
                    {
                        string? matchedProjectDir = MatchClosureProjectDir(relativePath, closureMetadata, pathComparison);
                        if (matchedProjectDir is not null)
                        {
                            if (!latestByProject.TryGetValue(matchedProjectDir, out DateTime existing) || writeUtc > existing)
                            {
                                latestByProject[matchedProjectDir] = writeUtc;
                            }

                            continue;
                        }

                        if (IsSharedDirectoryConfigFile(relativePath))
                        {
                            if (writeUtc > latestAncestorConfig)
                            {
                                latestAncestorConfig = writeUtc;
                            }

                            continue;
                        }
                    }

                    if (writeUtc > latestUnmapped)
                    {
                        latestUnmapped = writeUtc;
                    }
                }
            }
        }

        return new InputTimestamps(latestByProject, latestUnmapped, latestAncestorConfig);
    }

    private static string? MatchClosureProjectDir(
        string relativeFilePath,
        IReadOnlyList<ClosureProjectMetadata> sortedClosureMetadata,
        StringComparison pathComparison)
    {
        foreach (ClosureProjectMetadata candidate in sortedClosureMetadata)
        {
            if (candidate.RelativeProjectDir.Length == 0)
            {
                return candidate.RelativeProjectDir;
            }

            if (relativeFilePath.StartsWith(candidate.RelativeProjectDir + "/", pathComparison))
            {
                return candidate.RelativeProjectDir;
            }
        }

        return null;
    }

    private static bool IsSharedDirectoryConfigFile(string relativePath) =>
        !relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        !relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldSkipDirectory(string name)
    {
        if (ExcludedDirectoryNames.Contains(name))
        {
            return true;
        }

        return name.StartsWith('.') && !string.Equals(name, ".config", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadAssemblyName(string fullCsprojPath)
    {
        string fallback = Path.GetFileNameWithoutExtension(fullCsprojPath);
        if (!File.Exists(fullCsprojPath))
        {
            return fallback;
        }

        try
        {
            XDocument doc = XDocument.Load(fullCsprojPath);
            string? explicitName = doc.Descendants()
                .FirstOrDefault(static e => string.Equals(e.Name.LocalName, "AssemblyName", StringComparison.OrdinalIgnoreCase))
                ?.Value
                ?.Trim();
            if (!string.IsNullOrWhiteSpace(explicitName) && !explicitName.Contains("$(", StringComparison.Ordinal))
            {
                return explicitName;
            }
        }
        catch
        {
            // Fall back to project filename when project XML cannot be parsed.
        }

        return fallback;
    }

    private sealed record ClosureProjectMetadata(string RelativeProjectDir, string AssemblyName);

    private sealed record InputTimestamps(
        Dictionary<string, DateTime> LatestByClosureProject,
        DateTime LatestUnmappedInputWriteUtc,
        DateTime LatestAncestorConfigWriteUtc);
}
