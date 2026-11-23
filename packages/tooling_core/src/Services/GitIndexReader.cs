using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Streams staged Git index blobs via <c>git cat-file --batch</c> without acquiring <c>.git/index.lock</c>
/// or mutating working-tree files, and materializes disposable copies when external tools require file paths.
/// </summary>
public sealed class GitIndexReader : IDisposable
{
    private const int MaxCachedBlobBytes = 64 * 1024 * 1024;

    private readonly string _repoRoot;
    private readonly Dictionary<string, IndexEntry> _entries;
    private readonly HashSet<string> _directories;
    private readonly Dictionary<string, byte[]> _blobCache = new(StringComparer.Ordinal);
    private int _cachedBytes;
    private Process? _batchProcess;
    private bool _batchUnavailable;
    private bool _disposed;

    private readonly record struct IndexEntry(string Oid, string Mode);

    private GitIndexReader(
        string repoRoot,
        Dictionary<string, IndexEntry> entries,
        HashSet<string> directories,
        bool hasAuthoritativeIndex)
    {
        _repoRoot = Path.GetFullPath(repoRoot);
        _entries = entries;
        _directories = directories;
        HasAuthoritativeIndex = hasAuthoritativeIndex;
    }

    /// <summary>
    /// True when <c>repoRoot</c> is a Git repository with at least one staged index entry or an existing <c>HEAD</c> commit.
    /// False for non-Git directories or freshly initialized test repositories with an unborn <c>HEAD</c> and empty index.
    /// </summary>
    public bool HasAuthoritativeIndex { get; }

    public IReadOnlyCollection<string> Paths => _entries.Keys;

    public static bool HasGitRepository(string repoRoot)
    {
        string gitMarker = Path.Combine(repoRoot, ".git");
        return Directory.Exists(gitMarker) || File.Exists(gitMarker);
    }

    public static GitIndexReader? TryOpen(string repoRoot)
    {
        string fullRoot = Path.GetFullPath(repoRoot);
        if (!HasGitRepository(fullRoot))
        {
            return null;
        }

        StringComparer comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var entries = new Dictionary<string, IndexEntry>(comparer);
        var directories = new HashSet<string>(comparer);

        if (!TryReadStageEntries(fullRoot, entries, directories))
        {
            return null;
        }

        bool hasAuthoritativeIndex = entries.Count > 0 || HasHeadCommit(fullRoot);
        return new GitIndexReader(fullRoot, entries, directories, hasAuthoritativeIndex);
    }

    public static GitIndexReader? ResolvePreCommitReader(
        string repoRoot,
        bool preCommit,
        GitIndexReader? shared,
        out GitIndexReader? owned)
    {
        owned = null;
        if (!preCommit)
        {
            return null;
        }

        if (shared is not null)
        {
            return shared;
        }

        owned = TryOpen(repoRoot);
        return owned;
    }

    public static string? TryNormalizeRelativePath(string repoRoot, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        string fullRepoRoot = Path.GetFullPath(repoRoot);
        string fullCandidate = Path.IsPathRooted(candidatePath)
            ? Path.GetFullPath(candidatePath)
            : Path.GetFullPath(Path.Combine(fullRepoRoot, candidatePath));

        string relative = Path.GetRelativePath(fullRepoRoot, fullCandidate).Replace('\\', '/');
        if (relative == ".")
        {
            return string.Empty;
        }

        if (relative.StartsWith("../", StringComparison.Ordinal) ||
            string.Equals(relative, "..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            return null;
        }

        return relative;
    }

    public bool FileExists(string path)
    {
        string? rel = TryNormalizeRelativePath(_repoRoot, path);
        if (string.IsNullOrEmpty(rel))
        {
            return false;
        }

        if (HasAuthoritativeIndex)
        {
            return _entries.ContainsKey(rel);
        }

        return File.Exists(Path.Combine(_repoRoot, rel));
    }

    public bool DirectoryExists(string path)
    {
        string? rel = TryNormalizeRelativePath(_repoRoot, path);
        if (rel is null)
        {
            return false;
        }

        if (rel.Length == 0)
        {
            return true;
        }

        string trimmed = rel.TrimEnd('/');
        if (HasAuthoritativeIndex)
        {
            return _directories.Contains(trimmed);
        }

        return Directory.Exists(Path.Combine(_repoRoot, trimmed));
    }

    public bool TryReadBytes(string path, out byte[] bytes)
    {
        bytes = [];
        string? rel = TryNormalizeRelativePath(_repoRoot, path);
        if (string.IsNullOrEmpty(rel))
        {
            return false;
        }

        if (HasAuthoritativeIndex)
        {
            if (!_entries.TryGetValue(rel, out IndexEntry entry))
            {
                return false;
            }

            // Skip submodule gitlinks.
            if (string.Equals(entry.Mode, "160000", StringComparison.Ordinal))
            {
                return false;
            }

            return TryReadBlobByOid(entry.Oid, out bytes);
        }

        string fullPath = Path.Combine(_repoRoot, rel);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            bytes = File.ReadAllBytes(fullPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryReadText(string path, out string text)
    {
        text = string.Empty;
        if (!TryReadBytes(path, out byte[] bytes))
        {
            return false;
        }

        ReadOnlySpan<byte> span = bytes;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        text = Encoding.UTF8.GetString(span);
        return true;
    }

    public bool TryReadLines(string path, out string[] lines)
    {
        lines = [];
        if (!TryReadText(path, out string text))
        {
            return false;
        }

        lines = SplitLines(text);
        return true;
    }

    public static string[] SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var result = new List<string>();
        using var sr = new StringReader(text);
        while (sr.ReadLine() is { } line)
        {
            result.Add(line);
        }

        return [.. result];
    }

    public StagedBlobWorkspace Materialize(IEnumerable<string> relativePaths, bool createGitMarker = false)
    {
        var workspace = new StagedBlobWorkspace(createGitMarker);
        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawPath in relativePaths)
            {
                string? rel = TryNormalizeRelativePath(_repoRoot, rawPath);
                if (string.IsNullOrEmpty(rel) || !seen.Add(rel))
                {
                    continue;
                }

                if (!TryReadBytes(rel, out byte[] bytes))
                {
                    continue;
                }

                bool executable = HasAuthoritativeIndex &&
                                  _entries.TryGetValue(rel, out IndexEntry entry) &&
                                  string.Equals(entry.Mode, "100755", StringComparison.Ordinal);
                workspace.WriteBlob(rel, bytes, executable);
            }

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

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

        var visitedXmlFiles = new HashSet<string>(comparer);
        foreach (string proj in closureProjects)
        {
            visitedXmlFiles.Add(proj);
        }

        while (pending.Count > 0)
        {
            string currentFile = pending.Dequeue();
            if (!TryReadProjectXml(fullRoot, currentFile, indexReader, out string xml))
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

            string currentDir = Path.GetDirectoryName(currentFile)?.Replace('\\', '/') ?? string.Empty;
            string msbuildThisFileDir = currentDir.Length == 0 ? string.Empty : $"{currentDir}/";
            foreach (XElement element in doc.Descendants())
            {
                if (string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase))
                {
                    string? importProject = element.Attribute("Project")?.Value;
                    if (string.IsNullOrWhiteSpace(importProject))
                    {
                        continue;
                    }

                    string expandedImport = importProject
                        .Replace("$(MSBuildThisFileDirectory)", msbuildThisFileDir, StringComparison.OrdinalIgnoreCase)
                        .Replace('\\', '/');
                    string combinedImport = importProject.Contains("$(MSBuildThisFileDirectory)", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(currentDir)
                        ? expandedImport
                        : $"{currentDir}/{expandedImport}";
                    string? normalizedImport = TryNormalizeRelativePath(fullRoot, combinedImport);
                    if (string.IsNullOrEmpty(normalizedImport) ||
                        (!normalizedImport.EndsWith(".props", StringComparison.OrdinalIgnoreCase) &&
                         !normalizedImport.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }

                    closureProjects.Add(normalizedImport);
                    if (visitedXmlFiles.Add(normalizedImport))
                    {
                        pending.Enqueue(normalizedImport);
                    }

                    continue;
                }

                if (!string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? include = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                string expandedInclude = include
                    .Replace("$(MSBuildThisFileDirectory)", msbuildThisFileDir, StringComparison.OrdinalIgnoreCase)
                    .Replace('\\', '/');
                string combined = include.Contains("$(MSBuildThisFileDirectory)", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(currentDir)
                    ? expandedInclude
                    : $"{currentDir}/{expandedInclude}";
                string? referencedProject = TryNormalizeRelativePath(fullRoot, combined);
                if (string.IsNullOrEmpty(referencedProject) ||
                    !referencedProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                closureProjects.Add(referencedProject);
                if (visitedXmlFiles.Add(referencedProject))
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

    private bool TryReadBlobByOid(string oid, out byte[] bytes)
    {
        if (_blobCache.TryGetValue(oid, out byte[]? cached))
        {
            bytes = cached;
            return true;
        }

        bytes = [];
        if (_disposed || _batchUnavailable)
        {
            return false;
        }

        if (!EnsureBatchProcess())
        {
            return false;
        }

        try
        {
            Stream stdin = _batchProcess!.StandardInput.BaseStream;
            byte[] request = Encoding.ASCII.GetBytes(oid + "\n");
            stdin.Write(request, 0, request.Length);
            stdin.Flush();

            Stream stdout = _batchProcess.StandardOutput.BaseStream;
            string? header = ReadAsciiLine(stdout);
            if (string.IsNullOrEmpty(header))
            {
                ResetBatchProcess();
                return false;
            }

            // Format: "<oid> <type> <size>" or "<oid> missing"
            string[] parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 ||
                string.Equals(parts[1], "missing", StringComparison.Ordinal) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int size) ||
                size < 0)
            {
                return false;
            }

            byte[] payload = new byte[size];
            if (!ReadExact(stdout, payload))
            {
                ResetBatchProcess();
                return false;
            }

            // Consume trailing LF written by git cat-file --batch after each object payload.
            int trailing = stdout.ReadByte();
            if (trailing != '\n')
            {
                ResetBatchProcess();
                return false;
            }

            if (size <= MaxCachedBlobBytes - _cachedBytes)
            {
                _blobCache[oid] = payload;
                _cachedBytes += size;
            }

            bytes = payload;
            return true;
        }
        catch
        {
            ResetBatchProcess();
            return false;
        }
    }

    private bool EnsureBatchProcess()
    {
        if (_batchProcess is { HasExited: false })
        {
            return true;
        }

        ResetBatchProcess();

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repoRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("cat-file");
        psi.ArgumentList.Add("--batch");

        try
        {
            _batchProcess = Process.Start(psi);
            if (_batchProcess is null)
            {
                _batchUnavailable = true;
                return false;
            }

            return true;
        }
        catch
        {
            _batchUnavailable = true;
            return false;
        }
    }

    private void ResetBatchProcess()
    {
        if (_batchProcess is null)
        {
            return;
        }

        try
        {
            if (!_batchProcess.HasExited)
            {
                _batchProcess.StandardInput.BaseStream.Close();
                if (!_batchProcess.WaitForExit(1000))
                {
                    _batchProcess.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // Best-effort cleanup of helper process.
        }
        finally
        {
            _batchProcess.Dispose();
            _batchProcess = null;
        }
    }

    private static string? ReadAsciiLine(Stream stream)
    {
        var buffer = new List<byte>(80);
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            }

            if (b == '\n')
            {
                if (buffer.Count > 0 && buffer[^1] == '\r')
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }

                return Encoding.ASCII.GetString(buffer.ToArray());
            }

            buffer.Add((byte)b);
        }
    }

    private static bool ReadExact(Stream stream, byte[] destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = stream.Read(destination, offset, destination.Length - offset);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static bool TryReadStageEntries(
        string repoRoot,
        Dictionary<string, IndexEntry> entries,
        HashSet<string> directories)
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
        psi.ArgumentList.Add("--stage");
        psi.ArgumentList.Add("-z");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            using var ms = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(ms);
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return false;
            }

            ReadOnlySpan<byte> data = ms.ToArray();
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                {
                    continue;
                }

                ParseStageRecord(data[start..i], entries, directories);
                start = i + 1;
            }

            if (start < data.Length)
            {
                ParseStageRecord(data[start..], entries, directories);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ParseStageRecord(
        ReadOnlySpan<byte> recordBytes,
        Dictionary<string, IndexEntry> entries,
        HashSet<string> directories)
    {
        if (recordBytes.IsEmpty)
        {
            return;
        }

        int tabIndex = recordBytes.IndexOf((byte)'\t');
        if (tabIndex <= 0 || tabIndex + 1 >= recordBytes.Length)
        {
            return;
        }

        string meta = Encoding.ASCII.GetString(recordBytes[..tabIndex]);
        string path = Encoding.UTF8.GetString(recordBytes[(tabIndex + 1)..]).Replace('\\', '/');
        string[] metaParts = meta.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (metaParts.Length < 3)
        {
            return;
        }

        string mode = metaParts[0];
        string oid = metaParts[1];
        string stage = metaParts[2];

        // Prefer stage 0 (merged/staged); only record unmerged stage 2 ("ours") when stage 0 is absent.
        if (string.Equals(stage, "0", StringComparison.Ordinal) ||
            (string.Equals(stage, "2", StringComparison.Ordinal) && !entries.ContainsKey(path)))
        {
            entries[path] = new IndexEntry(oid, mode);
            AddAncestorDirectories(path, directories);
        }
    }

    private static void AddAncestorDirectories(string relativePath, HashSet<string> directories)
    {
        int slash = relativePath.LastIndexOf('/');
        while (slash > 0)
        {
            string dir = relativePath[..slash];
            if (!directories.Add(dir))
            {
                break;
            }

            slash = dir.LastIndexOf('/');
        }
    }

    private static bool HasHeadCommit(string repoRoot)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add("--verify");
        psi.ArgumentList.Add("-q");
        psi.ArgumentList.Add("HEAD");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ResetBatchProcess();
        _blobCache.Clear();
    }
}

/// <summary>
/// Disposable temporary directory holding materialized copies of staged Git blobs at their repository-relative paths.
/// </summary>
public sealed class StagedBlobWorkspace : IDisposable
{
    private bool _disposed;

    public StagedBlobWorkspace(bool createGitMarker = false)
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"cadence-staged-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
        if (createGitMarker)
        {
            Directory.CreateDirectory(Path.Combine(RootPath, ".git"));
        }
    }

    public string RootPath { get; }

    public string WriteBlob(string relativePath, ReadOnlySpan<byte> content, bool executable = false)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        string fullPath = Path.GetFullPath(Path.Combine(RootPath, normalized));
        if (!fullPath.StartsWith(RootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to materialize path outside workspace: {relativePath}");
        }

        string? parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(content);
        }

        if (executable && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                fullPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return fullPath;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!Directory.Exists(RootPath))
        {
            return;
        }

        try
        {
            foreach (FileInfo file in new DirectoryInfo(RootPath).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }
}
