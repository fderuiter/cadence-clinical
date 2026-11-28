using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

public sealed partial class GitIndexReader
{
    /// <summary>
    /// Returns normalized repository-relative paths that have unstaged working-tree edits or are untracked
    /// (excluding gitignored paths). Used by MSBuild-backed pre-commit gates to fail closed when unstaged edits
    /// overlap project inputs.
    /// </summary>
    public static HashSet<string> GetUnstagedOrUntrackedPaths(string repoRoot)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new HashSet<string>(comparer);
        string fullRoot = Path.GetFullPath(repoRoot);
        if (!HasGitRepository(fullRoot) || !HasHeadCommit(fullRoot) && !HasAnyStagedEntry(fullRoot))
        {
            return paths;
        }

        CollectNullTerminatedGitPaths(fullRoot, paths, "--no-optional-locks", "diff", "--name-only", "-z");
        CollectNullTerminatedGitPaths(fullRoot, paths, "--no-optional-locks", "ls-files", "--others", "--exclude-standard", "-z");
        return paths;
    }

    public static List<string> GetOverlappingUnstagedPaths(string repoRoot, Func<string, bool> predicate) =>
        GetUnstagedOrUntrackedPaths(repoRoot)
            .Where(predicate)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Resolves the transitive <c>ProjectReference</c> closure of <paramref name="rootProjects"/> and returns
    /// unstaged or untracked paths that overlap any project directory in that closure, root/ancestor MSBuild
    /// configuration files, or <paramref name="extraPredicate"/>. Fails closed across all MSBuild inputs when
    /// any root or referenced <c>.csproj</c> cannot be resolved.
    /// </summary>
    public static List<string> GetOverlappingUnstagedProjectClosurePaths(
        string repoRoot,
        IEnumerable<string> rootProjects,
        GitIndexReader? indexReader = null,
        Func<string, bool>? extraPredicate = null)
    {
        HashSet<string> unstagedPaths = GetUnstagedOrUntrackedPaths(repoRoot);
        if (unstagedPaths.Count == 0)
        {
            return [];
        }

        if (!TryResolveProjectClosure(repoRoot, rootProjects, indexReader, out HashSet<string> closureProjects))
        {
            return unstagedPaths
                .Where(p => IsMsBuildInputPath(p) || (extraPredicate is not null && extraPredicate(p)))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var projectDirs = new HashSet<string>(comparer);
        var ancestorDirs = new HashSet<string>(comparer) { string.Empty };

        foreach (string csproj in closureProjects)
        {
            string projDir = Path.GetDirectoryName(csproj)?.Replace('\\', '/') ?? string.Empty;
            projectDirs.Add(projDir);

            string cursor = projDir;
            while (!string.IsNullOrEmpty(cursor))
            {
                int slash = cursor.LastIndexOf('/');
                cursor = slash > 0 ? cursor[..slash] : string.Empty;
                ancestorDirs.Add(cursor);
            }
        }

        return unstagedPaths
            .Where(p =>
                (extraPredicate is not null && extraPredicate(p)) ||
                IsMsBuildClosureInputPath(p, closureProjects, projectDirs, ancestorDirs, pathComparison))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static bool TryResolveProjectClosure(
        string repoRoot,
        IEnumerable<string> rootProjects,
        GitIndexReader? indexReader,
        out HashSet<string> closureProjects)
    {
        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        closureProjects = new HashSet<string>(comparer);
        string fullRoot = Path.GetFullPath(repoRoot);
        var pending = new Queue<string>();

        foreach (string rawProject in rootProjects)
        {
            string? normalized = TryNormalizeRelativePath(fullRoot, rawProject);
            if (string.IsNullOrEmpty(normalized) ||
                !normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (closureProjects.Add(normalized))
            {
                pending.Enqueue(normalized);
            }
        }

        if (pending.Count == 0)
        {
            return false;
        }

        while (pending.Count > 0)
        {
            string currentCsproj = pending.Dequeue();
            if (!TryReadProjectXml(fullRoot, currentCsproj, indexReader, out string xml))
            {
                return false;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml, LoadOptions.None);
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }

            string currentDir = Path.GetDirectoryName(currentCsproj)?.Replace('\\', '/') ?? string.Empty;
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
                string combined = string.IsNullOrEmpty(currentDir)
                    ? normalizedInclude
                    : $"{currentDir}/{normalizedInclude}";
                string? referencedProject = TryNormalizeRelativePath(fullRoot, combined);
                if (string.IsNullOrEmpty(referencedProject) ||
                    !referencedProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (closureProjects.Add(referencedProject))
                {
                    pending.Enqueue(referencedProject);
                }
            }
        }

        return true;
    }

    public static bool IsMsBuildInputPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".editorconfig", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".globalconfig", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string fileName = Path.GetFileName(normalized);
        return string.Equals(fileName, "BannedSymbols.txt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "global.json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "NuGet.Config", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMsBuildClosureInputPath(
        string relativePath,
        HashSet<string> closureProjects,
        HashSet<string> projectDirs,
        HashSet<string> ancestorDirs,
        StringComparison pathComparison)
    {
        if (!IsMsBuildInputPath(relativePath))
        {
            return false;
        }

        string normalized = relativePath.Replace('\\', '/');
        if (closureProjects.Contains(normalized))
        {
            return true;
        }

        string fileDir = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
        if (fileDir.Length == 0)
        {
            return true;
        }

        if (ancestorDirs.Contains(fileDir) &&
            !normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
            !normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string projectDir in projectDirs)
        {
            if (projectDir.Length == 0 ||
                string.Equals(fileDir, projectDir, pathComparison) ||
                normalized.StartsWith(projectDir + "/", pathComparison))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadProjectXml(
        string fullRepoRoot,
        string relativeCsproj,
        GitIndexReader? indexReader,
        out string xml)
    {
        if (indexReader is { HasAuthoritativeIndex: true })
        {
            return indexReader.TryReadText(relativeCsproj, out xml);
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

    private static bool HasAnyStagedEntry(string repoRoot)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("ls-files");
        psi.ArgumentList.Add("-z");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            int firstByte = process.StandardOutput.BaseStream.ReadByte();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && firstByte >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CollectNullTerminatedGitPaths(
        string repoRoot,
        HashSet<string> destination,
        params string[] gitArgs)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in gitArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return;
            }

            using var ms = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(ms);
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return;
            }

            ReadOnlySpan<byte> bytes = ms.ToArray();
            int start = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                {
                    continue;
                }

                if (i > start)
                {
                    destination.Add(Encoding.UTF8.GetString(bytes[start..i]).Replace('\\', '/'));
                }

                start = i + 1;
            }

            if (start < bytes.Length)
            {
                destination.Add(Encoding.UTF8.GetString(bytes[start..]).Replace('\\', '/'));
            }
        }
        catch
        {
            // Ignore git invocation errors in non-standard environments.
        }
    }
}
