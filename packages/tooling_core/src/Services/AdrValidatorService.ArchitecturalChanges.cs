using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.ToolingCore.Services;

public static partial class AdrValidatorService
{
    [GeneratedRegex(@"(?:database|schema|model|migration|\bdb\b)", RegexOptions.IgnoreCase)]
    private static partial Regex AppPersistencePathRegex();

    private static readonly HashSet<string> FrontendExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vue", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".css", ".scss", ".html",
    };

    // Only dependency and toolchain sections of the root manifest count as architectural, so script edits need no ADR.
    private static readonly string[] PackageJsonDependencySections =
    [
        "dependencies", "devDependencies", "peerDependencies", "optionalDependencies",
        "overrides", "resolutions", "pnpm", "packageManager", "engines", "workspaces",
    ];

    private static bool CheckArchitecturalChangesRequireAdr(
        string repoRoot,
        bool preCommit,
        string? baseRef,
        string? pushBefore,
        string? eventName,
        string? gitRef)
    {
        var (changedFiles, adrCandidateFiles, compareBase) = ResolveChangedAndAdrFiles(repoRoot, preCommit, baseRef, pushBefore, eventName, gitRef);
        var architecturalChanges = changedFiles.Where(f => IsArchitecturalFile(f, repoRoot, preCommit, compareBase)).ToList();
        if (architecturalChanges.Count == 0) return true;

        bool newAdrAdded = adrCandidateFiles.Any(f => IsExistingAdrFile(f, repoRoot, preCommit));
        if (!newAdrAdded)
        {
            AnsiConsole.MarkupLine("[red]Error: Architectural changes detected, but no corresponding ADR found in docs/adr/.[/]");
            AnsiConsole.MarkupLine("Architectural files changed:");
            foreach (var f in architecturalChanges)
            {
                AnsiConsole.MarkupLine($"  - {f}");
            }
            AnsiConsole.MarkupLine("Please add a new ADR markdown file under docs/adr/ following the template.");
            return false;
        }
        return true;
    }

    private static bool IsExistingAdrFile(string relativePath, string repoRoot, bool preCommit)
    {
        string normalized = relativePath.Replace('\\', '/');
        if (!normalized.StartsWith("docs/adr/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(normalized);
        if (!FilenamePattern.IsMatch(fileName))
        {
            return false;
        }

        if (preCommit && GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", normalized) is not null)
        {
            return true;
        }

        return File.Exists(normalized) || File.Exists(Path.Combine(repoRoot, normalized));
    }

    public static bool IsArchitecturalFile(string filepath, string repoRoot, bool preCommit = false, string? compareBase = null)
    {
        string normalized = filepath.Replace('\\', '/');
        if (normalized.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".github/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            (Path.GetFileName(normalized).StartsWith('.') && normalized.EndsWith("ignore", StringComparison.OrdinalIgnoreCase)) ||
            normalized.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(normalized, "package.json", StringComparison.OrdinalIgnoreCase))
        {
            return RootPackageJsonDependenciesChanged(repoRoot, compareBase, preCommit);
        }

        if (normalized.StartsWith("apps/gateway/", StringComparison.OrdinalIgnoreCase)) return true;
        if (normalized.StartsWith("packages/security/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("packages/ui/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.StartsWith("apps/web/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("apps/subject-portal/", StringComparison.OrdinalIgnoreCase) ||
            FrontendExtensions.Contains(Path.GetExtension(normalized)))
        {
            return false;
        }

        if (normalized.StartsWith("apps/", StringComparison.OrdinalIgnoreCase) &&
            AppPersistencePathRegex().IsMatch(normalized))
        {
            return true;
        }

        return false;
    }

    public static bool PackageJsonDependenciesChanged(string? before, string? after)
    {
        if (before is null || after is null) return true;

        try
        {
            JsonNode? beforeRoot = JsonNode.Parse(before);
            JsonNode? afterRoot = JsonNode.Parse(after);
            if (beforeRoot is not JsonObject beforeObject || afterRoot is not JsonObject afterObject) return true;

            return PackageJsonDependencySections.Any(section =>
                !JsonNode.DeepEquals(beforeObject[section], afterObject[section]));
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool RootPackageJsonDependenciesChanged(string repoRoot, string? compareBase, bool preCommit)
    {
        string baselineRevision = string.IsNullOrEmpty(compareBase) ? "HEAD" : compareBase;
        string? before = GitRevisionScopeService.ReadFileAtRevision(repoRoot, baselineRevision, "package.json");
        string? after;
        if (preCommit)
        {
            after = GitRevisionScopeService.ReadFileAtRevision(repoRoot, "", "package.json");
        }
        else
        {
            string workingPath = Path.Combine(repoRoot, "package.json");
            after = File.Exists(workingPath) ? File.ReadAllText(workingPath) : null;
        }

        return PackageJsonDependenciesChanged(before, after);
    }

    private static (HashSet<string> ChangedFiles, HashSet<string> AdrCandidateFiles, string? CompareBase) ResolveChangedAndAdrFiles(
        string repoRoot,
        bool preCommit,
        string? baseRef,
        string? pushBefore,
        string? eventName,
        string? gitRef)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var adrCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string changedFilesTxt = Path.Combine(repoRoot, "changed_files.txt");
        if (File.Exists(changedFilesTxt))
        {
            foreach (var line in File.ReadAllLines(changedFilesTxt))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string entry = line.Trim().Replace('\\', '/');
                    changed.Add(entry);
                    adrCandidates.Add(entry);
                }
            }
        }

        if (!GitRevisionScopeService.HasGitRepository(repoRoot))
        {
            return (changed, adrCandidates, null);
        }

        string effectiveBaseRef = GitRevisionScopeService.NormalizeBaseRef(baseRef);
        string? headCommit = GitRevisionScopeService.TryResolveCommitSha(repoRoot, "HEAD");

        if (preCommit)
        {
            foreach (string staged in GitRevisionScopeService.GetStagedFiles(repoRoot, includeDeletions: false))
            {
                changed.Add(staged);
                adrCandidates.Add(staged);
            }

            string? branchCompareBase = GitRevisionScopeService.TryResolveBranchCompareBase(repoRoot, effectiveBaseRef);
            if (!string.IsNullOrEmpty(branchCompareBase) &&
                !string.IsNullOrEmpty(headCommit) &&
                !string.Equals(branchCompareBase, headCommit, StringComparison.Ordinal))
            {
                foreach (string committed in GitRevisionScopeService.GetChangedFilesBetweenCommits(repoRoot, branchCompareBase, headCommit, includeDeletions: false))
                {
                    adrCandidates.Add(committed);
                }
            }

            return (changed, adrCandidates, branchCompareBase);
        }

        var eventScope = GitRevisionScopeService.ResolveEventScope(repoRoot, effectiveBaseRef, pushBefore, eventName, gitRef);
        string? compareBase = eventScope.Kind switch
        {
            GitEventScopeKind.TargetRef or GitEventScopeKind.UnprotectedTargetRef => null,
            _ => eventScope.CompareBase,
        };

        if (!string.IsNullOrEmpty(compareBase) &&
            !string.IsNullOrEmpty(headCommit) &&
            !string.Equals(compareBase, headCommit, StringComparison.Ordinal))
        {
            IEnumerable<string> committedChanges = GitRevisionScopeService.GetChangedFilesBetweenCommits(
                repoRoot,
                compareBase,
                headCommit,
                includeDeletions: false);

            // When comparing against a branch ref (for example after merging origin/main into a feature branch), exclude files
            // whose content matches baseRef verbatim so upstream architectural commits do not require a second ADR on the branch.
            if (eventScope.Kind == GitEventScopeKind.BranchDiff)
            {
                var differsFromBaseRef = GitRevisionScopeService.TryGetPathsDifferingFromRevision(repoRoot, effectiveBaseRef, cached: false);
                if (differsFromBaseRef is not null)
                {
                    var differingSet = new HashSet<string>(differsFromBaseRef, StringComparer.OrdinalIgnoreCase);
                    committedChanges = committedChanges.Where(differingSet.Contains);
                }
            }

            foreach (string file in committedChanges)
            {
                changed.Add(file);
                adrCandidates.Add(file);
            }
        }

        foreach (string workingFile in GitRevisionScopeService.GetWorkingTreeChanges(repoRoot, includeDeletions: false, includeUntracked: true))
        {
            changed.Add(workingFile);
            adrCandidates.Add(workingFile);
        }

        return (changed, adrCandidates, compareBase);
    }
}
