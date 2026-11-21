using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Starts external CLI processes and falls back to <c>/bin/sh</c> on POSIX <c>ENOEXEC</c> (<c>NativeErrorCode == 8</c>),
/// matching <c>execvp(3)</c> semantics for shebang-less shims such as npm's <c>pnpm</c> 10/11 POSIX wrapper.
/// </summary>
public static class PosixScriptProcessHelper
{
    /// <summary>POSIX <c>errno</c> code for <c>ENOEXEC</c> (<c>Exec format error</c>).</summary>
    public const int PosixEnoexec = 8;

    /// <summary>
    /// Starts <paramref name="psi"/> via <see cref="Process.Start(ProcessStartInfo)"/>, retrying under <c>/bin/sh</c>
    /// when a POSIX executable lacks a kernel-recognized binary header or <c>#!</c> interpreter line.
    /// </summary>
    public static Process? StartWithPosixScriptFallback(
        ProcessStartInfo psi,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(psi);

        try
        {
            return Process.Start(psi);
        }
        catch (Win32Exception ex) when (!OperatingSystem.IsWindows() && ex.NativeErrorCode == PosixEnoexec && psi.FileName != "/bin/sh")
        {
            string workingDirectory = string.IsNullOrWhiteSpace(psi.WorkingDirectory)
                ? Directory.GetCurrentDirectory()
                : psi.WorkingDirectory;
            string? resolvedScript = ResolveExecutablePath(psi.FileName, workingDirectory, environment);
            if (resolvedScript is null)
            {
                throw;
            }

            var shPsi = new ProcessStartInfo("/bin/sh")
            {
                WorkingDirectory = psi.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = psi.RedirectStandardOutput,
                RedirectStandardError = psi.RedirectStandardError,
                RedirectStandardInput = psi.RedirectStandardInput,
                CreateNoWindow = psi.CreateNoWindow,
                StandardOutputEncoding = psi.StandardOutputEncoding,
                StandardErrorEncoding = psi.StandardErrorEncoding,
            };

            shPsi.ArgumentList.Add(resolvedScript);
            foreach (string arg in psi.ArgumentList)
            {
                shPsi.ArgumentList.Add(arg);
            }

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    shPsi.Environment[key] = value;
                }
            }
            else if (psi.Environment.Count > 0)
            {
                foreach (var (key, value) in psi.Environment)
                {
                    shPsi.Environment[key] = value;
                }
            }

            return Process.Start(shPsi);
        }
    }

    /// <summary>
    /// Resolves <paramref name="fileName"/> to an existing file path using <paramref name="workingDirectory"/> when
    /// relative or <c>PATH</c> when bare.
    /// </summary>
    public static string? ResolveExecutablePath(
        string fileName,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null)
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

        string? pathValue = environment is not null && environment.TryGetValue("PATH", out string? overridePath)
            ? overridePath
            : Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (string dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
