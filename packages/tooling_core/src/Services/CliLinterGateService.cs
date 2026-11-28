using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Fail-closed <c>actionlint</c>, <c>hadolint</c>, and <c>shellcheck</c> quality gates for <c>cadence check</c> and <c>cadence check --pre-commit</c>,
/// plus <c>.cadence/bin</c> / <c>PATH</c> binary resolution for <c>actionlint</c>, <c>hadolint</c>, and <c>shellcheck</c>.
/// </summary>
public static partial class CliLinterGateService
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
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
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
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
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
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
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

                    if (IsShellScriptPath(rel))
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
                    var shellIndexPaths = reader.Paths
                        .Where(p => IsShellScriptPath(p) || IsShellcheckConfig(p))
                        .Order(StringComparer.Ordinal)
                        .ToList();

                    var scriptsToValidate = configChanged
                        ? shellIndexPaths.Where(IsShellScriptPath).ToList()
                        : matchedScripts.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                    if (scriptsToValidate.Count == 0)
                    {
                        return 0;
                    }

                    using var workspace = reader.Materialize(shellIndexPaths);
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

    public static bool IsShellScriptPath(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

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

        return segments.Length >= 2 &&
               string.Equals(segments[0], ".githooks", StringComparison.Ordinal) &&
               !fileName.EndsWith(".sample", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShellcheckConfig(string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return string.Equals(normalized, ".shellcheckrc", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "shellcheckrc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ExistsInSnapshot(string repoRoot, string relativePath, GitIndexReader? reader) =>
        reader is not null ? reader.FileExists(relativePath) : File.Exists(Path.Combine(repoRoot, relativePath));
}
