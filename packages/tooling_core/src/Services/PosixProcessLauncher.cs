using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Starts external CLI processes safely with <c>UseShellExecute = false</c>, resolving Windows <c>.exe</c>,
/// <c>.cmd</c>, and <c>.bat</c> shims in <c>PATH</c> directory order, retrying transient POSIX <c>ETXTBSY</c>
/// races on freshly written executables, and falling back to <c>/bin/sh</c> on POSIX <c>ENOEXEC</c> for
/// shebang-less script wrappers such as standalone <c>pnpm</c>.
/// </summary>
public static class PosixProcessLauncher
{
    /// <summary>POSIX <c>ENOEXEC</c> (<c>Exec format error</c>) native error code.</summary>
    public const int PosixEnoexec = 8;

    /// <summary>POSIX <c>ETXTBSY</c> (<c>Text file busy</c>) native error code.</summary>
    public const int PosixEtxtbsy = 26;

    private const int ErrorFileNotFound = 2;

    private static readonly int[] EtxtbsyRetryDelaysMs = [1, 2, 5, 10, 20, 50];

    private static readonly string[] WindowsExecutableExtensions = [".exe", ".cmd", ".bat"];

    /// <summary>
    /// Starts a process configured from <paramref name="template"/>, honoring custom <c>PATH</c> overrides in
    /// <see cref="ProcessStartInfo.Environment"/>, resolving Windows <c>.exe</c>/<c>.cmd</c>/<c>.bat</c> shims,
    /// retrying transient POSIX <c>ETXTBSY</c> races, and falling back to <c>/bin/sh</c> when a POSIX executable
    /// lacks a kernel <c>#!</c> header (<c>ENOEXEC</c>).
    /// </summary>
    /// <param name="template">The initial <see cref="ProcessStartInfo"/> describing the command to run.</param>
    /// <returns>The started <see cref="Process"/>, or <see langword="null"/> if no process resource was started.</returns>
    public static Process? Start(ProcessStartInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.UseShellExecute)
        {
            throw new ArgumentException("PosixProcessLauncher requires UseShellExecute = false.", nameof(template));
        }

        bool isWindows = OperatingSystem.IsWindows();
        bool hasEnvPath = TryGetPathFromDictionary(template.Environment, isWindows, out string? envPath);
        string? resolved = ResolveExecutablePathCore(
            template.FileName,
            template.WorkingDirectory,
            hasPathOverride: true,
            pathOverride: hasEnvPath ? envPath : null,
            isWindows: isWindows);

        if (resolved is null)
        {
            throw new Win32Exception(ErrorFileNotFound, $"Executable '{template.FileName}' was not found on PATH.");
        }

        ProcessStartInfo startInfo = CloneStartInfo(template, resolved, prependArgument: null);

        try
        {
            return StartWithPosixEtxtbsyRetry(() => Process.Start(startInfo), isWindows);
        }
        catch (Win32Exception ex) when (!isWindows && ex.NativeErrorCode == PosixEnoexec && template.FileName != "/bin/sh")
        {
            ProcessStartInfo fallback = CloneStartInfo(template, "/bin/sh", prependArgument: resolved);
            return StartWithPosixEtxtbsyRetry(() => Process.Start(fallback), isWindows);
        }
    }

    /// <summary>
    /// Executes <paramref name="startProcess"/> and retries transient POSIX <c>ETXTBSY</c> (<see cref="PosixEtxtbsy"/>)
    /// failures caused when a concurrent <c>fork()</c> briefly inherits a writable descriptor to a freshly written
    /// executable before <c>execve()</c> closes <c>FD_CLOEXEC</c> descriptors.
    /// </summary>
    /// <typeparam name="T">The process handle type returned by <paramref name="startProcess"/>.</typeparam>
    /// <param name="startProcess">Callback that starts the process.</param>
    /// <param name="isWindows">Whether the current platform is Windows (<c>ETXTBSY</c> retries apply only on POSIX).</param>
    /// <param name="sleep">Optional backoff delay callback receiving milliseconds; defaults to <see cref="Thread.Sleep(int)"/>.</param>
    /// <returns>The value returned by <paramref name="startProcess"/>.</returns>
    public static T StartWithPosixEtxtbsyRetry<T>(Func<T> startProcess, bool isWindows, Action<int>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(startProcess);
        Action<int> effectiveSleep = sleep ?? Thread.Sleep;

        for (int attempt = 0; attempt < EtxtbsyRetryDelaysMs.Length; attempt++)
        {
            try
            {
                return startProcess();
            }
            catch (Win32Exception ex) when (!isWindows && ex.NativeErrorCode == PosixEtxtbsy)
            {
                effectiveSleep(EtxtbsyRetryDelaysMs[attempt]);
            }
        }

        return startProcess();
    }

    /// <summary>
    /// Resolves <paramref name="fileName"/> to an existing executable file path relative to
    /// <paramref name="workingDirectory"/> or across <c>PATH</c> directories in directory order.
    /// </summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="workingDirectory">Optional working directory for relative paths.</param>
    /// <param name="environment">Optional environment overrides (honors custom <c>PATH</c>).</param>
    /// <param name="isWindows">Optional platform override for cross-platform unit testing.</param>
    /// <returns>The resolved file path, or <see langword="null"/> when not found.</returns>
    public static string? ResolveExecutablePath(
        string fileName,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        bool? isWindows = null)
    {
        bool effectiveIsWindows = isWindows ?? OperatingSystem.IsWindows();
        bool hasOverride = false;
        string? pathOverride = null;
        if (environment is not null)
        {
            hasOverride = TryGetPathFromReadOnlyDictionary(environment, effectiveIsWindows, out pathOverride);
        }

        return ResolveExecutablePathCore(fileName, workingDirectory, hasOverride, pathOverride, effectiveIsWindows);
    }

    private static string? ResolveExecutablePathCore(
        string fileName,
        string? workingDirectory,
        bool hasPathOverride,
        string? pathOverride,
        bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (HasDirectorySeparator(fileName))
        {
            string baseDir = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory;
            string candidate = Path.IsPathRooted(fileName) ? fileName : Path.GetFullPath(fileName, baseDir);
            return ResolveCandidateInDirectory(candidate, isWindows, allowExtensionlessOnPosixOnly: false);
        }

        string? pathValue = hasPathOverride ? pathOverride : Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        char pathSeparator = isWindows ? ';' : ':';
        string? nonExecutableFallback = null;

        foreach (string rawDir in pathValue.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir = rawDir.Trim().Trim('"');
            if (dir.Length == 0)
            {
                continue;
            }

            string baseCandidate = Path.Combine(dir, fileName);
            if (isWindows)
            {
                string? winMatch = ResolveWindowsCandidate(baseCandidate, fileName);
                if (winMatch is not null)
                {
                    return winMatch;
                }
            }
            else if (File.Exists(baseCandidate))
            {
                if (IsPosixExecutable(baseCandidate))
                {
                    return baseCandidate;
                }

                nonExecutableFallback ??= baseCandidate;
            }
        }

        return nonExecutableFallback;
    }

    private static string? ResolveCandidateInDirectory(string candidate, bool isWindows, bool allowExtensionlessOnPosixOnly)
    {
        if (isWindows)
        {
            if (!string.IsNullOrEmpty(Path.GetExtension(candidate)))
            {
                return File.Exists(candidate) ? candidate : null;
            }

            foreach (string ext in WindowsExecutableExtensions)
            {
                string withExt = candidate + ext;
                if (File.Exists(withExt))
                {
                    return withExt;
                }
            }

            return !allowExtensionlessOnPosixOnly && File.Exists(candidate) ? candidate : null;
        }

        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveWindowsCandidate(string baseCandidate, string originalFileName)
    {
        if (!string.IsNullOrEmpty(Path.GetExtension(originalFileName)))
        {
            return File.Exists(baseCandidate) ? baseCandidate : null;
        }

        foreach (string ext in WindowsExecutableExtensions)
        {
            string candidateWithExt = baseCandidate + ext;
            if (File.Exists(candidateWithExt))
            {
                return candidateWithExt;
            }
        }

        return null;
    }

    private static bool IsPosixExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(path);
            const UnixFileMode executeBits = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & executeBits) != 0;
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

    private static bool HasDirectorySeparator(string fileName) =>
        fileName.Contains('/', StringComparison.Ordinal) || fileName.Contains('\\', StringComparison.Ordinal);

    private static bool TryGetPathFromDictionary(IDictionary<string, string?> environment, bool isWindows, out string? pathValue)
    {
        if (environment.TryGetValue("PATH", out pathValue))
        {
            return true;
        }

        if (isWindows)
        {
            string? matchKey = environment.Keys.FirstOrDefault(k => string.Equals(k, "PATH", StringComparison.OrdinalIgnoreCase));
            if (matchKey is not null)
            {
                pathValue = environment[matchKey];
                return true;
            }
        }

        pathValue = null;
        return false;
    }

    private static bool TryGetPathFromReadOnlyDictionary(
        IReadOnlyDictionary<string, string?> environment,
        bool isWindows,
        out string? pathValue)
    {
        if (environment.TryGetValue("PATH", out pathValue))
        {
            return true;
        }

        if (isWindows)
        {
            string? matchKey = environment.Keys.FirstOrDefault(k => string.Equals(k, "PATH", StringComparison.OrdinalIgnoreCase));
            if (matchKey is not null)
            {
                pathValue = environment[matchKey];
                return true;
            }
        }

        pathValue = null;
        return false;
    }

    private static ProcessStartInfo CloneStartInfo(ProcessStartInfo template, string fileName, string? prependArgument)
    {
        var clone = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = template.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = template.RedirectStandardOutput,
            RedirectStandardError = template.RedirectStandardError,
            RedirectStandardInput = template.RedirectStandardInput,
            CreateNoWindow = template.CreateNoWindow,
        };

        if (template.StandardOutputEncoding is not null)
        {
            clone.StandardOutputEncoding = template.StandardOutputEncoding;
        }

        if (template.StandardErrorEncoding is not null)
        {
            clone.StandardErrorEncoding = template.StandardErrorEncoding;
        }

        if (template.StandardInputEncoding is not null)
        {
            clone.StandardInputEncoding = template.StandardInputEncoding;
        }

        if (prependArgument is not null)
        {
            clone.ArgumentList.Add(prependArgument);
            foreach (string arg in template.ArgumentList)
            {
                clone.ArgumentList.Add(arg);
            }
        }
        else if (template.ArgumentList.Count > 0)
        {
            foreach (string arg in template.ArgumentList)
            {
                clone.ArgumentList.Add(arg);
            }
        }
        else if (!string.IsNullOrEmpty(template.Arguments))
        {
            clone.Arguments = template.Arguments;
        }

        clone.Environment.Clear();
        foreach (KeyValuePair<string, string?> entry in template.Environment)
        {
            clone.Environment[entry.Key] = entry.Value;
        }

        return clone;
    }
}
