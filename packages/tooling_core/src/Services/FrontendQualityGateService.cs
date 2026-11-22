using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Fail-closed Prettier and ESLint quality gates for <c>cadence check</c> and <c>cadence check --pre-commit</c>.
/// </summary>
public static class FrontendQualityGateService
{
    private const int MaxFilesPerBatch = 400;

    private static readonly HashSet<string> PrettierExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue", ".json", ".css", ".md", ".yaml", ".yml",
    };

    private static readonly HashSet<string> EslintExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue",
    };

    public static int CheckPrettier(string? repoRoot = null, string[]? targetFiles = null, bool preCommit = false) =>
        RunPrettier(repoRoot, targetFiles, preCommit, write: false);

    public static int FormatPrettier(string? repoRoot = null, string[]? targetFiles = null) =>
        RunPrettier(repoRoot, targetFiles, preCommit: false, write: true);

    private static int RunPrettier(string? repoRoot, string[]? targetFiles, bool preCommit, bool write)
    {
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        repoRoot ??= FindRepoRoot();
        string? pnpm = ResolveUsablePnpm(repoRoot);
        if (pnpm is null)
        {
            return 1;
        }

        if (!EnsureNodeModules(repoRoot))
        {
            return 1;
        }

        IEnumerable<string> rawFiles;
        if (preCommit)
        {
            rawFiles = targetFiles is not null
                ? targetFiles
                : PreCommitOpenApiService.GetStagedFiles(repoRoot);
        }
        else if (targetFiles is not null)
        {
            rawFiles = targetFiles;
        }
        else
        {
            rawFiles = PreCommitOpenApiService.TryGetTrackedFiles(repoRoot)
                ?? EnumerateRepositoryFiles(repoRoot);
        }

        var matching = SelectExistingFiles(repoRoot, rawFiles, PrettierExtensions);
        if (matching.Count == 0)
        {
            return 0;
        }

        string ignorePath = Path.Combine(repoRoot, ".prettierignore");
        bool hasIgnoreFile = File.Exists(ignorePath);
        string modeFlag = write ? "--write" : "--check";

        foreach (string[] batch in matching.Chunk(MaxFilesPerBatch))
        {
            var args = new List<string> { "exec", "prettier", modeFlag };
            if (hasIgnoreFile)
            {
                args.Add("--ignore-path");
                args.Add(".prettierignore");
            }

            args.Add("--");
            args.AddRange(batch);

            if (RunProcess(repoRoot, pnpm, args) != 0)
            {
                Console.WriteLine(write
                    ? "[ERROR] Prettier auto-formatting failed."
                    : "[ERROR] Prettier formatting violations detected. Run 'pnpm cadence fix' or 'pnpm format' to auto-fix.");
                return 1;
            }
        }

        return 0;
    }

    public static int CheckEslint(string? repoRoot = null, string[]? targetFiles = null, bool preCommit = false)
    {
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        repoRoot ??= FindRepoRoot();
        string? pnpm = ResolveUsablePnpm(repoRoot);
        if (pnpm is null)
        {
            return 1;
        }

        if (!EnsureNodeModules(repoRoot))
        {
            return 1;
        }

        if (preCommit || targetFiles is not null)
        {
            var rawFiles = preCommit && targetFiles is null
                ? PreCommitOpenApiService.GetStagedFiles(repoRoot)
                : (IReadOnlyList<string>)(targetFiles ?? []);

            var matching = SelectExistingFiles(repoRoot, rawFiles, EslintExtensions);
            if (matching.Count == 0)
            {
                return 0;
            }

            string duplicateCheckScript = Path.Combine(repoRoot, "scripts", "check_duplicate_identifiers.js");
            if (File.Exists(duplicateCheckScript) &&
                matching.Any(f => f.Equals("apps/web/src/stores/clinical.ts", StringComparison.OrdinalIgnoreCase)) &&
                RunProcess(Path.Combine(repoRoot, "apps", "web"), "node", [duplicateCheckScript]) != 0)
            {
                return 1;
            }

            foreach (string[] batch in matching.Chunk(MaxFilesPerBatch))
            {
                var args = new List<string> { "exec", "eslint", "--no-warn-ignored", "--" };
                args.AddRange(batch);
                if (RunProcess(repoRoot, pnpm, args) != 0)
                {
                    Console.WriteLine("[ERROR] ESLint violations detected in staged files. Fix the findings above or run 'pnpm -r lint'.");
                    return 1;
                }
            }

            return 0;
        }

        if (RunProcess(repoRoot, pnpm, ["-r", "lint"]) != 0)
        {
            Console.WriteLine("[ERROR] Workspace ESLint check ('pnpm -r lint') failed.");
            return 1;
        }

        return 0;
    }

    private static string? ResolveUsablePnpm(string repoRoot)
    {
        string[] candidates = OperatingSystem.IsWindows() ? ["pnpm.cmd", "pnpm"] : ["pnpm"];
        foreach (string candidate in candidates)
        {
            var psi = new ProcessStartInfo(candidate)
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--version");

            try
            {
                using var process = PosixProcessLauncher.Start(psi);
                if (process is null)
                {
                    continue;
                }

                var stderrTask = process.StandardError.ReadToEndAsync();
                _ = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                _ = stderrTask.GetAwaiter().GetResult();

                if (process.ExitCode == 0)
                {
                    return candidate;
                }

                Console.WriteLine("[ERROR] pnpm is installed on PATH but exited non-zero. Run 'corepack enable' so pnpm matches the packageManager pin in package.json.");
                return null;
            }
            catch (Win32Exception)
            {
                // Try next candidate.
            }
        }

        Console.WriteLine("[ERROR] 'pnpm' is required for frontend quality gates but was not found on PATH. Install Node.js and run 'corepack enable'.");
        return null;
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

    private static List<string> SelectExistingFiles(string repoRoot, IEnumerable<string> candidates, HashSet<string> extensions)
    {
        var selected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in candidates)
        {
            string fullPath = Path.IsPathRooted(raw) ? Path.GetFullPath(raw) : Path.GetFullPath(Path.Combine(repoRoot, raw));
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

            string ext = Path.GetExtension(fullPath);
            if (!extensions.Contains(ext))
            {
                continue;
            }

            string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
            selected.Add(rel);
        }

        return [.. selected];
    }

    internal static int RunProcess(string workingDirectory, string fileName, IReadOnlyList<string> arguments, bool suppressOutput = false)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = suppressOutput,
            RedirectStandardError = suppressOutput,
        };
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

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
