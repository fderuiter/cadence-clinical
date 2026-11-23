using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Fail-closed Prettier and ESLint quality gates for <c>cadence check</c> and <c>cadence check --pre-commit</c>.
/// </summary>
public static class FrontendQualityGateService
{
    private const int MaxFilesPerBatch = 400;
    private static readonly int MaxStdinParallelism = Math.Clamp(Environment.ProcessorCount, 1, 8);
    private static readonly Lock ConsoleOutputLock = new();

    private static readonly HashSet<string> PrettierExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue", ".json", ".css", ".md", ".yaml", ".yml",
    };

    private static readonly HashSet<string> EslintExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs", ".vue", ".css", ".html", ".json",
    };

    public static int CheckPrettier(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null) =>
        RunPrettier(repoRoot, targetFiles, preCommit, write: false, indexReader);

    public static int FormatPrettier(string? repoRoot = null, string[]? targetFiles = null) =>
        RunPrettier(repoRoot, targetFiles, preCommit: false, write: true, indexReader: null);

    private static int RunPrettier(
        string? repoRoot,
        string[]? targetFiles,
        bool preCommit,
        bool write,
        GitIndexReader? indexReader)
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

        GitIndexReader? ownedReader = null;
        GitIndexReader? reader = !write
            ? GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out ownedReader)
            : null;
        using (ownedReader)
        {
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

            var matching = SelectExistingFiles(repoRoot, rawFiles, PrettierExtensions, reader);
            if (matching.Count == 0)
            {
                return 0;
            }

            string ignorePath = Path.Combine(repoRoot, ".prettierignore");
            bool hasIgnoreFile = File.Exists(ignorePath);

            if (!write && reader is { HasAuthoritativeIndex: true })
            {
                var stagedBlobs = ReadStagedBlobs(reader, matching);
                int failed = 0;
                Parallel.ForEach(
                    stagedBlobs,
                    new ParallelOptions { MaxDegreeOfParallelism = MaxStdinParallelism },
                    (entry, loopState) =>
                    {
                        if (Volatile.Read(ref failed) != 0)
                        {
                            return;
                        }

                        var stdinArgs = new List<string> { "exec", "prettier", "--check" };
                        if (hasIgnoreFile)
                        {
                            stdinArgs.Add("--ignore-path");
                            stdinArgs.Add(".prettierignore");
                        }

                        stdinArgs.Add("--stdin-filepath");
                        stdinArgs.Add(entry.Path);

                        if (RunProcessWithStdin(repoRoot, pnpm, stdinArgs, entry.Bytes, suppressStdoutOnSuccess: true) != 0 &&
                            Interlocked.CompareExchange(ref failed, 1, 0) == 0)
                        {
                            lock (ConsoleOutputLock)
                            {
                                Console.WriteLine($"[ERROR] Prettier formatting violations detected in staged '{entry.Path}'. Run 'pnpm cadence fix' or 'pnpm format' to auto-fix.");
                            }

                            loopState.Stop();
                        }
                    });

                return failed != 0 ? 1 : 0;
            }

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
    }

    public static int CheckEslint(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
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
            var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
            using (ownedReader)
            {
                var rawFiles = preCommit && targetFiles is null
                    ? PreCommitOpenApiService.GetStagedFiles(repoRoot)
                    : (IReadOnlyList<string>)targetFiles!;

                var matching = SelectExistingFiles(repoRoot, rawFiles, EslintExtensions, reader);
                if (matching.Count == 0)
                {
                    return 0;
                }

                const string EcrfStoreRelPath = "apps/web/src/stores/demo/clinicalEcrf.ts";
                string duplicateCheckScript = Path.Combine(repoRoot, "scripts", "check_duplicate_identifiers.js");
                if (File.Exists(duplicateCheckScript) &&
                    matching.Any(f => f.Equals(EcrfStoreRelPath, StringComparison.OrdinalIgnoreCase)))
                {
                    if (reader is { HasAuthoritativeIndex: true })
                    {
                        using var storeWorkspace = reader.Materialize([EcrfStoreRelPath]);
                        string tempStorePath = Path.Combine(storeWorkspace.RootPath, "apps", "web", "src", "stores", "demo", "clinicalEcrf.ts");
                        if (File.Exists(tempStorePath) &&
                            RunProcess(Path.Combine(repoRoot, "apps", "web"), "node", [duplicateCheckScript, tempStorePath]) != 0)
                        {
                            return 1;
                        }
                    }
                    else
                    {
                        string diskStorePath = Path.Combine(repoRoot, "apps", "web", "src", "stores", "demo", "clinicalEcrf.ts");
                        if (RunProcess(Path.Combine(repoRoot, "apps", "web"), "node", [duplicateCheckScript, diskStorePath]) != 0)
                        {
                            return 1;
                        }
                    }
                }

                if (reader is { HasAuthoritativeIndex: true })
                {
                    var stagedBlobs = ReadStagedBlobs(reader, matching);
                    int failed = 0;
                    Parallel.ForEach(
                        stagedBlobs,
                        new ParallelOptions { MaxDegreeOfParallelism = MaxStdinParallelism },
                        (entry, loopState) =>
                        {
                            if (Volatile.Read(ref failed) != 0)
                            {
                                return;
                            }

                            var stdinArgs = BuildEslintArgs(["--stdin", "--stdin-filename", entry.Path]);

                            if (RunProcessWithStdin(repoRoot, pnpm, stdinArgs, entry.Bytes, suppressStdoutOnSuccess: false) != 0 &&
                                Interlocked.CompareExchange(ref failed, 1, 0) == 0)
                            {
                                lock (ConsoleOutputLock)
                                {
                                    Console.WriteLine($"[ERROR] ESLint violations detected in staged '{entry.Path}'. Fix the findings above or run 'pnpm -r lint -- --max-warnings 0'.");
                                }

                                loopState.Stop();
                            }
                        });

                    return failed != 0 ? 1 : 0;
                }

                foreach (string[] batch in matching.Chunk(MaxFilesPerBatch))
                {
                    var args = BuildEslintArgs(["--", .. batch]);
                    if (RunProcess(repoRoot, pnpm, args) != 0)
                    {
                        Console.WriteLine("[ERROR] ESLint violations detected in staged files. Fix the findings above or run 'pnpm -r lint -- --max-warnings 0'.");
                        return 1;
                    }
                }

                return 0;
            }
        }

        if (RunProcess(repoRoot, pnpm, ["-r", "lint"]) != 0 ||
            RunProcess(repoRoot, pnpm, BuildEslintArgs(["."])) != 0)
        {
            Console.WriteLine("[ERROR] Workspace ESLint check ('pnpm -r lint && pnpm exec eslint --max-warnings 0 .') failed.");
            return 1;
        }

        return 0;
    }

    public static List<string> BuildEslintArgs(IEnumerable<string> additionalArgs) =>
        ["exec", "eslint", "--no-warn-ignored", "--max-warnings", "0", .. additionalArgs];

    private static string? ResolveUsablePnpm(string repoRoot)
    {
        string? resolved = PosixProcessLauncher.ResolveExecutablePath("pnpm", repoRoot);
        if (resolved is null)
        {
            Console.WriteLine("[ERROR] 'pnpm' is required for frontend quality gates but was not found on PATH. Install Node.js and run 'corepack enable'.");
            return null;
        }

        var psi = new ProcessStartInfo(resolved)
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
                Console.WriteLine("[ERROR] 'pnpm' is required for frontend quality gates but could not be started.");
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode == 0)
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

    private static List<(string Path, byte[] Bytes)> ReadStagedBlobs(GitIndexReader reader, IReadOnlyList<string> paths)
    {
        var blobs = new List<(string Path, byte[] Bytes)>(paths.Count);
        foreach (string rel in paths)
        {
            if (reader.TryReadBytes(rel, out byte[] blobBytes))
            {
                blobs.Add((rel, blobBytes));
            }
        }

        return blobs;
    }

    private static int RunProcessWithStdin(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        byte[] stdinBytes,
        bool suppressStdoutOnSuccess)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

            if (((process.ExitCode != 0 || !suppressStdoutOnSuccess) && !string.IsNullOrWhiteSpace(stdout)) ||
                (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr)))
            {
                lock (ConsoleOutputLock)
                {
                    if ((process.ExitCode != 0 || !suppressStdoutOnSuccess) && !string.IsNullOrWhiteSpace(stdout))
                    {
                        Console.WriteLine(stdout.TrimEnd());
                    }

                    if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                    {
                        Console.WriteLine(stderr.TrimEnd());
                    }
                }
            }

            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.WriteLine($"[ERROR] Could not start '{fileName}': {ex.Message}");
            return 1;
        }
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
