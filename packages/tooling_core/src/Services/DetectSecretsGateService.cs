using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Cadence.ToolingCore.Services;

/// <summary>Describes a verified <c>detect-secrets-hook</c> executable resolved from <c>.cadence/venv</c>, <c>PATH</c>, or <c>uvx</c>.</summary>
public sealed record DetectSecretsTool(string Source, string FileName, IReadOnlyList<string> PrefixArguments, string Version);

/// <summary>
/// Fail-closed <c>detect-secrets</c> gate shared by <c>cadence check</c>, <c>cadence check --pre-commit</c>,
/// <c>cadence doctor</c>, and <c>cadence ci</c>.
/// </summary>
public static class DetectSecretsGateService
{
    public const string PinnedVersion = "1.5.0";
    public const string DisableUvxFallbackEnvVar = "CADENCE_DISABLE_UVX_FALLBACK";
    private const int MaxFilesPerBatch = 200;

    /// <summary>
    /// Resolves <c>detect-secrets-hook</c> in priority order: <c>.cadence/venv</c>, <c>.venv</c>, <c>PATH</c>, then <c>uvx</c>.
    /// Every candidate must successfully run <c>--version</c>.
    /// </summary>
    public static DetectSecretsTool? ResolveTool(string? repoRoot = null, Func<string, string?>? getEnvironmentVariable = null)
    {
        repoRoot ??= FindRepoRoot();
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        // 1. Isolated .cadence/venv (provisioned by setup_wizard.sh / setup_wizard.ps1)
        foreach (string venvDir in new[] { Path.Combine(repoRoot, ".cadence", "venv"), Path.Combine(repoRoot, ".venv") })
        {
            string venvBinary = OperatingSystem.IsWindows()
                ? Path.Combine(venvDir, "Scripts", "detect-secrets-hook.exe")
                : Path.Combine(venvDir, "bin", "detect-secrets-hook");

            if (File.Exists(venvBinary) && TryProbeVersion(venvBinary, [], getEnvironmentVariable, out string venvVersion))
            {
                string label = venvDir.EndsWith(Path.Combine(".cadence", "venv"), StringComparison.Ordinal)
                    ? ".cadence/venv"
                    : ".venv";
                return new DetectSecretsTool(label, venvBinary, [], venvVersion);
            }
        }

        // 2. PATH lookup
        string? pathBinary = FindExecutableOnPath("detect-secrets-hook", getEnvironmentVariable("PATH"));
        if (pathBinary is not null && TryProbeVersion(pathBinary, [], getEnvironmentVariable, out string pathVersion))
        {
            return new DetectSecretsTool("PATH", pathBinary, [], pathVersion);
        }

        // 3. uvx fallback
        if (getEnvironmentVariable(DisableUvxFallbackEnvVar) != "1")
        {
            string? uvxBinary = FindExecutableOnPath("uvx", getEnvironmentVariable("PATH"));
            string[] uvxPrefix = ["--from", $"detect-secrets=={PinnedVersion}", "detect-secrets-hook"];
            if (uvxBinary is not null && TryProbeVersion(uvxBinary, uvxPrefix, getEnvironmentVariable, out string uvxVersion))
            {
                return new DetectSecretsTool("uvx", uvxBinary, uvxPrefix, uvxVersion);
            }
        }

        return null;
    }

    public static int Scan(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        repoRoot ??= FindRepoRoot();
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        var tool = ResolveTool(repoRoot, getEnvironmentVariable);
        if (tool is null)
        {
            Console.WriteLine("[ERROR] 'detect-secrets-hook' is not available in .cadence/venv, PATH, or uvx.");
            Console.WriteLine($"  Provision it via './scripts/setup_wizard.sh --minimal' (or 'python3 -m venv .cadence/venv && .cadence/venv/bin/python -m pip install --upgrade pip detect-secrets=={PinnedVersion}').");
            return 1;
        }

        string baselinePath = Path.Combine(repoRoot, ".secrets.baseline");
        if (!File.Exists(baselinePath))
        {
            Console.WriteLine($"[ERROR] Secrets baseline file not found at '{baselinePath}'.");
            return 1;
        }

        string baselineContent;
        try
        {
            baselineContent = File.ReadAllText(baselinePath);
            using var _ = JsonDocument.Parse(baselineContent);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Secrets baseline '{baselinePath}' is invalid JSON: {ex.Message}");
            return 1;
        }

        var filesToScan = ResolveFilesToScan(repoRoot, targetFiles, preCommit);
        if (filesToScan.Count == 0)
        {
            return 0;
        }

        if (!IsGitWorkTree(repoRoot))
        {
            Console.WriteLine($"[ERROR] '{repoRoot}' is not a git work tree; detect-secrets requires a git repository context.");
            return 1;
        }

        string tempBaseline = Path.Combine(Path.GetTempPath(), $"cadence-secrets-baseline-{Guid.NewGuid():N}.json");
        try
        {
            foreach (string[] batch in filesToScan.Chunk(MaxFilesPerBatch))
            {
                // Re-seed temp baseline on each batch because detect-secrets-hook trims baseline entries to the scanned batch.
                File.WriteAllText(tempBaseline, baselineContent);

                var args = new List<string>(tool.PrefixArguments)
                {
                    "--baseline",
                    tempBaseline,
                    "--exclude-files",
                    SecretsBaselineCleanerService.CanonicalExcludePattern,
                };
                args.AddRange(batch);

                (int exitCode, string stdout, string stderr) = RunCaptured(repoRoot, tool.FileName, args, getEnvironmentVariable);

                // Exit code 0: clean.
                // Exit code 3: detect_secrets.pre_commit_hook returns 3 strictly when new_secrets is empty and only
                // baseline line_number / trim metadata changed (SecretsBaselineCleanerService strips line_number).
                if (exitCode is 0 or 3)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    Console.WriteLine(stdout.TrimEnd());
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Console.WriteLine(stderr.TrimEnd());
                }

                Console.WriteLine("[ERROR] detect-secrets blocked unbaselined secret(s). Remove the secret, or mark a reviewed false positive with an inline '# pragma: allowlist secret' comment.");
                return 1;
            }

            return 0;
        }
        finally
        {
            if (File.Exists(tempBaseline))
            {
                File.Delete(tempBaseline);
            }
        }
    }

    private static List<string> ResolveFilesToScan(string repoRoot, string[]? targetFiles, bool preCommit)
    {
        IEnumerable<string> rawCandidates;
        if (preCommit)
        {
            rawCandidates = targetFiles is { Length: > 0 }
                ? targetFiles
                : PreCommitOpenApiService.GetStagedFiles(repoRoot);
        }
        else if (targetFiles is not null)
        {
            rawCandidates = targetFiles;
        }
        else
        {
            rawCandidates = PreCommitOpenApiService.TryGetChangedFilesSinceBase(repoRoot)
                ?? PreCommitOpenApiService.TryGetTrackedFiles(repoRoot)
                ?? EnumerateDirectoryFiles(repoRoot);
        }

        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in rawCandidates)
        {
            string fullPath = Path.IsPathRooted(raw) ? Path.GetFullPath(raw) : Path.GetFullPath(Path.Combine(repoRoot, raw));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string rel = Path.GetRelativePath(repoRoot, fullPath).Replace('\\', '/');
            if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith(".cadence/", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals(".secrets.baseline", StringComparison.Ordinal) ||
                SecretsBaselineCleanerService.IsExcludedFile(rel))
            {
                continue;
            }

            result.Add(rel);
        }

        return [.. result];
    }

    public static string? FindExecutableOnPath(string toolName, string? pathEnv)
    {
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        string[] suffixes = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string suffix in suffixes)
            {
                string candidate = Path.Combine(dir, toolName + suffix);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool TryProbeVersion(
        string fileName,
        IReadOnlyList<string> prefixArgs,
        Func<string, string?> getEnvironmentVariable,
        out string version)
    {
        version = "";
        var args = new List<string>(prefixArgs) { "--version" };
        var (exitCode, stdout, _) = RunCaptured(Directory.GetCurrentDirectory(), fileName, args, getEnvironmentVariable);
        if (exitCode != 0)
        {
            return false;
        }

        version = stdout.Trim();
        return version.Length > 0;
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCaptured(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        Func<string, string?> getEnvironmentVariable)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        string? overriddenPath = getEnvironmentVariable("PATH");
        if (overriddenPath is not null)
        {
            psi.Environment["PATH"] = overriddenPath;
        }

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return (1, "", "Process failed to start.");
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            string stderr = stderrTask.GetAwaiter().GetResult();
            return (process.ExitCode, stdout, stderr);
        }
        catch (Win32Exception ex)
        {
            return (127, "", ex.Message);
        }
    }

    private static bool IsGitWorkTree(string repoRoot) =>
        Directory.Exists(Path.Combine(repoRoot, ".git")) || File.Exists(Path.Combine(repoRoot, ".git"));

    private static IEnumerable<string> EnumerateDirectoryFiles(string repoRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return Directory.EnumerateFiles(repoRoot, "*", options);
    }

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
