using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Streams staged Git index blobs via <c>git cat-file --batch</c> without acquiring <c>.git/index.lock</c>
/// or mutating working-tree files, and materializes disposable copies when external tools require file paths.
/// </summary>
public sealed partial class GitIndexReader : IDisposable
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

        text = Encoding.UTF8.GetString(WorkspacePrimitives.StripBom(bytes));
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
