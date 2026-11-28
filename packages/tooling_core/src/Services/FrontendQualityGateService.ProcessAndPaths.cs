using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class FrontendQualityGateService
{
    private static string? ResolveUsablePnpm(string repoRoot, Func<string, string?>? getEnvironmentVariable = null)
    {
        IReadOnlyDictionary<string, string?>? environment = getEnvironmentVariable is null
            ? null
            : new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = getEnvironmentVariable("PATH"),
            };

        string? resolved = PosixProcessLauncher.ResolveExecutablePath("pnpm", repoRoot, environment);
        if (resolved is null)
        {
            Console.WriteLine("[ERROR] 'pnpm' is required for frontend quality gates but was not found on PATH. Install Node.js and run 'corepack enable'.");
            return null;
        }

        var psi = ProcessPrimitives.CreateStartInfo(resolved, repoRoot, ["--version"]);
        if (getEnvironmentVariable is not null && getEnvironmentVariable("PATH") is { } pathOverride)
        {
            psi.Environment["PATH"] = pathOverride;
        }

        try
        {
            var output = ProcessPrimitives.RunCaptured(psi);
            if (output.ExitCode == -1 && output.Stderr.StartsWith("Failed to start process", StringComparison.Ordinal))
            {
                Console.WriteLine("[ERROR] 'pnpm' is required for frontend quality gates but could not be started.");
                return null;
            }

            if (output.ExitCode == 0)
            {
                return resolved;
            }

            Console.WriteLine("[ERROR] pnpm is installed on PATH but exited non-zero. Run 'corepack enable' so pnpm matches the packageManager pin in package.json.");
            return null;
        }
        catch (Win32Exception)
        {
            Console.WriteLine("[ERROR] 'pnpm' is required for frontend quality gates but was not found on PATH. Install Node.js and run 'corepack enable'.");
            return null;
        }
    }

    private static bool EnsureNodeModules(string repoRoot)
    {
        if (Directory.Exists(Path.Combine(repoRoot, "node_modules")))
        {
            return true;
        }

        Console.WriteLine($"[ERROR] Frontend workspace dependencies ('node_modules') are missing in '{repoRoot}'. Run 'pnpm install --frozen-lockfile' first.");
        return false;
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string repoRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        return Directory.EnumerateFiles(repoRoot, "*", options)
            .Where(fullPath =>
            {
                string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
                return !rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith(".cadence/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("dist/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/dist/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) &&
                       !rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static List<string> SelectExistingFiles(
        string repoRoot,
        IEnumerable<string> candidates,
        HashSet<string> extensions,
        GitIndexReader? reader)
    {
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in candidates)
        {
            string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
            if (string.IsNullOrEmpty(rel))
            {
                continue;
            }

            if (reader is not null)
            {
                if (!reader.FileExists(rel))
                {
                    continue;
                }
            }
            else
            {
                string fullPath = Path.Combine(repoRoot, rel);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }
            }

            string ext = Path.GetExtension(rel);
            if (!extensions.Contains(ext))
            {
                continue;
            }

            selected.Add(rel);
        }

        return [.. selected];
    }

    private static int RunProcessWithStdin(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        byte[] stdinBytes,
        bool suppressStdoutOnSuccess)
    {
        var psi = ProcessPrimitives.CreateStartInfo(fileName, workingDirectory, arguments, redirectInput: true);

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return 1;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            try
            {
                process.StandardInput.BaseStream.Write(stdinBytes, 0, stdinBytes.Length);
                process.StandardInput.BaseStream.Flush();
            }
            catch (IOException)
            {
                // Subprocess exited before consuming all stdin bytes.
            }
            finally
            {
                process.StandardInput.Close();
            }

            process.WaitForExit();
            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if ((process.ExitCode != 0 || !suppressStdoutOnSuccess) && !string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine(stdout.TrimEnd());
            }

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine(stderr.TrimEnd());
            }

            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not start '{fileName}': {ex.Message}");
            return 1;
        }
    }

    internal static int RunProcess(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        bool suppressOutput = false,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = suppressOutput,
            RedirectStandardError = suppressOutput,
        };
        if (getEnvironmentVariable is not null && getEnvironmentVariable("PATH") is { } pathOverride)
        {
            psi.Environment["PATH"] = pathOverride;
        }
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return 1;
            }

            if (suppressOutput)
            {
                var stderrTask = process.StandardError.ReadToEndAsync();
                _ = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                _ = stderrTask.GetAwaiter().GetResult();
            }
            else
            {
                process.WaitForExit();
            }

            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not start '{fileName}': {ex.Message}");
            return 1;
        }
    }
}
