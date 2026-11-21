using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

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

        string workingDirectory = string.IsNullOrWhiteSpace(psi.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : psi.WorkingDirectory;
        string? preResolved = !OperatingSystem.IsWindows()
            ? ResolveExecutablePath(psi.FileName, workingDirectory, environment, psi.Environment)
            : null;
        ProcessStartInfo effectivePsi = preResolved is not null && !string.Equals(preResolved, psi.FileName, StringComparison.Ordinal)
            ? CloneStartInfo(psi, preResolved, environment)
            : psi;

        try
        {
            return Process.Start(effectivePsi);
        }
        catch (Win32Exception ex) when (!OperatingSystem.IsWindows() && ex.NativeErrorCode == PosixEnoexec && psi.FileName != "/bin/sh")
        {
            string? resolvedScript = preResolved ?? ResolveExecutablePath(psi.FileName, workingDirectory, environment, psi.Environment);
            if (resolvedScript is null)
            {
                throw;
            }

            var shPsi = CloneStartInfo(psi, "/bin/sh", environment, includeOriginalArguments: false);
            shPsi.ArgumentList.Add(resolvedScript);
            if (psi.ArgumentList.Count > 0)
            {
                foreach (string arg in psi.ArgumentList)
                {
                    shPsi.ArgumentList.Add(arg);
                }
            }
            else if (!string.IsNullOrWhiteSpace(psi.Arguments))
            {
                foreach (string arg in SplitArguments(psi.Arguments))
                {
                    shPsi.ArgumentList.Add(arg);
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
        IReadOnlyDictionary<string, string>? environment = null) =>
        ResolveExecutablePath(fileName, workingDirectory, environment, startInfoEnvironment: null);

    private static string? ResolveExecutablePath(
        string fileName,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        IDictionary<string, string?>? startInfoEnvironment)
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

        string? pathValue = null;
        if (environment is not null && environment.TryGetValue("PATH", out string? overridePath))
        {
            pathValue = overridePath;
        }
        else if (startInfoEnvironment is not null && startInfoEnvironment.TryGetValue("PATH", out string? psiPath))
        {
            pathValue = psiPath;
        }
        else
        {
            pathValue = Environment.GetEnvironmentVariable("PATH");
        }

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

    private static ProcessStartInfo CloneStartInfo(
        ProcessStartInfo source,
        string fileName,
        IReadOnlyDictionary<string, string>? environment,
        bool includeOriginalArguments = true)
    {
        var clone = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = source.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = source.RedirectStandardOutput,
            RedirectStandardError = source.RedirectStandardError,
            RedirectStandardInput = source.RedirectStandardInput,
            CreateNoWindow = source.CreateNoWindow,
            StandardOutputEncoding = source.StandardOutputEncoding,
            StandardErrorEncoding = source.StandardErrorEncoding,
        };

        if (includeOriginalArguments)
        {
            if (source.ArgumentList.Count > 0)
            {
                foreach (string arg in source.ArgumentList)
                {
                    clone.ArgumentList.Add(arg);
                }
            }
            else if (!string.IsNullOrEmpty(source.Arguments))
            {
                clone.Arguments = source.Arguments;
            }
        }

        if (source.Environment.Count > 0)
        {
            foreach (var (key, value) in source.Environment)
            {
                clone.Environment[key] = value;
            }
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                clone.Environment[key] = value;
            }
        }

        return clone;
    }

    private static List<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool tokenStarted = false;

        int index = 0;
        while (index < arguments.Length)
        {
            char c = arguments[index];
            if (c == '\\' && index + 1 < arguments.Length && (arguments[index + 1] == '"' || arguments[index + 1] == '\\'))
            {
                current.Append(arguments[index + 1]);
                tokenStarted = true;
                index += 2;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (tokenStarted)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }
            }
            else
            {
                current.Append(c);
                tokenStarted = true;
            }

            index++;
        }

        if (tokenStarted)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
