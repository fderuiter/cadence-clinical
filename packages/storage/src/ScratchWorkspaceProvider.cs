using System;
using System.IO;

namespace Cadence.Storage;

/// <summary>
/// Configuration options for <see cref="IScratchWorkspaceProvider"/>, bound from the <c>Scratch</c> configuration section.
/// </summary>
public sealed record ScratchWorkspaceOptions
{
    /// <summary>
    /// Fully qualified directory path on a dedicated volume where transient working files and per-run sandbox
    /// directories are created with owner-only (<c>0700</c>) permissions. Required.
    /// </summary>
    public string RootPath { get; init; } = "";
}

/// <summary>
/// Allocates isolated temporary files and per-run working directories confined to a validated, owner-only
/// <c>Scratch:RootPath</c> directory so services never write to unmanaged OS temporary directories.
/// </summary>
public interface IScratchWorkspaceProvider
{
    /// <summary>Canonical fully qualified root directory for this scratch workspace.</summary>
    string RootPath { get; }

    /// <summary>
    /// Atomically creates a new owner-only (<c>0600</c> on Unix) scratch file inside <see cref="RootPath"/> and returns
    /// its full path and open <see cref="FileStream"/>.
    /// </summary>
    (string Path, FileStream Stream) CreateFile(
        string prefix = "scratch",
        string extension = ".tmp",
        FileAccess access = FileAccess.ReadWrite,
        int bufferSize = 65_536,
        FileOptions options = FileOptions.Asynchronous);

    /// <summary>
    /// Creates a new owner-only (<c>0700</c> on Unix) subdirectory inside <see cref="RootPath"/> and returns its full path.
    /// </summary>
    string CreateDirectory(string prefix = "run");
}

/// <summary>
/// Filesystem-backed <see cref="IScratchWorkspaceProvider"/> enforcing an absolute, non-symlink, owner-only (<c>0700</c>)
/// scratch root separate from the shared OS temporary directory.
/// </summary>
public sealed class FileSystemScratchWorkspaceProvider : IScratchWorkspaceProvider
{
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private const UnixFileMode GroupOrOtherPermissions =
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _rootWithSeparator;

    /// <summary>
    /// Initializes and validates the scratch workspace root configured in <paramref name="options"/>.
    /// </summary>
    public FileSystemScratchWorkspaceProvider(ScratchWorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.RootPath) || !Path.IsPathFullyQualified(options.RootPath))
        {
            throw new InvalidOperationException(
                "Scratch:RootPath must be set to a fully qualified directory path.");
        }

        if (ContainsTraversalSegment(options.RootPath))
        {
            throw new InvalidOperationException(
                "Scratch:RootPath must not contain parent-directory ('..') traversal segments.");
        }

        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath));
        string? pathRoot = Path.GetPathRoot(fullRoot);
        if (!string.IsNullOrEmpty(pathRoot) &&
            string.Equals(Path.TrimEndingDirectorySeparator(pathRoot), fullRoot, PathComparison))
        {
            throw new InvalidOperationException(
                "Scratch:RootPath must be a dedicated directory, not a filesystem root.");
        }

        string systemTempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (string.Equals(fullRoot, systemTempRoot, PathComparison))
        {
            throw new InvalidOperationException(
                "Scratch:RootPath must be a dedicated owner-only directory, not the shared system temporary directory.");
        }

        EnsureValidatedOwnerOnlyDirectory(fullRoot);

        RootPath = fullRoot;
        _rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;
    }

    /// <inheritdoc />
    public string RootPath { get; }

    /// <inheritdoc />
    public (string Path, FileStream Stream) CreateFile(
        string prefix = "scratch",
        string extension = ".tmp",
        FileAccess access = FileAccess.ReadWrite,
        int bufferSize = 65_536,
        FileOptions options = FileOptions.Asynchronous)
    {
        string safePrefix = ValidateToken(prefix, nameof(prefix));
        string normalizedExtension = NormalizeExtension(extension);

        EnsureValidatedOwnerOnlyDirectory(RootPath);

        string fileName = $"{safePrefix}-{Guid.NewGuid():N}{normalizedExtension}";
        string fullPath = ResolveConfinedChildPath(fileName);

        var streamOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = access,
            Share = FileShare.None,
            BufferSize = bufferSize,
            Options = options,
        };

        if (!OperatingSystem.IsWindows())
        {
            streamOptions.UnixCreateMode = OwnerOnlyFileMode;
        }

        var stream = new FileStream(fullPath, streamOptions);
        return (fullPath, stream);
    }

    /// <inheritdoc />
    public string CreateDirectory(string prefix = "run")
    {
        string safePrefix = ValidateToken(prefix, nameof(prefix));

        EnsureValidatedOwnerOnlyDirectory(RootPath);

        string dirName = $"{safePrefix}-{Guid.NewGuid():N}";
        string fullPath = ResolveConfinedChildPath(dirName);

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(fullPath);
        }
        else
        {
            Directory.CreateDirectory(fullPath, OwnerOnlyDirectoryMode);
            File.SetUnixFileMode(fullPath, OwnerOnlyDirectoryMode);
        }

        RejectReparsePoint(fullPath);
        return fullPath;
    }

    private string ResolveConfinedChildPath(string childName)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_rootWithSeparator, childName));
        if (!fullPath.StartsWith(_rootWithSeparator, PathComparison))
        {
            throw new InvalidOperationException("Allocated scratch path escaped Scratch:RootPath.");
        }

        return fullPath;
    }

    private static void EnsureValidatedOwnerOnlyDirectory(string fullRoot)
    {
        if (File.Exists(fullRoot))
        {
            throw new InvalidOperationException(
                $"Scratch:RootPath '{fullRoot}' points to an existing file rather than a directory.");
        }

        if (Directory.Exists(fullRoot))
        {
            RejectReparsePoint(fullRoot);
            ValidateUnixOwnerOnlyPermissions(fullRoot);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(fullRoot);
        }
        else
        {
            string? parent = Path.GetDirectoryName(fullRoot);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }

            Directory.CreateDirectory(fullRoot, OwnerOnlyDirectoryMode);
            File.SetUnixFileMode(fullRoot, OwnerOnlyDirectoryMode);
        }

        RejectReparsePoint(fullRoot);
        ValidateUnixOwnerOnlyPermissions(fullRoot);
    }

    private static void RejectReparsePoint(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Scratch:RootPath '{path}' must not be a symbolic link or reparse point.");
        }
    }

    private static void ValidateUnixOwnerOnlyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(path);
        if ((mode & GroupOrOtherPermissions) != 0)
        {
            throw new InvalidOperationException(
                $"Scratch:RootPath '{path}' must have owner-only permissions (0700); group or other permissions were present ({mode}).");
        }

        if ((mode & OwnerOnlyDirectoryMode) != OwnerOnlyDirectoryMode)
        {
            throw new InvalidOperationException(
                $"Scratch:RootPath '{path}' must grant read, write, and execute permissions (0700) to its owner (got {mode}).");
        }
    }

    private static bool ContainsTraversalSegment(string rawPath)
    {
        string[] segments = rawPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static string ValidateToken(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Scratch name prefix must not be empty.", paramName);
        }

        string trimmed = value.Trim();
        if (trimmed is "." or "..")
        {
            throw new ArgumentException("Scratch name prefix must not be '.' or '..'.", paramName);
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c) || c is '/' or '\\' or ':')
            {
                throw new ArgumentException($"Scratch name prefix '{value}' contains invalid path characters.", paramName);
            }
        }

        return trimmed;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        string trimmed = extension.Trim();
        if (!trimmed.StartsWith('.'))
        {
            trimmed = "." + trimmed;
        }

        if (trimmed is "." or "..")
        {
            throw new ArgumentException($"Scratch file extension '{extension}' is invalid.", nameof(extension));
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c) || c is '/' or '\\' or ':')
            {
                throw new ArgumentException($"Scratch file extension '{extension}' contains invalid path characters.", nameof(extension));
            }
        }

        return trimmed;
    }
}
