using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Checks whether a .NET exporter binary (<c>Cadence.OpenApiExporter</c> or <c>Cadence.SchemaDocExporter</c>)
/// already exists and strictly postdates all inputs in its transitive <c>&lt;ProjectReference&gt;</c> closure so
/// <c>dotnet run --no-build</c> can skip redundant MSBuild evaluation and compilation.
/// </summary>
public static class ExporterBuildFreshness
{
    private const string DefaultTargetFramework = "net10.0";
    private const string DefaultConfiguration = "Debug";

    private static readonly string[] AncestorMsBuildFiles =
    [
        "Cadence.sln",
        "global.json",
        "NuGet.Config",
        "BannedSymbols.txt",
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        ".editorconfig",
        ".globalconfig",
    ];

    public static bool CanSkipBuild(string repoRoot, string exporterProjectRelativePath)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || string.IsNullOrWhiteSpace(exporterProjectRelativePath))
        {
            return false;
        }

        string fullRepoRoot = Path.GetFullPath(repoRoot);
        string? normalizedExporter = GitIndexReader.TryNormalizeRelativePath(fullRepoRoot, exporterProjectRelativePath);
        if (string.IsNullOrEmpty(normalizedExporter))
        {
            return false;
        }

        string exporterCsprojPath = Path.Combine(fullRepoRoot, normalizedExporter);
        if (!File.Exists(exporterCsprojPath))
        {
            return false;
        }

        string? exporterDir = Path.GetDirectoryName(exporterCsprojPath);
        if (string.IsNullOrEmpty(exporterDir))
        {
            return false;
        }

        (string assemblyName, string targetFramework) = ReadExporterIdentity(fullRepoRoot, exporterCsprojPath);
        string binDir = Path.Combine(exporterDir, "bin", DefaultConfiguration, targetFramework);
        string dllPath = Path.Combine(binDir, $"{assemblyName}.dll");
        string depsPath = Path.Combine(binDir, $"{assemblyName}.deps.json");
        string runtimeConfigPath = Path.Combine(binDir, $"{assemblyName}.runtimeconfig.json");
        string exporterAssetsPath = Path.Combine(exporterDir, "obj", "project.assets.json");

        if (!File.Exists(dllPath) ||
            !File.Exists(depsPath) ||
            !File.Exists(runtimeConfigPath) ||
            !File.Exists(exporterAssetsPath))
        {
            return false;
        }

        DateTime binaryWriteTimeUtc = File.GetLastWriteTimeUtc(dllPath);
        if (!GitIndexReader.TryResolveProjectClosure(fullRepoRoot, [normalizedExporter], indexReader: null, out HashSet<string> closureProjects) ||
            closureProjects.Count == 0)
        {
            return false;
        }

        var checkedAncestorDirs = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (string relativeProject in closureProjects)
        {
            string fullProjectPath = Path.Combine(fullRepoRoot, relativeProject);
            string? projectDir = Path.GetDirectoryName(fullProjectPath);
            if (string.IsNullOrEmpty(projectDir) || !Directory.Exists(projectDir))
            {
                return false;
            }

            string projectAssetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
            if (!File.Exists(projectAssetsPath) || File.GetLastWriteTimeUtc(projectAssetsPath) >= binaryWriteTimeUtc)
            {
                return false;
            }

            if (HasAnyInputAtOrAfter(projectDir, binaryWriteTimeUtc) ||
                HasExternalProjectItemAtOrAfter(fullRepoRoot, fullProjectPath, projectDir, binaryWriteTimeUtc) ||
                HasAncestorMsBuildFileAtOrAfter(fullRepoRoot, projectDir, checkedAncestorDirs, binaryWriteTimeUtc))
            {
                return false;
            }
        }

        return true;
    }

    public static bool RunDotnetExporter(
        string repoRoot,
        string exporterProjectRelativePath,
        IReadOnlyList<string> exporterArguments,
        string exporterDisplayName)
    {
        bool skipBuild = CanSkipBuild(repoRoot, exporterProjectRelativePath);
        var args = new List<string>(exporterArguments.Count + 5) { "run" };
        if (skipBuild)
        {
            args.Add("--no-build");
        }

        args.Add("--project");
        args.Add(exporterProjectRelativePath);
        args.Add("--");
        args.AddRange(exporterArguments);

        return ExporterBinaryResolver.ExecuteProcess(repoRoot, "dotnet", args, exporterDisplayName);
    }

    private static (string AssemblyName, string TargetFramework) ReadExporterIdentity(string fullRepoRoot, string csprojPath)
    {
        string defaultAssemblyName = Path.GetFileNameWithoutExtension(csprojPath);
        string? assemblyName = null;
        string? targetFramework = null;

        if (TryLoadXml(csprojPath, out XDocument? projectDoc) && projectDoc is not null)
        {
            assemblyName = FirstPropertyValue(projectDoc, "AssemblyName");
            targetFramework = FirstPropertyValue(projectDoc, "TargetFramework");
        }

        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            string rootProps = Path.Combine(fullRepoRoot, "Directory.Build.props");
            if (File.Exists(rootProps) && TryLoadXml(rootProps, out XDocument? propsDoc) && propsDoc is not null)
            {
                targetFramework = FirstPropertyValue(propsDoc, "TargetFramework");
            }
        }

        return (
            string.IsNullOrWhiteSpace(assemblyName) ? defaultAssemblyName : assemblyName.Trim(),
            string.IsNullOrWhiteSpace(targetFramework) ? DefaultTargetFramework : targetFramework.Trim());
    }

    private static string? FirstPropertyValue(XDocument doc, string localName) =>
        doc.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static bool TryLoadXml(string path, out XDocument? doc)
    {
        try
        {
            doc = XDocument.Load(path, LoadOptions.None);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            doc = null;
            return false;
        }
    }

    private static bool HasAnyInputAtOrAfter(string directory, DateTime binaryWriteTimeUtc)
    {
        var pending = new Stack<string>();
        pending.Push(directory);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current))
            {
                if (File.GetLastWriteTimeUtc(file) >= binaryWriteTimeUtc)
                {
                    return true;
                }
            }

            foreach (string subDir in Directory.EnumerateDirectories(current))
            {
                string dirName = Path.GetFileName(subDir);
                if (string.Equals(dirName, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dirName, "obj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(subDir);
            }
        }

        return false;
    }

    private static bool HasExternalProjectItemAtOrAfter(
        string fullRepoRoot,
        string fullProjectPath,
        string projectDir,
        DateTime binaryWriteTimeUtc)
    {
        if (!TryLoadXml(fullProjectPath, out XDocument? doc) || doc is null)
        {
            return true;
        }

        foreach (XElement element in doc.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "EmbeddedResource", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(element.Name.LocalName, "Content", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(element.Name.LocalName, "Compile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? include = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include) || include.Contains('*', StringComparison.Ordinal) || include.Contains("$(", StringComparison.Ordinal))
            {
                continue;
            }

            string candidate = Path.GetFullPath(Path.Combine(projectDir, include.Replace('\\', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(fullRepoRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                continue;
            }

            if (!File.Exists(candidate) || File.GetLastWriteTimeUtc(candidate) >= binaryWriteTimeUtc)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAncestorMsBuildFileAtOrAfter(
        string fullRepoRoot,
        string projectDir,
        HashSet<string> checkedAncestorDirs,
        DateTime binaryWriteTimeUtc)
    {
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string? current = projectDir;
        while (!string.IsNullOrEmpty(current) && current.StartsWith(fullRepoRoot, pathComparison))
        {
            if (checkedAncestorDirs.Add(current))
            {
                foreach (string fileName in AncestorMsBuildFiles)
                {
                    string candidate = Path.Combine(current, fileName);
                    if (File.Exists(candidate) && File.GetLastWriteTimeUtc(candidate) >= binaryWriteTimeUtc)
                    {
                        return true;
                    }
                }
            }

            if (string.Equals(current, fullRepoRoot, pathComparison))
            {
                break;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return false;
    }
}
