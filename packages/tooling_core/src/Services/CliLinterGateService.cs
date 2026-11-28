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
        Func<string, string?>? getEnvironmentVariable = null) =>
        RunLinterGate(
            repoRoot,
            targetFiles,
            preCommit,
            indexReader,
            toolName: "actionlint",
            toolVersion: ActionlintVersion,
            resolveTool: r => ResolveActionlint(r, getEnvironmentVariable),
            isPrimaryTarget: IsWorkflowYaml,
            isConfigOrSupport: IsCompositeActionOrConfig,
            discoverAllOnDisk: DiscoverWorkflowFiles,
            planIndexExecution: static (idx, checkAll, matched) =>
            {
                var githubIndexPaths = idx.Paths
                    .Where(p => IsWorkflowYaml(p) || IsCompositeActionOrConfig(p))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                var workflowsToValidate = checkAll
                    ? githubIndexPaths.Where(IsWorkflowYaml).ToList()
                    : matched;
                return (workflowsToValidate, githubIndexPaths, CreateGitMarker: true);
            },
            executeBatches: ExecuteActionlintBatches);

    public static int RunHadolint(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null,
        Func<string, string?>? getEnvironmentVariable = null) =>
        RunLinterGate(
            repoRoot,
            targetFiles,
            preCommit,
            indexReader,
            toolName: "hadolint",
            toolVersion: HadolintVersion,
            resolveTool: r => ResolveHadolint(r, getEnvironmentVariable),
            isPrimaryTarget: IsDockerfilePath,
            isConfigOrSupport: IsHadolintConfig,
            discoverAllOnDisk: DiscoverDockerfiles,
            planIndexExecution: static (idx, checkAll, matched) =>
            {
                var dockerfilesToValidate = checkAll
                    ? idx.Paths.Where(IsDockerfilePath).Order(StringComparer.Ordinal).ToList()
                    : matched;
                var materializePaths = new List<string>(dockerfilesToValidate);
                materializePaths.AddRange(idx.Paths.Where(IsHadolintConfig));
                return (dockerfilesToValidate, materializePaths, CreateGitMarker: false);
            },
            executeBatches: ExecuteHadolintBatches);

    public static int RunShellcheck(
        string? repoRoot = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null,
        Func<string, string?>? getEnvironmentVariable = null) =>
        RunLinterGate(
            repoRoot,
            targetFiles,
            preCommit,
            indexReader,
            toolName: "shellcheck",
            toolVersion: ShellcheckVersion,
            resolveTool: r => ResolveShellcheck(r, getEnvironmentVariable),
            isPrimaryTarget: IsShellScriptPath,
            isConfigOrSupport: IsShellcheckConfig,
            discoverAllOnDisk: DiscoverShellScripts,
            planIndexExecution: static (idx, checkAll, matched) =>
            {
                var shellIndexPaths = idx.Paths
                    .Where(p => IsShellScriptPath(p) || IsShellcheckConfig(p))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                var scriptsToValidate = checkAll
                    ? shellIndexPaths.Where(IsShellScriptPath).ToList()
                    : matched;
                return (scriptsToValidate, shellIndexPaths, CreateGitMarker: false);
            },
            executeBatches: ExecuteShellcheckBatches);

    private static int RunLinterGate(
        string? repoRoot,
        string[]? targetFiles,
        bool preCommit,
        GitIndexReader? indexReader,
        string toolName,
        string toolVersion,
        Func<string, ResolvedLinterTool?> resolveTool,
        Func<string, bool> isPrimaryTarget,
        Func<string, bool> isConfigOrSupport,
        Func<string, List<string>> discoverAllOnDisk,
        Func<GitIndexReader, bool, List<string>, (List<string> TargetsToValidate, List<string> PathsToMaterialize, bool CreateGitMarker)> planIndexExecution,
        Func<string, string, List<string>, int> executeBatches)
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

                var tool = resolveTool(repoRoot);
                if (tool is null)
                {
                    PrintMissingToolError(toolName, toolVersion);
                    return 1;
                }

                var matchedTargets = new List<string>();
                bool checkAll = false;

                foreach (string raw in rawFiles)
                {
                    string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                    if (string.IsNullOrEmpty(rel) || !ExistsInSnapshot(repoRoot, rel, reader))
                    {
                        continue;
                    }

                    if (isPrimaryTarget(rel))
                    {
                        matchedTargets.Add(rel);
                    }
                    else if (isConfigOrSupport(rel))
                    {
                        checkAll = true;
                    }
                }

                if (matchedTargets.Count == 0 && !checkAll)
                {
                    return 0;
                }

                var distinctMatched = matchedTargets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

                if (reader is { HasAuthoritativeIndex: true })
                {
                    var (targetsToValidate, pathsToMaterialize, createGitMarker) =
                        planIndexExecution(reader, checkAll, distinctMatched);
                    if (targetsToValidate.Count == 0)
                    {
                        return 0;
                    }

                    using var workspace = reader.Materialize(pathsToMaterialize, createGitMarker);
                    return executeBatches(workspace.RootPath, tool.FileName, targetsToValidate);
                }

                var diskTargets = checkAll ? discoverAllOnDisk(repoRoot) : distinctMatched;
                if (diskTargets.Count == 0)
                {
                    return 0;
                }

                return executeBatches(repoRoot, tool.FileName, diskTargets);
            }

            var fullScanTool = resolveTool(repoRoot);
            if (fullScanTool is null)
            {
                PrintMissingToolError(toolName, toolVersion);
                return 1;
            }

            var allTargets = discoverAllOnDisk(repoRoot);
            if (allTargets.Count == 0)
            {
                return 0;
            }

            return executeBatches(repoRoot, fullScanTool.FileName, allTargets);
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
