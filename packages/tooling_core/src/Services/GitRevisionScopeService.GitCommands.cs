using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static partial class GitRevisionScopeService
{
    /// <summary>Resolves a Git revision to its commit SHA (<c>rev-parse --verify -q &lt;rev&gt;^{commit}</c>).</summary>
    public static string? TryResolveCommitSha(string repoRoot, string revision) =>
        HasGitRepository(repoRoot)
            ? RunGitSingleLine(repoRoot, "rev-parse", "--verify", "-q", $"{revision}^{{commit}}")
            : null;

    /// <summary>Resolves a Git revision via <c>rev-parse --verify -q</c>.</summary>
    public static string? TryResolveRevision(string repoRoot, string revision) =>
        HasGitRepository(repoRoot)
            ? RunGitSingleLine(repoRoot, "rev-parse", "--verify", "-q", revision)
            : null;

    /// <summary>
    /// Collects changed paths between <paramref name="compareBase"/> and the working tree / index (plus untracked files when
    /// <paramref name="includeUntracked"/> is <c>true</c>).
    /// </summary>
    public static List<string> CollectChangedFilesSinceCommit(
        string repoRoot,
        string compareBase,
        bool includeDeletions = true,
        bool includeUntracked = false)
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(compareBase))
        {
            return [];
        }

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        string[] workingDiffArgs = includeDeletions
            ? ["diff", "--name-only", "-z", compareBase]
            : ["diff", "--name-only", "--diff-filter=d", "-z", compareBase];
        string[] cachedDiffArgs = includeDeletions
            ? ["diff", "--cached", "--name-only", "-z", compareBase]
            : ["diff", "--cached", "--name-only", "--diff-filter=d", "-z", compareBase];

        foreach (string path in RunGitNullTerminated(repoRoot, workingDiffArgs) ?? [])
        {
            changed.Add(NormalizePath(path));
        }

        foreach (string path in RunGitNullTerminated(repoRoot, cachedDiffArgs) ?? [])
        {
            changed.Add(NormalizePath(path));
        }

        if (includeUntracked)
        {
            foreach (string path in RunGitNullTerminated(repoRoot, "ls-files", "--others", "--exclude-standard", "-z") ?? [])
            {
                changed.Add(NormalizePath(path));
            }
        }

        changed.Remove(string.Empty);
        return [.. changed];
    }

    /// <summary>Collects paths changed between <paramref name="fromCommit"/> and <paramref name="toCommit"/>.</summary>
    public static List<string> GetChangedFilesBetweenCommits(
        string repoRoot,
        string fromCommit,
        string toCommit,
        bool includeDeletions = true)
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(fromCommit) || string.IsNullOrWhiteSpace(toCommit))
        {
            return [];
        }

        string[] args = includeDeletions
            ? ["diff", "--name-only", "-z", fromCommit, toCommit]
            : ["diff", "--name-only", "--diff-filter=d", "-z", fromCommit, toCommit];

        return NormalizePaths(RunGitNullTerminated(repoRoot, args) ?? []);
    }

    /// <summary>Collects uncommitted staged, unstaged, and untracked paths in <paramref name="repoRoot"/>.</summary>
    public static List<string> GetWorkingTreeChanges(string repoRoot, bool includeDeletions = true, bool includeUntracked = true)
    {
        if (!HasGitRepository(repoRoot))
        {
            return [];
        }

        var changed = new SortedSet<string>(StringComparer.Ordinal);
        string? headCommit = TryResolveCommitSha(repoRoot, "HEAD");

        if (!string.IsNullOrEmpty(headCommit))
        {
            string[] headDiffArgs = includeDeletions
                ? ["diff", "--name-only", "-z", "HEAD"]
                : ["diff", "--name-only", "--diff-filter=d", "-z", "HEAD"];
            foreach (string path in RunGitNullTerminated(repoRoot, headDiffArgs) ?? [])
            {
                changed.Add(NormalizePath(path));
            }
        }

        string[] stagedArgs = includeDeletions
            ? ["diff", "--cached", "--name-only", "-z"]
            : ["diff", "--cached", "--name-only", "--diff-filter=d", "-z"];
        foreach (string path in RunGitNullTerminated(repoRoot, stagedArgs) ?? [])
        {
            changed.Add(NormalizePath(path));
        }

        if (includeUntracked)
        {
            foreach (string path in RunGitNullTerminated(repoRoot, "ls-files", "--others", "--exclude-standard", "-z") ?? [])
            {
                changed.Add(NormalizePath(path));
            }
        }

        changed.Remove(string.Empty);
        return [.. changed];
    }

    /// <summary>Lists commits in <c>&lt;compareBase&gt;..&lt;head&gt;</c> in chronological order (<c>--reverse</c>).</summary>
    public static List<string> GetCommitRange(string repoRoot, string compareBase, string head = "HEAD")
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(compareBase))
        {
            return [];
        }

        return RunGitLines(repoRoot, "rev-list", "--reverse", $"{compareBase}..{head}") ?? [];
    }

    /// <summary>Reads file content at <c>&lt;revision&gt;:&lt;relativePath&gt;</c> (use empty revision for staged index <c>:&lt;path&gt;</c>).</summary>
    public static string? ReadFileAtRevision(string repoRoot, string revision, string relativePath)
    {
        if (!HasGitRepository(repoRoot))
        {
            return null;
        }

        string normalized = NormalizePath(relativePath);
        string spec = string.IsNullOrEmpty(revision) ? $":{normalized}" : $"{revision}:{normalized}";
        return RunGitRaw(repoRoot, "show", spec);
    }

    /// <summary>Lists paths differing between the working tree (or staged index when <paramref name="cached"/> is true) and <paramref name="revision"/>.</summary>
    public static List<string>? TryGetPathsDifferingFromRevision(string repoRoot, string revision, bool cached)
    {
        if (!HasGitRepository(repoRoot) || string.IsNullOrWhiteSpace(revision))
        {
            return null;
        }

        string[] args = cached
            ? ["diff", "--cached", "--name-only", "-z", revision]
            : ["diff", "--name-only", "-z", revision];
        var raw = RunGitNullTerminated(repoRoot, args);
        return raw is null ? null : NormalizePaths(raw);
    }

    /// <summary>Whether <paramref name="value"/> consists solely of <c>'0'</c> characters (such as Git's 40-zero null SHA).</summary>
    public static bool IsAllZeros(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().All(c => c == '0');

    /// <summary>Whether <paramref name="revision"/> names a tag reference (<c>refs/tags/*</c> or a local tag name).</summary>
    public static bool IsTagReference(string repoRoot, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return false;
        }

        string trimmed = revision.Trim();
        if (trimmed.StartsWith("refs/tags/", StringComparison.Ordinal))
        {
            return true;
        }

        return HasGitRepository(repoRoot) &&
               RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/tags/{trimmed}");
    }

    /// <summary>Whether <paramref name="revision"/> is a relative, peeled, or special Git revision rather than a branch ref.</summary>
    public static bool IsRelativeOrSpecialRevision(string? revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return false;
        }

        string trimmed = revision.Trim();
        if (trimmed is "HEAD" or "@")
        {
            return true;
        }

        if (trimmed.IndexOfAny(['~', '^', ':']) >= 0 || trimmed.Contains("@{", StringComparison.Ordinal))
        {
            return true;
        }

        return trimmed.StartsWith("refs/", StringComparison.Ordinal)
            && !trimmed.StartsWith("refs/remotes/", StringComparison.Ordinal)
            && !trimmed.StartsWith("refs/heads/", StringComparison.Ordinal);
    }

    public static List<string>? RunGitNullTerminated(string repoRoot, params string[] args)
    {
        string? output = RunGitRaw(repoRoot, args);
        if (output is null)
        {
            return null;
        }

        return [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
    }

    public static List<string>? RunGitLines(string repoRoot, params string[] args)
    {
        string? output = RunGitRaw(repoRoot, args);
        if (output is null)
        {
            return null;
        }

        return [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    public static string? RunGitSingleLine(string repoRoot, params string[] args) =>
        RunGitLines(repoRoot, args)?.FirstOrDefault();

    public static bool RunGitZeroExit(string repoRoot, params string[] args) =>
        RunGitRaw(repoRoot, args) is not null;

    public static string? RunGitRaw(string repoRoot, params string[] args)
    {
        var (exitCode, stdout, _) = RunGitCaptured(repoRoot, args);
        return exitCode == 0 ? stdout : null;
    }

    public static (int ExitCode, string Stdout, string Stderr) RunGitCaptured(string repoRoot, params string[] args) =>
        RunGitCapturedWithEnv(repoRoot, environment: null, args);

    public static (int ExitCode, string Stdout, string Stderr) RunGitCapturedWithEnv(
        string repoRoot,
        IReadOnlyDictionary<string, string>? environment,
        params string[] args)
    {
        var psi = ProcessPrimitives.CreateStartInfo("git", repoRoot, args);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }

        try
        {
            var output = ProcessPrimitives.RunCaptured(psi);
            return (output.ExitCode, output.Stdout, output.Stderr);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return (-1, string.Empty, ex.Message);
        }
    }

    private static bool IsLocalNonBranchRevision(string repoRoot, string normalized)
    {
        if (IsTagReference(repoRoot, normalized) || IsRelativeOrSpecialRevision(normalized))
        {
            return true;
        }

        if (!IsHexRevisionCandidate(normalized))
        {
            return false;
        }

        if (!HasGitRepository(repoRoot))
        {
            return normalized.Length is 40 or 64;
        }

        if (RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/heads/{normalized}")
            || RunGitZeroExit(repoRoot, "show-ref", "--verify", "--quiet", $"refs/remotes/origin/{normalized}"))
        {
            return false;
        }

        return normalized.Length is 40 or 64 || TryResolveCommitSha(repoRoot, normalized) is not null;
    }

    private static bool IsHexRevisionCandidate(string value) =>
        value.Length is >= 4 and <= 64 && !IsAllZeros(value) && value.All(Uri.IsHexDigit);

    private static string? FirstNonEmptyLine(string? text) =>
        text?
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static string FormatGitFailure(string commandLabel, int exitCode, string stdout, string stderr)
    {
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            return stderr.Trim();
        }

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            return stdout.Trim();
        }

        return $"{commandLabel} exited with code {exitCode}.";
    }

    private static void DeleteFileBestEffort(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of temporary index file.
        }
    }

    private static List<string> NormalizeExplicitPaths(string repoRoot, IEnumerable<string> explicitFiles)
    {
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in explicitFiles)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full = Path.IsPathRooted(raw) ? Path.GetFullPath(raw) : Path.GetFullPath(Path.Combine(repoRoot, raw));
            string rel = NormalizePath(Path.GetRelativePath(repoRoot, full));
            if (rel.Length > 0 && !rel.StartsWith("../", StringComparison.Ordinal) && rel != "..")
            {
                normalized.Add(rel);
            }
        }

        return [.. normalized];
    }

    private static List<string> NormalizePaths(IEnumerable<string> rawPaths)
    {
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in rawPaths)
        {
            string rel = NormalizePath(raw);
            if (rel.Length > 0)
            {
                normalized.Add(rel);
            }
        }

        return [.. normalized];
    }

    private static string NormalizePath(string path)
    {
        string normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }
}
