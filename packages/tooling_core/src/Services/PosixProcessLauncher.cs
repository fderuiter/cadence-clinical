using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Starts external CLI processes with POSIX <c>ENOEXEC</c> (<c>/bin/sh</c>) fallback for shebang-less script shims
/// such as the standalone <c>pnpm</c> wrapper installed by npm and <c>pnpm/action-setup</c>.
/// </summary>
public static class PosixProcessLauncher
{
    /// <summary>POSIX <c>ENOEXEC</c> (<c>Exec format error</c>) native error code.</summary>
    public const int PosixEnoexec = 8;

    /// <summary>
    /// Starts a process configured from <paramref name="template"/>, falling back to <c>/bin/sh</c> when a POSIX
    /// executable lacks a kernel <c>#!</c> header (<c>ENOEXEC</c>).
    /// </summary>
    /// <param name="template">The initial <see cref="ProcessStartInfo"/> describing the command to run.</param>
    /// <returns>The started <see cref="Process"/>, or <see langword="null"/> if no process resource was started.</returns>
    public static Process? Start(ProcessStartInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);

        try
        {
            return Process.Start(template);
        }
        catch (Win32Exception ex) when (!OperatingSystem.IsWindows() && ex.NativeErrorCode == PosixEnoexec && template.FileName != "/bin/sh")
        {
            string? resolved = ResolveExecutablePath(template.FileName, template.WorkingDirectory);
            if (resolved is null)
            {
                throw;
            }

            var fallback = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = template.WorkingDirectory,
                UseShellExecute = template.UseShellExecute,
                RedirectStandardOutput = template.RedirectStandardOutput,
                RedirectStandardError = template.RedirectStandardError,
                RedirectStandardInput = template.RedirectStandardInput,
                CreateNoWindow = template.CreateNoWindow,
            };
            fallback.ArgumentList.Add(resolved);
            foreach (string arg in template.ArgumentList)
            {
                fallback.ArgumentList.Add(arg);
            }

            foreach (KeyValuePair<string, string?> entry in template.Environment)
            {
                fallback.Environment[entry.Key] = entry.Value;
            }

            return Process.Start(fallback);
        }
    }

    /// <summary>
    /// Resolves <paramref name="fileName"/> to an existing file path relative to <paramref name="workingDirectory"/>
    /// or across <c>PATH</c> directories.
    /// </summary>
    /// <param name="fileName">Executable name or path.</param>
    /// <param name="workingDirectory">Optional working directory for relative paths.</param>
    /// <returns>The resolved file path, or <see langword="null"/> when not found.</returns>
    public static string? ResolveExecutablePath(string fileName, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
        {
            string baseDir = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory;
            string candidate = Path.IsPathRooted(fileName) ? fileName : Path.GetFullPath(fileName, baseDir);
            return File.Exists(candidate) ? candidate : null;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        return pathValue
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, fileName))
            .FirstOrDefault(File.Exists);
    }
}
