using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Fail-closed <c>actionlint</c>, <c>hadolint</c>, and <c>shellcheck</c> quality gates for <c>cadence check</c> and <c>cadence check --pre-commit</c>.
/// </summary>
public static class CliLinterGateService
{
    public const string ActionlintVersion = "1.7.12";
    public const string HadolintVersion = "2.15.1";
    public const string ShellcheckVersion = "0.11.0";
    private const int MaxFilesPerBatch = 100;

    private static readonly string[] ExcludedDirectories =
    [
        ".git", ".venv", ".cadence", "node_modules", "bin", "obj", "dist", "coverage", "playwright-report", "test-results"
    ];

    public sealed record ResolvedLinterTool(
        string FileName,
        string SourceLabel,
        string VersionOutput);

    public static ResolvedLinterTool? ResolveActionlint(
        string repoRoot,
        Func<string, string?>? getEnvironmentVariable = null) =>
        ResolveBinary(repoRoot, "actionlint", "-version", getEnvironmentVariable);

    public static ResolvedLinterTool? ResolveHadolint(
        string repoRoot,
        Func<string, string?>? getEnvironmentVariable = null) =>
        ResolveBinary(repoRoot, "hadolint", "--version", getEnvironmentVariable);

    public static ResolvedLinterTool? ResolveShellcheck(
        string repoRoot,
        Func<string, string?>? getEnvironmentVariable = null) =>
        ResolveBinary(repoRoot, "shellcheck", "--version", getEnvironmentVariable);

    public static int RunActionlint(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        repoRoot ??= FindRepoRoot();
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            if (preCommit || targetFiles is not null)
            {
                var rawFiles = preCommit && targetFiles is null
                    ? PreCommitOpenApiService.GetStagedFiles(repoRoot)
                    : (IReadOnlyList<string>)targetFiles!;

                if (rawFiles.Count == 0)
                {
                    return 0;
                }

                var tool = ResolveActionlint(repoRoot, getEnvironmentVariable);
                if (tool is null)
                {
                    PrintMissingToolError("actionlint", ActionlintVersion);
                    return 1;
                }

                var matchedWorkflows = new List<string>();
                bool checkAllWorkflows = false;

                foreach (string raw in rawFiles)
                {
                    string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                    if (string.IsNullOrEmpty(rel) || !ExistsInSnapshot(repoRoot, rel, reader))
                    {
                        continue;
                    }

                    if (IsWorkflowYaml(rel))
                    {
                        matchedWorkflows.Add(rel);
                    }
                    else if (IsCompositeActionOrConfig(rel))
                    {
                        checkAllWorkflows = true;
                    }
                }

                if (matchedWorkflows.Count == 0 && !checkAllWorkflows)
                {
                    return 0;
                }

                if (reader is { HasAuthoritativeIndex: true })
                {
                    var githubIndexPaths = reader.Paths
                        .Where(p => IsWorkflowYaml(p) || IsCompositeActionOrConfig(p))
                        .Order(StringComparer.Ordinal)
                        .ToList();

                    var workflowsToValidate = checkAllWorkflows
                        ? githubIndexPaths.Where(IsWorkflowYaml).ToList()
                        : matchedWorkflows.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                    if (workflowsToValidate.Count == 0)
                    {
                        return 0;
                    }

                    using var workspace = reader.Materialize(githubIndexPaths, createGitMarker: true);
                    return ExecuteActionlintBatches(workspace.RootPath, tool.FileName, workflowsToValidate);
                }

                var diskWorkflows = checkAllWorkflows
                    ? DiscoverWorkflowFiles(repoRoot)
                    : matchedWorkflows.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                if (diskWorkflows.Count == 0)
                {
                    return 0;
                }

                return ExecuteActionlintBatches(repoRoot, tool.FileName, diskWorkflows);
            }

            var fullScanTool = ResolveActionlint(repoRoot, getEnvironmentVariable);
            if (fullScanTool is null)
            {
                PrintMissingToolError("actionlint", ActionlintVersion);
                return 1;
            }

            var allWorkflows = DiscoverWorkflowFiles(repoRoot);
            if (allWorkflows.Count == 0)
            {
                return 0;
            }

            return ExecuteActionlintBatches(repoRoot, fullScanTool.FileName, allWorkflows);
        }
    }

    public static int RunHadolint(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        repoRoot ??= FindRepoRoot();
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            if (preCommit || targetFiles is not null)
            {
                var rawFiles = preCommit && targetFiles is null
                    ? PreCommitOpenApiService.GetStagedFiles(repoRoot)
                    : (IReadOnlyList<string>)targetFiles!;

                if (rawFiles.Count == 0)
                {
                    return 0;
                }

                var tool = ResolveHadolint(repoRoot, getEnvironmentVariable);
                if (tool is null)
                {
                    PrintMissingToolError("hadolint", HadolintVersion);
                    return 1;
                }

                var matchedDockerfiles = new List<string>();
                bool configChanged = false;

                foreach (string raw in rawFiles)
                {
                    string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                    if (string.IsNullOrEmpty(rel) || !ExistsInSnapshot(repoRoot, rel, reader))
                    {
                        continue;
                    }

                    if (IsDockerfilePath(rel))
                    {
                        matchedDockerfiles.Add(rel);
                    }
                    else if (IsHadolintConfig(rel))
                    {
                        configChanged = true;
                    }
                }

                if (matchedDockerfiles.Count == 0 && !configChanged)
                {
                    return 0;
                }

                if (reader is { HasAuthoritativeIndex: true })
                {
                    var dockerfilesToValidate = configChanged
                        ? reader.Paths.Where(IsDockerfilePath).Order(StringComparer.Ordinal).ToList()
                        : matchedDockerfiles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                    if (dockerfilesToValidate.Count == 0)
                    {
                        return 0;
                    }

                    var materializePaths = new List<string>(dockerfilesToValidate);
                    materializePaths.AddRange(reader.Paths.Where(IsHadolintConfig));

                    using var workspace = reader.Materialize(materializePaths);
                    return ExecuteHadolintBatches(workspace.RootPath, tool.FileName, dockerfilesToValidate);
                }

                var diskDockerfiles = configChanged
                    ? DiscoverDockerfiles(repoRoot)
                    : matchedDockerfiles.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                if (diskDockerfiles.Count == 0)
                {
                    return 0;
                }

                return ExecuteHadolintBatches(repoRoot, tool.FileName, diskDockerfiles);
            }

            var fullScanTool = ResolveHadolint(repoRoot, getEnvironmentVariable);
            if (fullScanTool is null)
            {
                PrintMissingToolError("hadolint", HadolintVersion);
                return 1;
            }

            var allDockerfiles = DiscoverDockerfiles(repoRoot);
            if (allDockerfiles.Count == 0)
            {
                return 0;
            }

            return ExecuteHadolintBatches(repoRoot, fullScanTool.FileName, allDockerfiles);
        }
    }

    public static int RunShellcheck(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        repoRoot ??= FindRepoRoot();
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            if (preCommit || targetFiles is not null)
            {
                var rawFiles = preCommit && targetFiles is null
                    ? PreCommitOpenApiService.GetStagedFiles(repoRoot)
                    : (IReadOnlyList<string>)targetFiles!;

                if (rawFiles.Count == 0)
                {
                    return 0;
                }

                var tool = ResolveShellcheck(repoRoot, getEnvironmentVariable);
                if (tool is null)
                {
                    PrintMissingToolError("shellcheck", ShellcheckVersion);
                    return 1;
                }

                var matchedScripts = new List<string>();
                bool configChanged = false;

                foreach (string raw in rawFiles)
                {
                    string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                    if (string.IsNullOrEmpty(rel) || !ExistsInSnapshot(repoRoot, rel, reader))
                    {
                        continue;
                    }

                    if (IsShellScript(repoRoot, rel, reader))
                    {
                        matchedScripts.Add(rel);
                    }
                    else if (IsShellcheckConfig(rel))
                    {
                        configChanged = true;
                    }
                }

                if (matchedScripts.Count == 0 && !configChanged)
                {
                    return 0;
                }

                if (reader is { HasAuthoritativeIndex: true })
                {
                    var scriptsToValidate = configChanged
                        ? reader.Paths.Where(p => IsShellScript(repoRoot, p, reader)).Order(StringComparer.Ordinal).ToList()
                        : matchedScripts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                    if (scriptsToValidate.Count == 0)
                    {
                        return 0;
                    }

                    var materializePaths = new List<string>(scriptsToValidate);
                    materializePaths.AddRange(reader.Paths.Where(IsShellcheckConfig));

                    using var workspace = reader.Materialize(materializePaths);
                    return ExecuteShellcheckBatches(workspace.RootPath, tool.FileName, scriptsToValidate);
                }

                var diskScripts = configChanged
                    ? DiscoverShellScripts(repoRoot)
                    : matchedScripts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                if (diskScripts.Count == 0)
                {
                    return 0;
                }

                return ExecuteShellcheckBatches(repoRoot, tool.FileName, diskScripts);
            }

            var fullScanTool = ResolveShellcheck(repoRoot, getEnvironmentVariable);
            if (fullScanTool is null)
            {
                PrintMissingToolError("shellcheck", ShellcheckVersion);
                return 1;
            }

            var allScripts = DiscoverShellScripts(repoRoot);
            if (allScripts.Count == 0)
            {
                return 0;
            }

            return ExecuteShellcheckBatches(repoRoot, fullScanTool.FileName, allScripts);
        }
    }

    public static bool IsWorkflowYaml(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string rest = normalized[".github/workflows/".Length..];
        if (rest.Length == 0 || rest.Contains('/'))
        {
            return false;
        }

        return rest.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
               rest.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCompositeActionOrConfig(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (string.Equals(normalized, ".github/actionlint.yml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, ".github/actionlint.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalized.StartsWith(".github/actions/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(normalized);
        return string.Equals(fileName, "action.yml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "action.yaml", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDockerfilePath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (ExcludedDirectories.Contains(segments[i], StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        string fileName = segments[^1];
        return fileName.StartsWith("Dockerfile", StringComparison.Ordinal);
    }

    private static bool IsHadolintConfig(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return string.Equals(normalized, ".hadolint.yaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, ".hadolint.yml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShellcheckConfig(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        return string.Equals(normalized, ".shellcheckrc", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsShellScript(string repoRoot, string relativePath, GitIndexReader? reader = null)
    {
        string normalized = relativePath.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (ExcludedDirectories.Contains(segments[i], StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        string fileName = segments[^1];
        if (fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        string? firstLine = ReadFirstLine(repoRoot, normalized, reader);
        return IsPosixOrBashShebang(firstLine);
    }

    public static bool IsPosixOrBashShebang(string? firstLine)
    {
        if (string.IsNullOrEmpty(firstLine))
        {
            return false;
        }

        ReadOnlySpan<char> span = firstLine.AsSpan();
        if (span.Length > 0 && span[0] == '\uFEFF')
        {
            span = span[1..];
        }

        span = span.TrimEnd(['\r', '\n']);
        if (!span.StartsWith("#!", StringComparison.Ordinal))
        {
            return false;
        }

        span = span[2..].TrimStart([' ', '\t']);
        string line = span.ToString();
        string[] tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        string interpreter = tokens[0];
        if (interpreter is "/usr/bin/env" or "/bin/env")
        {
            int idx = 1;
            while (idx < tokens.Length && tokens[idx].StartsWith('-'))
            {
                idx++;
            }

            if (idx >= tokens.Length)
            {
                return false;
            }

            return IsShellName(tokens[idx]);
        }

        if (interpreter.StartsWith('/'))
        {
            int slash = interpreter.LastIndexOf('/');
            return slash >= 0 && IsShellName(interpreter[(slash + 1)..]);
        }

        return false;
    }

    private static bool IsShellName(string name) =>
        name is "sh" or "bash" or "dash" or "ash" or "ksh";

    private static string? ReadFirstLine(string repoRoot, string relativePath, GitIndexReader? reader)
    {
        if (reader is not null)
        {
            if (!reader.TryReadBytes(relativePath, out byte[] bytes) || bytes.Length == 0)
            {
                return null;
            }

            int limit = Math.Min(bytes.Length, 256);
            for (int i = 0; i < limit; i++)
            {
                if (bytes[i] == 0)
                {
                    return null;
                }

                if (bytes[i] == (byte)'\n')
                {
                    limit = i;
                    break;
                }
            }

            return System.Text.Encoding.UTF8.GetString(bytes, 0, limit);
        }

        string fullPath = Path.Combine(repoRoot, relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            Span<byte> buffer = stackalloc byte[256];
            using var stream = File.OpenRead(fullPath);
            int read = stream.Read(buffer);
            if (read <= 0)
            {
                return null;
            }

            int limit = read;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == 0)
                {
                    return null;
                }

                if (buffer[i] == (byte)'\n')
                {
                    limit = i;
                    break;
                }
            }

            return System.Text.Encoding.UTF8.GetString(buffer[..limit]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static List<string> DiscoverShellScripts(string repoRoot)
    {
        var tracked = GitRevisionScopeService.TryGetTrackedFiles(repoRoot);
        if (tracked is not null)
        {
            return tracked
                .Where(rel => File.Exists(Path.Combine(repoRoot, rel)) && IsShellScript(repoRoot, rel))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

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
            if (IsShellScript(repoRoot, rel))
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

    private static bool ExistsInSnapshot(string repoRoot, string relativePath, GitIndexReader? reader) =>
        reader is not null ? reader.FileExists(relativePath) : File.Exists(Path.Combine(repoRoot, relativePath));

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
            Console.WriteLine("[ERROR] hadolint reported Dockerfile warning-or-error violations.");
            return 1;
        }

        Console.WriteLine($"✔ hadolint validated {dockerfiles.Count} Dockerfile(s).");
        return 0;
    }

    private static int ExecuteShellcheckBatches(string workingDirectory, string executable, List<string> scripts)
    {
        bool failed = false;
        foreach (string[] batch in scripts.Chunk(MaxFilesPerBatch))
        {
            if (RunLinterProcess(workingDirectory, executable, batch) != 0)
            {
                failed = true;
            }
        }

        if (failed)
        {
            Console.WriteLine("[ERROR] shellcheck reported shell script violations.");
            return 1;
        }

        Console.WriteLine($"✔ shellcheck validated {scripts.Count} shell script(s).");
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
        string cadenceBinCandidate = Path.Combine(repoRoot, ".cadence", "bin", exeName);
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
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(versionFlag);

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return false;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            if (process.ExitCode != 0)
            {
                return false;
            }

            string combined = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            string[] lines = combined.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string? explicitVersion = lines.FirstOrDefault(l => l.StartsWith("version:", StringComparison.OrdinalIgnoreCase));
            versionOutput = explicitVersion is not null
                ? explicitVersion["version:".Length..].Trim()
                : lines.FirstOrDefault() ?? "available";
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private static int RunLinterProcess(string workingDirectory, string fileName, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                Console.WriteLine($"[ERROR] Failed to start '{fileName}'.");
                return 1;
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine(stdout.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.WriteLine(stderr.TrimEnd());
            }

            return process.ExitCode;
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
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("          pwsh scripts/setup_wizard.ps1 -Dev -Minimal");
        }
        else
        {
            Console.WriteLine("          bash scripts/setup_wizard.sh --dev --minimal");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cadence.sln")) || Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Environment.CurrentDirectory;
    }
}
