using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class PreCommitOpenApiService
{
    /// <summary>
    /// The <c>openapi</c> gate. When staged C# files exist, exports specs for affected services.
    /// When no C# files are staged, skips service host startup for sub-second execution.
    /// </summary>
    public static int ExecutePreCommitOpenApi(string? repoRoot = null, bool quick = false, IEnumerable<string>? targetFiles = null)
    {
        repoRoot ??= FindRepoRoot();

        if (!quick)
        {
            return OpenApiDriftValidatorService.ValidateDrift(repoRoot);
        }

        var files = targetFiles?.ToList() ?? GetStagedFiles(repoRoot);

        var csharpFiles = files
            .Where(IsCsharpFile)
            .ToList();

        if (csharpFiles.Count == 0)
        {
            Console.WriteLine("No staged C# backend changes detected. Skipping service startup (lightweight docs check)...");
            return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
        }

        bool affectsSharedLibraries = csharpFiles.Any(IsSharedOrSolutionFile);
        List<string>? targetServices = null;

        if (!affectsSharedLibraries)
        {
            var detectedServices = csharpFiles
                .Select(ResolveServiceFromPath)
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()!;

            if (detectedServices.Count == 0)
            {
                Console.WriteLine("Staged C# changes do not affect microservice API contracts. Skipping service startup...");
                return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
            }

            targetServices = detectedServices!;
            Console.WriteLine($"Staged C# changes detected for microservice(s): {string.Join(", ", targetServices)}");
        }
        else
        {
            Console.WriteLine("Staged C# changes detected in shared packages/solution files. Triggering multi-service OpenAPI export...");
        }

        int syncResult = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot, targetServices);
        int docsResult = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
        return syncResult != 0 || docsResult != 0 ? 1 : 0;
    }

    public static List<string> GetStagedFiles(string repoRoot)
    {
        var staged = RunGitLines(repoRoot, "diff", "--cached", "--name-only", "--diff-filter=ACMR");
        if (staged is null || staged.Count == 0)
        {
            return [];
        }

        // During a merge commit, `git diff --cached` against HEAD lists every file brought in from MERGE_HEAD even when its
        // staged content matches MERGE_HEAD verbatim. Keep only paths that also differ from MERGE_HEAD (conflict resolutions
        // or manual merge edits), matching GxpSnapshotGuardService's merge handling.
        string? mergeHead = RunGitLines(repoRoot, "rev-parse", "--verify", "-q", "MERGE_HEAD")?.FirstOrDefault();
        if (!string.IsNullOrEmpty(mergeHead))
        {
            var differsFromMergeHead = RunGitLines(repoRoot, "diff", "--cached", "--name-only", "--diff-filter=ACMR", mergeHead);
            if (differsFromMergeHead is not null)
            {
                var differing = new HashSet<string>(differsFromMergeHead, StringComparer.Ordinal);
                staged = [.. staged.Where(differing.Contains)];
            }
        }

        return staged;
    }

    private static bool IsCsharpFile(string file)
    {
        string normalized = file.Replace('\\', '/');
        return normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedOrSolutionFile(string file)
    {
        string normalized = file.Replace('\\', '/');

        if (normalized.StartsWith("packages/cli/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("packages/tooling_core/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("packages/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/apphost/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/gateway/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalized.Contains('/') ||
            normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static string? ResolveServiceFromPath(string file)
    {
        string normalized = file.Replace('\\', '/');
        if (!normalized.StartsWith("apps/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        string appFolder = parts[1].ToLowerInvariant();
        return appFolder switch
        {
            "ctms" => "ctms",
            "designer" => "designer",
            "econsent" => "econsent",
            "eisf" => "eisf",
            "etmf" => "etmf",
            "execution" => "execution",
            "fileshare" => "fileshare",
            "imaging" => "imaging",
            "interop" => "interop",
            "kernel-api" or "kernel" or "intelligence" or "org" => "kernel",
            "knowledge" => "knowledge",
            "notifications" => "notifications",
            "payments" => "payments",
            "quality" => "quality",
            "safety" => "safety",
            "tickets" => "tickets",
            _ => null
        };
    }

    public static List<string>? TryGetTrackedFiles(string repoRoot)
    {
        if (!Directory.Exists(Path.Combine(repoRoot, ".git")) && !File.Exists(Path.Combine(repoRoot, ".git")))
        {
            return null;
        }

        return RunGitLines(repoRoot, "ls-files");
    }

    public static List<string>? TryGetChangedFilesSinceBase(string repoRoot, string baseRef = "origin/main")
    {
        if (!Directory.Exists(Path.Combine(repoRoot, ".git")) && !File.Exists(Path.Combine(repoRoot, ".git")))
        {
            return null;
        }

        string? compareBase = null;
        string? mergeHead = RunGitLines(repoRoot, "rev-parse", "--verify", "-q", "MERGE_HEAD")?.FirstOrDefault();
        if (!string.IsNullOrEmpty(mergeHead) &&
            RunGitLines(repoRoot, "merge-base", "--is-ancestor", baseRef, mergeHead) is not null)
        {
            compareBase = RunGitLines(repoRoot, "rev-parse", "--verify", "-q", baseRef)?.FirstOrDefault();
        }

        compareBase ??= RunGitLines(repoRoot, "merge-base", baseRef, "HEAD")?.FirstOrDefault();
        if (string.IsNullOrEmpty(compareBase))
        {
            return null;
        }

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in RunGitLines(repoRoot, "diff", "--name-only", "--diff-filter=d", compareBase) ?? [])
        {
            changed.Add(file);
        }

        foreach (string file in RunGitLines(repoRoot, "diff", "--cached", "--name-only", "--diff-filter=d", compareBase) ?? [])
        {
            changed.Add(file);
        }

        return [.. changed];
    }

    private static List<string>? RunGitLines(string repoRoot, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
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
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stderrTask = process.StandardError.ReadToEndAsync();
            string stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = stderrTask.GetAwaiter().GetResult();
            return process.ExitCode == 0
                ? [.. stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                : null;
        }
        catch
        {
            return null;
        }
    }

    internal static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
