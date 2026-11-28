using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

public static partial class PreCommitOpenApiService
{
    public static List<string> ResolveTargetClosureProjects(
        string repoRoot,
        IReadOnlyList<string> existingFiles,
        List<string>? targetServices,
        out bool requireRepoWideCheck)
    {
        requireRepoWideCheck = false;
        var projects = new HashSet<string>(StringComparer.Ordinal);

        foreach (string csFile in existingFiles.Where(IsCsharpFile))
        {
            string? nearest = FindNearestCsproj(repoRoot, csFile);
            if (string.IsNullOrEmpty(nearest))
            {
                requireRepoWideCheck = true;
                return [];
            }

            projects.Add(nearest);
        }

        if (targetServices is not null)
        {
            foreach (string service in targetServices)
            {
                string? serviceProject = TryFindServiceCsproj(repoRoot, service);
                if (string.IsNullOrEmpty(serviceProject))
                {
                    requireRepoWideCheck = true;
                    return [];
                }

                projects.Add(serviceProject);

                if (IsKernelHostedModuleService(service))
                {
                    string? hostProject = TryFindServiceCsproj(repoRoot, "kernel");
                    if (!string.IsNullOrEmpty(hostProject))
                    {
                        projects.Add(hostProject);
                    }
                }
            }
        }
        else
        {
            projects.Add(OpenApiExporterProject);
        }

        if (projects.Count == 0)
        {
            requireRepoWideCheck = true;
        }

        return [.. projects];
    }

    private static bool IsKernelHostedModuleService(string service) =>
        string.Equals(service, "execution_forms", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(service, "intelligence", StringComparison.OrdinalIgnoreCase);

    private static string? TryFindServiceCsproj(string repoRoot, string service)
    {
        string srcDir;
        if (string.Equals(service, "execution_forms", StringComparison.OrdinalIgnoreCase))
        {
            srcDir = Path.Combine(repoRoot, "apps", "execution", "module");
        }
        else
        {
            string appDir = string.Equals(service, "kernel", StringComparison.OrdinalIgnoreCase)
                ? "kernel-api"
                : service.ToLowerInvariant();
            srcDir = Path.Combine(repoRoot, "apps", appDir, "src");
        }

        if (!Directory.Exists(srcDir))
        {
            return null;
        }

        string[] csprojs = Directory.GetFiles(srcDir, "*.csproj");
        return csprojs.Length == 1
            ? Path.GetRelativePath(repoRoot, csprojs[0]).Replace('\\', '/')
            : null;
    }

    public static bool IsGlobalOpenApiBuildFile(string file)
    {
        string normalized = file.Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);
        return string.Equals(fileName, "Cadence.sln", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "global.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Directory.Build.targets", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Directory.Packages.props", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCsharpFile(string file)
    {
        if (IsGlobalOpenApiBuildFile(file))
        {
            return true;
        }

        string normalized = file.Replace('\\', '/');
        return normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKernelHostedModuleProject(string relativeCsproj) =>
        IsKernelHostedModuleService(ResolveServiceFromPath(relativeCsproj) ?? string.Empty);

    private static bool IsKernelHostProject(string relativeCsproj) =>
        string.Equals(ResolveServiceFromPath(relativeCsproj), "kernel", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> DiscoverAllProjects(string fullRepoRoot, GitIndexReader? indexReader)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var projects = new HashSet<string>(comparer);

        if (indexReader is { HasAuthoritativeIndex: true })
        {
            foreach (string path in indexReader.Paths)
            {
                if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    projects.Add(path.Replace('\\', '/'));
                }
            }

            if (projects.Count > 0)
            {
                return projects;
            }
        }

        if (!Directory.Exists(fullRepoRoot))
        {
            return projects;
        }

        var pendingDirs = new Stack<string>();
        pendingDirs.Push(fullRepoRoot);
        while (pendingDirs.Count > 0)
        {
            string currentDir = pendingDirs.Pop();
            foreach (string csproj in Directory.EnumerateFiles(currentDir, "*.csproj"))
            {
                projects.Add(Path.GetRelativePath(fullRepoRoot, csproj).Replace('\\', '/'));
            }

            foreach (string subDir in Directory.EnumerateDirectories(currentDir))
            {
                string name = Path.GetFileName(subDir);
                if (name.StartsWith('.') ||
                    string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pendingDirs.Push(subDir);
            }
        }

        return projects;
    }

    private static Dictionary<string, List<string>> BuildReverseProjectReferenceGraph(
        string fullRepoRoot,
        HashSet<string> allProjects,
        GitIndexReader? indexReader)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var reverse = new Dictionary<string, List<string>>(comparer);

        foreach (string project in allProjects)
        {
            if (!TryReadProjectText(fullRepoRoot, project, indexReader, out string xml))
            {
                continue;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml, LoadOptions.None);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            string projectDir = Path.GetDirectoryName(project)?.Replace('\\', '/') ?? string.Empty;
            foreach (XElement element in doc.Descendants())
            {
                if (!string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? include = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                string normalizedInclude = include.Replace('\\', '/');
                string combined = string.IsNullOrEmpty(projectDir)
                    ? normalizedInclude
                    : $"{projectDir}/{normalizedInclude}";
                string? targetProject = GitIndexReader.TryNormalizeRelativePath(fullRepoRoot, combined);
                if (string.IsNullOrEmpty(targetProject) || !targetProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!reverse.TryGetValue(targetProject, out List<string>? dependents))
                {
                    dependents = [];
                    reverse[targetProject] = dependents;
                }

                dependents.Add(project);
            }
        }

        return reverse;
    }

    private static bool TryReadProjectText(
        string fullRepoRoot,
        string relativeCsproj,
        GitIndexReader? indexReader,
        out string xml)
    {
        if (indexReader is { HasAuthoritativeIndex: true } && indexReader.TryReadText(relativeCsproj, out xml))
        {
            return true;
        }

        string diskPath = Path.Combine(fullRepoRoot, relativeCsproj);
        if (File.Exists(diskPath))
        {
            xml = File.ReadAllText(diskPath);
            return true;
        }

        xml = string.Empty;
        return false;
    }

    private static string? FindNearestCsprojInSet(string fullRepoRoot, string relativeFile, HashSet<string> allProjects)
    {
        if (relativeFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && allProjects.Contains(relativeFile))
        {
            return relativeFile;
        }

        string? dir = Path.GetDirectoryName(relativeFile)?.Replace('\\', '/');
        while (!string.IsNullOrEmpty(dir))
        {
            string prefix = dir + "/";
            string? singleMatch = null;
            int count = 0;
            foreach (string proj in allProjects)
            {
                if (proj.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
                    proj.IndexOf('/', prefix.Length) < 0)
                {
                    singleMatch = proj;
                    count++;
                    if (count > 1)
                    {
                        break;
                    }
                }
            }

            if (count == 1)
            {
                return singleMatch;
            }

            int lastSlash = dir.LastIndexOf('/');
            dir = lastSlash > 0 ? dir[..lastSlash] : null;
        }

        return FindNearestCsproj(fullRepoRoot, relativeFile)?.Replace('\\', '/');
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
            "execution" => parts.Length >= 3 && parts[2].Equals("module", StringComparison.OrdinalIgnoreCase)
                ? "execution_forms"
                : "execution",
            "fileshare" => "fileshare",
            "imaging" => "imaging",
            "intelligence" => "intelligence",
            "interop" => "interop",
            "kernel-api" or "kernel" => "kernel",
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
}
