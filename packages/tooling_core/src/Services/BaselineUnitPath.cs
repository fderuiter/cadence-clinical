using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Maps repository-relative paths to their owning baseline shard unit (<c>apps/&lt;name&gt;</c>,
/// <c>packages/&lt;name&gt;</c>, or <c>root</c>) and removes empty parent directories when shards shrink to zero.
/// </summary>
public static class BaselineUnitPath
{
    public const string RootUnit = "root";

    public static string ResolveUnit(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            (string.Equals(parts[0], "apps", StringComparison.Ordinal) ||
             string.Equals(parts[0], "packages", StringComparison.Ordinal)))
        {
            return $"{parts[0]}/{parts[1]}";
        }

        return RootUnit;
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
