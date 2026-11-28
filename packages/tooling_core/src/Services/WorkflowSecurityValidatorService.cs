using System;
using System.Collections.Generic;
using System.IO;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Validates GitHub Actions workflows (<c>.github/workflows/*.{yml,yaml}</c>) and composite or container actions
/// (<c>.github/actions/**/action.{yml,yaml}</c>) for 40-character commit SHA pinning, inline version comments,
/// <c>sha256:</c> digests on <c>docker://</c> references, <c>persist-credentials: false</c> on <c>actions/checkout</c>,
/// least-privilege workflow token permissions, ref-guarded <c>workflow_dispatch</c> controls with typed inputs,
/// and bot actor gating on <c>issue_comment</c> mutation jobs.
/// </summary>
public static partial class WorkflowSecurityValidatorService
{
    public static int ValidateWorkflowSecurity(
        string[]? files = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        Console.WriteLine("Validating GitHub Actions workflow security, SHA pinning, and token permissions...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ No workflow or composite action files in target list.");
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            var targets = ResolveTargetFiles(files, repoRoot, reader, out bool explicitFilterEmpty);
            if (explicitFilterEmpty)
            {
                Console.WriteLine("✔ No workflow or composite action files in target list.");
                return 0;
            }

            if (targets.Count == 0)
            {
                Console.WriteLine($"[ERROR] No GitHub workflow or action files found under '{Path.Combine(repoRoot, ".github")}'.");
                return 1;
            }

            var errors = new List<string>();
            foreach (var (fullPath, relativePath, isWorkflow) in targets)
            {
                ValidateFile(fullPath, relativePath, isWorkflow, reader, errors);
            }

            if (errors.Count > 0)
            {
                foreach (string error in errors)
                {
                    Console.WriteLine($"[WORKFLOW SECURITY] {error}");
                }

                Console.WriteLine($"\n[ERROR] Found {errors.Count} workflow security violation(s).");
                return 1;
            }

            Console.WriteLine($"✔ Validated {targets.Count} workflow and action file(s) cleanly.");
            return 0;
        }
    }

    private static List<(string FullPath, string RelativePath, bool IsWorkflow)> ResolveTargetFiles(
        string[]? files,
        string repoRoot,
        GitIndexReader? reader,
        out bool explicitFilterEmpty)
    {
        explicitFilterEmpty = false;
        var results = new List<(string FullPath, string RelativePath, bool IsWorkflow)>();

        if (files is not null)
        {
            foreach (string raw in files)
            {
                string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
                if (string.IsNullOrEmpty(rel))
                {
                    continue;
                }

                string fullPath = Path.Combine(repoRoot, rel);
                bool exists = reader is not null ? reader.FileExists(rel) : File.Exists(fullPath);
                if (!exists)
                {
                    continue;
                }

                if (IsWorkflowPath(rel))
                {
                    results.Add((fullPath, rel, true));
                }
                else if (IsCompositeActionPath(rel))
                {
                    results.Add((fullPath, rel, false));
                }
            }

            results.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
            explicitFilterEmpty = results.Count == 0;
            return results;
        }

        string workflowsDir = Path.Combine(repoRoot, ".github", "workflows");
        if (Directory.Exists(workflowsDir))
        {
            foreach (string file in Directory.EnumerateFiles(workflowsDir))
            {
                string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (IsWorkflowPath(rel))
                {
                    results.Add((file, rel, true));
                }
            }
        }

        string actionsDir = Path.Combine(repoRoot, ".github", "actions");
        if (Directory.Exists(actionsDir))
        {
            foreach (string file in Directory.EnumerateFiles(actionsDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                if (IsCompositeActionPath(rel))
                {
                    results.Add((file, rel, false));
                }
            }
        }

        results.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
        return results;
    }

    private static bool IsWorkflowPath(string relativePath)
    {
        if (!relativePath.StartsWith(".github/workflows/", StringComparison.Ordinal))
        {
            return false;
        }

        string remainder = relativePath[".github/workflows/".Length..];
        return !remainder.Contains('/', StringComparison.Ordinal) && IsYamlExtension(remainder);
    }

    private static bool IsCompositeActionPath(string relativePath)
    {
        if (!relativePath.StartsWith(".github/actions/", StringComparison.Ordinal))
        {
            return false;
        }

        string fileName = Path.GetFileName(relativePath);
        return string.Equals(fileName, "action.yml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "action.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYamlExtension(string fileName) =>
        fileName.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    private static void ValidateFile(
        string fullPath,
        string relativePath,
        bool isWorkflow,
        GitIndexReader? reader,
        List<string> errors)
    {
        string[] lines;
        if (reader is not null)
        {
            if (!reader.TryReadLines(relativePath, out lines))
            {
                return;
            }
        }
        else
        {
            lines = File.ReadAllLines(fullPath);
        }

        if (isWorkflow)
        {
            var workflow = ParseWorkflowFile(lines);
            bool triggersPullRequest = workflow.Triggers.Contains("pull_request") ||
                                       workflow.Triggers.Contains("pull_request_target");
            ValidateWorkflowPermissionsAndTriggers(relativePath, workflow, triggersPullRequest, errors);
            foreach (var uses in workflow.JobUses)
            {
                ValidateUsesReference(relativePath, uses, errors);
            }

            foreach (var step in workflow.Steps)
            {
                ValidateStep(relativePath, step, triggersPullRequest, errors);
            }
        }
        else
        {
            var action = ParseActionFile(lines);
            if (action.DockerImage is { } dockerImage)
            {
                ValidateDockerReference(relativePath, dockerImage.LineNumber, dockerImage.Value, errors);
            }

            foreach (var step in action.Steps)
            {
                ValidateStep(relativePath, step, triggersPullRequest: false, errors);
            }
        }
    }
}
