using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class CliLinterGateService
{
    private static List<string> DiscoverWorkflowFiles(string repoRoot)
    {
        string workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
        if (!Directory.Exists(workflowsDir))
        {
            return [];
        }

        return Directory.EnumerateFiles(workflowsDir)
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .Where(IsWorkflowYaml)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> DiscoverDockerfiles(string repoRoot)
    {
        var results = new List<string>();
        CollectDockerfilesRecursive(repoRoot, repoRoot, results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static void CollectDockerfilesRecursive(string repoRoot, string currentDir, List<string> results)
    {
        if (!Directory.Exists(currentDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(currentDir))
        {
            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith("Dockerfile", StringComparison.Ordinal))
            {
                results.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
            }
        }

        foreach (string subDir in Directory.EnumerateDirectories(currentDir))
        {
            string dirName = Path.GetFileName(subDir);
            if (ExcludedDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            CollectDockerfilesRecursive(repoRoot, subDir, results);
        }
    }

    private static List<string> DiscoverShellScripts(string repoRoot)
    {
        var results = new List<string>();
        CollectShellScriptsRecursive(repoRoot, repoRoot, results);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static void CollectShellScriptsRecursive(string repoRoot, string currentDir, List<string> results)
    {
        if (!Directory.Exists(currentDir))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(currentDir))
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (IsShellScriptPath(rel))
            {
                results.Add(rel);
            }
        }

        foreach (string subDir in Directory.EnumerateDirectories(currentDir))
        {
            string dirName = Path.GetFileName(subDir);
            if (ExcludedDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            CollectShellScriptsRecursive(repoRoot, subDir, results);
        }
    }

    private static int ExecuteActionlintBatches(string workingDirectory, string executable, List<string> workflowFiles)
    {
        bool failed = false;
        foreach (string[] batch in workflowFiles.Chunk(MaxFilesPerBatch))
        {
            if (RunLinterProcess(workingDirectory, executable, batch) != 0)
            {
                failed = true;
            }
        }

        if (failed)
        {
            Console.WriteLine("[ERROR] actionlint reported workflow or composite-action violations.");
            return 1;
        }

        Console.WriteLine($"✔ actionlint validated {workflowFiles.Count} workflow file(s).");
        return 0;
    }

    private static int ExecuteHadolintBatches(string workingDirectory, string executable, List<string> dockerfiles)
    {
        bool failed = false;
        foreach (string[] batch in dockerfiles.Chunk(MaxFilesPerBatch))
        {
            var args = new List<string>(batch.Length + 2) { "--failure-threshold", "warning" };
            args.AddRange(batch);
            if (RunLinterProcess(workingDirectory, executable, args) != 0)
            {
                failed = true;
            }
        }

        if (failed)
        {
            Console.WriteLine("[ERROR] hadolint reported Dockerfile warning- or error-level violations.");
            return 1;
        }

        Console.WriteLine($"✔ hadolint validated {dockerfiles.Count} Dockerfile(s).");
        return 0;
    }

    private static int ExecuteShellcheckBatches(string workingDirectory, string executable, List<string> shellScripts)
    {
        bool failed = false;
        foreach (string[] batch in shellScripts.Chunk(MaxFilesPerBatch))
        {
            var args = new List<string>(batch.Length + 1) { "-x" };
            args.AddRange(batch);
            if (RunLinterProcess(workingDirectory, executable, args) != 0)
            {
                failed = true;
            }
        }

        if (failed)
        {
            Console.WriteLine("[ERROR] shellcheck reported shell script violations.");
            return 1;
        }

        Console.WriteLine($"✔ shellcheck validated {shellScripts.Count} shell script(s).");
        return 0;
    }

    private static ResolvedLinterTool? ResolveBinary(
        string repoRoot,
        string toolName,
        string versionFlag,
        Func<string, string?>? getEnvironmentVariable)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        string exeName = OperatingSystem.IsWindows() ? $"{toolName}.exe" : toolName;
        string cadenceBinDir = DoctorProvisioningService.ResolveCadenceBinDirectory(repoRoot, getEnvironmentVariable);
        string cadenceBinCandidate = Path.Combine(cadenceBinDir, exeName);
        if (File.Exists(cadenceBinCandidate) && TryProbeVersion(repoRoot, cadenceBinCandidate, versionFlag, out string binVersion))
        {
            return new ResolvedLinterTool(cadenceBinCandidate, ".cadence/bin", binVersion);
        }

        string? pathTool = DetectSecretsGateService.FindExecutableOnPath(toolName, getEnvironmentVariable("PATH"));
        if (pathTool is not null && TryProbeVersion(repoRoot, pathTool, versionFlag, out string pathVersion))
        {
            return new ResolvedLinterTool(pathTool, "PATH", pathVersion);
        }

        return null;
    }

    private static bool TryProbeVersion(string workingDirectory, string fileName, string versionFlag, out string versionOutput)
    {
        versionOutput = string.Empty;
        string effectiveDir = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory;

        try
        {
            if (!ProcessPrimitives.TryRunCaptured(fileName, effectiveDir, [versionFlag], out var output, timeoutMilliseconds: 15_000) ||
                output.ExitCode != 0)
            {
                return false;
            }

            string combined = string.IsNullOrWhiteSpace(output.Stdout) ? output.Stderr : output.Stdout;
            versionOutput = combined
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? "available";
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static int RunLinterProcess(string workingDirectory, string fileName, IReadOnlyList<string> args)
    {
        try
        {
            var psi = ProcessPrimitives.CreateStartInfo(fileName, workingDirectory, args);
            var output = ProcessPrimitives.RunCaptured(psi);
            if (output.ExitCode == -1 && output.Stderr.StartsWith("Failed to start process", StringComparison.Ordinal))
            {
                Console.WriteLine($"[ERROR] Failed to start '{fileName}'.");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(output.Stdout))
            {
                Console.WriteLine(output.Stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(output.Stderr))
            {
                Console.WriteLine(output.Stderr.TrimEnd());
            }

            return output.ExitCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to execute '{fileName}': {ex.Message}");
            return 1;
        }
    }

    private static void PrintMissingToolError(string toolName, string version)
    {
        Console.WriteLine($"[ERROR] '{toolName}' was not found in .cadence/bin or on PATH (refusing to skip {toolName} quality gate).");
        Console.WriteLine($"        Provision pinned CLI linters ({toolName} v{version}) with:");
        Console.WriteLine("          pnpm cadence doctor --auto-fix --linters-only");
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("          pwsh scripts/setup_wizard.ps1 -NonInteractive -Minimal");
        }
        else
        {
            Console.WriteLine("          bash scripts/setup_wizard.sh --dev --minimal");
        }
    }
}
