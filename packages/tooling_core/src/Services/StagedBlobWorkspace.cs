using System;
using System.IO;

namespace Cadence.ToolingCore.Services;

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
