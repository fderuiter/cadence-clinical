using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Maps repository-relative paths to their owning baseline shard paths (<c>&lt;gate&gt;/files/&lt;source-path&gt;&lt;suffix&gt;</c>
/// or legacy workspace units) and removes empty parent directories when shards shrink to zero.
/// </summary>
public static class BaselineUnitPath
{
    public const string RootUnit = "root";
    public const string FilesSubdirectory = "files";

    public static string NormalizeRepoRelativePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    public static string ResolvePerFileShardRelativePath(string gateDirectory, string sourceRelativePath, string shardSuffix)
    {
        string normalizedGate = NormalizeRepoRelativePath(gateDirectory).TrimEnd('/');
        string normalizedSource = NormalizeRepoRelativePath(sourceRelativePath);
        if (!IsSafeRelativeFilePath(normalizedSource))
        {
            throw new ArgumentException($"Invalid repository-relative file path '{sourceRelativePath}'.", nameof(sourceRelativePath));
        }

        return $"{normalizedGate}/{FilesSubdirectory}/{normalizedSource}{shardSuffix}";
    }

    public static bool TryExtractPerFileSourcePath(
        string shardRelativePath,
        string gateDirectory,
        string shardSuffix,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        string normalizedShard = NormalizeRepoRelativePath(shardRelativePath);
        string normalizedGate = NormalizeRepoRelativePath(gateDirectory).TrimEnd('/');
        string prefix = $"{normalizedGate}/{FilesSubdirectory}/";

        if (!normalizedShard.StartsWith(prefix, StringComparison.Ordinal) ||
            !normalizedShard.EndsWith(shardSuffix, StringComparison.Ordinal) ||
            normalizedShard.Length <= prefix.Length + shardSuffix.Length)
        {
            return false;
        }

        string candidate = normalizedShard[prefix.Length..^shardSuffix.Length];
        if (!IsSafeRelativeFilePath(candidate))
        {
            return false;
        }

        sourceRelativePath = candidate;
        return true;
    }

    public static string ResolveUnit(string relativePath)
    {
        string normalized = NormalizeRepoRelativePath(relativePath);

        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            (string.Equals(parts[0], "apps", StringComparison.Ordinal) ||
             string.Equals(parts[0], "packages", StringComparison.Ordinal)))
        {
            return $"{parts[0]}/{parts[1]}";
        }

        return RootUnit;
    }

    private static bool IsSafeRelativeFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.EndsWith('/') ||
            path.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = path.Split('/');
        return segments.All(seg => seg.Length > 0 && seg != "." && seg != "..");
    }

    public static void DeleteEmptyParentDirectories(string deletedFilePath, string stopDirectory)
    {
        string normalizedStop = Path.GetFullPath(stopDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? current = Path.GetDirectoryName(Path.GetFullPath(deletedFilePath));

        while (!string.IsNullOrEmpty(current))
        {
            string trimmedCurrent = current.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(trimmedCurrent, normalizedStop, StringComparison.Ordinal) ||
                !trimmedCurrent.StartsWith(normalizedStop + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                break;
            }

            if (!Directory.Exists(trimmedCurrent) || Directory.EnumerateFileSystemEntries(trimmedCurrent).Any())
            {
                break;
            }

            Directory.Delete(trimmedCurrent, recursive: false);
            current = Path.GetDirectoryName(trimmedCurrent);
        }
    }
}
