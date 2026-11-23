using System;
using System.IO;

namespace Cadence.Topology;

/// <summary>
/// Locates the Cadence repository root directory by walking ancestor directories for <c>Cadence.slnx</c>,
/// falling back to legacy <c>Cadence.sln</c> or <c>global.json</c> for temporary test workspaces.
/// </summary>
public static class RepositoryRoot
{
    /// <summary>The primary GUID-free .NET 10 solution file at the repository root.</summary>
    public const string SolutionFileName = "Cadence.slnx";

    /// <summary>The legacy solution file name accepted in temporary test workspaces.</summary>
    public const string LegacySolutionFileName = "Cadence.sln";

    /// <summary>The .NET SDK pin file at the repository root.</summary>
    public const string GlobalJsonFileName = "global.json";

    /// <summary>
    /// Finds the repository root directory. When <paramref name="startDirectory"/> is specified, walks ancestors
    /// starting at <paramref name="startDirectory"/> and throws <see cref="InvalidOperationException"/> if no root
    /// marker is found. When <paramref name="startDirectory"/> is <see langword="null"/>, checks
    /// <see cref="Directory.GetCurrentDirectory"/> first and then <see cref="AppContext.BaseDirectory"/>, falling
    /// back to <see cref="Directory.GetCurrentDirectory"/> when neither ancestor chain contains a root marker.
    /// </summary>
    /// <param name="startDirectory">Optional starting directory to walk upward from.</param>
    /// <returns>The full path to the located repository root directory.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="startDirectory"/> is specified and no ancestor contains a root marker.
    /// </exception>
    public static string Find(string? startDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(startDirectory))
        {
            return TryFindFrom(startDirectory)
                ?? throw new InvalidOperationException(
                    $"Could not locate '{SolutionFileName}' (or '{LegacySolutionFileName}' / '{GlobalJsonFileName}') above '{startDirectory}'.");
        }

        string cwd = Directory.GetCurrentDirectory();
        return TryFindFrom(cwd)
            ?? TryFindFrom(AppContext.BaseDirectory)
            ?? cwd;
    }

    /// <summary>
    /// Attempts to locate the repository root walking upward from <paramref name="startDirectory"/>.
    /// </summary>
    /// <param name="startDirectory">The directory from which to walk upward.</param>
    /// <returns>The repository root path if found; otherwise <see langword="null"/>.</returns>
    public static string? TryFindFrom(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return null;
        }

        string? current = Path.GetFullPath(startDirectory);
        // Prefer solution markers across the entire ancestor chain before falling back to global.json so nested
        // directories never shadow an enclosing Cadence.slnx / Cadence.sln workspace.
        for (string? cursor = current; !string.IsNullOrEmpty(cursor); cursor = GetParentPath(cursor))
        {
            if (File.Exists(Path.Combine(cursor, SolutionFileName)) ||
                File.Exists(Path.Combine(cursor, LegacySolutionFileName)))
            {
                return cursor;
            }
        }

        for (string? cursor = current; !string.IsNullOrEmpty(cursor); cursor = GetParentPath(cursor))
        {
            if (File.Exists(Path.Combine(cursor, GlobalJsonFileName)))
            {
                return cursor;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the solution file name present in <paramref name="repoRoot"/>: returns <c>Cadence.slnx</c> when it
    /// exists (or when neither solution file exists), and falls back to <c>Cadence.sln</c> when only the legacy
    /// solution file exists in a temporary test directory.
    /// </summary>
    /// <param name="repoRoot">The repository root directory.</param>
    /// <returns><see cref="SolutionFileName"/> or <see cref="LegacySolutionFileName"/>.</returns>
    public static string ResolveSolutionFileName(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        if (File.Exists(Path.Combine(repoRoot, SolutionFileName)))
        {
            return SolutionFileName;
        }

        if (File.Exists(Path.Combine(repoRoot, LegacySolutionFileName)))
        {
            return LegacySolutionFileName;
        }

        return SolutionFileName;
    }

    private static string? GetParentPath(string path)
    {
        string? parent = Directory.GetParent(path)?.FullName;
        return string.Equals(parent, path, StringComparison.Ordinal) ? null : parent;
    }
}
