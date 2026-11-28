using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;
using Cadence.ToolingCore.Services;

using Npgsql;

namespace Cadence.Cli.Services;

/// <summary>The side effects of a local CI run, replaceable in tests.</summary>
public sealed class LocalCiEnvironment
{
    /// <summary>The repository root; every command runs here.</summary>
    public required string RepoRoot { get; init; }

    /// <summary>
    /// Runs a command with the given extra arguments and extra environment variables in the repository root and
    /// returns its exit code.
    /// </summary>
    public required Func<CiCommand, IReadOnlyList<string>, IReadOnlyDictionary<string, string>, int> Run { get; init; }

    /// <summary>Reads a file under the repository root, or returns null when it does not exist.</summary>
    public Func<string, string?> ReadFile { get; init; } = path => File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>Writes a file back to the given content, or deletes it when the content is null.</summary>
    public Action<string, string?> RestoreFile { get; init; } = (path, content) =>
    {
        if (content is null) File.Delete(path);
        else if (!File.Exists(path) || File.ReadAllText(path) != content) File.WriteAllText(path, content);
    };

    /// <summary>Whether a program is on <c>PATH</c>.</summary>
    public required Func<string, bool> IsToolOnPath { get; init; }

    /// <summary>Checks whether the tests can reach PostgreSQL. Called at most once per run.</summary>
    public required Func<CiPostgresStatus> ProbePostgres { get; init; }

    /// <summary>Checks whether a Docker daemon is reachable for <c>container-security</c>. Called at most once per run.</summary>
    public Func<CiDockerStatus> ProbeDocker { get; init; } = () => new(true, "Docker daemon is reachable.");

    /// <summary>Resolves whether a default <c>cadence ci</c> run executes <c>container-security</c> against the given base ref.</summary>
    public Func<string, CiContainerScanScope> ShouldRunContainerSecurity { get; init; } = _ => new(true, "");

    /// <summary>Finds the files a step appends to its command.</summary>
    public required Func<CiStep, string, IReadOnlyList<string>> FindFiles { get; init; }

    /// <summary>Called before each step that runs, with the step and the commands it is about to run.</summary>
    public Action<CiStep, IReadOnlyList<string>>? OnStepStarting { get; init; }

    /// <summary>
    /// Lists the database tests that did not execute in the TRX files under a results directory (#5325), optionally
    /// discovering unexecuted test methods under the repository root. Called after a PostgreSQL step passes unless
    /// <c>--allow-db-skip</c> was given.
    /// </summary>
    public Func<string, string?, IReadOnlyList<SkippedDatabaseTest>> FindSkippedDatabaseTests { get; init; } =
        (resultsPath, repoRoot) => DatabaseTestEvidence.FindSkipped(resultsPath, repoRoot, requireBrowserEvidence: false);

    /// <summary>Checks whether every database assembly recorded <c>*.database.txt</c> fixture metadata under the results directory.</summary>
    public Func<string, bool> HasRecordedDatabaseEnvironment { get; init; } = DatabaseTestEvidence.HasRecordedEnvironment;

    /// <summary>Deletes a directory under the repository root, if it exists.</summary>
    public Action<string> DeleteDirectory { get; init; } = path =>
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    };

    /// <summary>
    /// Optional preflight hook that fetches <see cref="LocalCiOptions.BaseRef"/> (unless <see cref="LocalCiOptions.Offline"/>
    /// is set), synthesizes a merged commit via <c>git merge-tree --write-tree</c>, and checks out a detached temporary
    /// worktree under <see cref="Path.GetTempPath"/> while reusing read-only <c>.cadence</c> toolchains from <see cref="RepoRoot"/>.
    /// </summary>
    public Func<LocalCiOptions, CiWorkspacePreparation>? PrepareWorkspace { get; init; }

    /// <summary>The real environment: child processes, <c>PATH</c>, PostgreSQL, Docker and git in a merged detached worktree for <paramref name="repoRoot"/>.</summary>
    public static LocalCiEnvironment Create(string repoRoot, Action<CiStep, IReadOnlyList<string>>? onStepStarting = null) => new()
    {
        RepoRoot = repoRoot,
        Run = (command, extra, variables) => RunProcess(repoRoot, repoRoot, command, extra, variables),
        IsToolOnPath = tool => IsToolAvailable(repoRoot, tool),
        ProbePostgres = ProbePostgresFromEnvironment,
        ProbeDocker = () => ProbeDockerFromEnvironment(repoRoot),
        ShouldRunContainerSecurity = baseRef => ResolveContainerSecurityScope(repoRoot, baseRef),
        FindFiles = (step, baseRef) => DiscoverFiles(repoRoot, step, baseRef),
        OnStepStarting = onStepStarting,
        PrepareWorkspace = options => PrepareMergedWorktree(repoRoot, options, onStepStarting),
    };

    /// <summary>
    /// Prepares an ephemeral detached Git worktree under <see cref="Path.GetTempPath"/> representing <c>HEAD</c> (or an
    /// opt-in uncommitted working-tree snapshot when <see cref="LocalCiOptions.IncludeUncommitted"/> is <c>true</c>) merged
    /// with <see cref="LocalCiOptions.BaseRef"/>, reusing read-only <c>.cadence</c> toolchains from <paramref name="toolchainRoot"/>.
    /// </summary>
    public static CiWorkspacePreparation PrepareMergedWorktree(
        string toolchainRoot,
        LocalCiOptions options,
        Action<CiStep, IReadOnlyList<string>>? onStepStarting = null)
    {
        var dirtyPaths = GitRevisionScopeService.GetWorkingTreeChanges(toolchainRoot);
        if (dirtyPaths.Count > 0 && !options.IncludeUncommitted)
        {
            string listedPaths = dirtyPaths.Count <= 10
                ? string.Join(", ", dirtyPaths)
                : $"{string.Join(", ", dirtyPaths.Take(10))}, ... (+{dirtyPaths.Count - 10} more)";
            return new CiWorkspacePreparation(
                false,
                null,
                null,
                $"uncommitted working-tree or index changes ({dirtyPaths.Count}): {listedPaths}. "
                + "Commit or stash your changes first, or pass --include-uncommitted to validate a temporary snapshot.");
        }

        string effectiveBaseRef = GitRevisionScopeService.NormalizeBaseRef(options.BaseRef);
        if (!options.Offline)
        {
            var fetch = GitRevisionScopeService.FetchBaseRef(toolchainRoot, effectiveBaseRef);
            if (!fetch.Succeeded)
            {
                return new CiWorkspacePreparation(
                    false,
                    null,
                    null,
                    $"git fetch failed for '{effectiveBaseRef}': {fetch.ErrorMessage} (pass --offline only for air-gapped debugging)");
            }

            if (!string.IsNullOrWhiteSpace(fetch.TrackingRef))
            {
                effectiveBaseRef = fetch.TrackingRef;
            }
        }
        else if (dirtyPaths.Count > 0 && GitRevisionScopeService.IsRelativeOrSpecialRevision(effectiveBaseRef))
        {
            string? resolvedOfflineBase = GitRevisionScopeService.TryResolveCommitSha(toolchainRoot, effectiveBaseRef);
            if (!string.IsNullOrEmpty(resolvedOfflineBase))
            {
                effectiveBaseRef = resolvedOfflineBase;
            }
        }

        string pinnedRef = GitRevisionScopeService.CreateTemporaryRefName();
        string headRevision = "HEAD";
        if (dirtyPaths.Count > 0)
        {
            var snapshot = GitRevisionScopeService.SynthesizeWorkingTreeSnapshot(toolchainRoot, pinnedRef);
            if (!snapshot.Succeeded || string.IsNullOrEmpty(snapshot.SnapshotCommitSha))
            {
                GitRevisionScopeService.DeleteTemporaryRef(toolchainRoot, pinnedRef);
                return new CiWorkspacePreparation(
                    false,
                    null,
                    null,
                    $"could not synthesize uncommitted working-tree snapshot: {snapshot.ErrorMessage}");
            }

            headRevision = snapshot.SnapshotCommitSha;
        }

        var synthesized = GitRevisionScopeService.SynthesizeMergedCommit(toolchainRoot, effectiveBaseRef, headRevision, pinnedRef);
        if (!synthesized.MergeTree.Succeeded || string.IsNullOrEmpty(synthesized.MergeCommitSha))
        {
            GitRevisionScopeService.DeleteTemporaryRef(toolchainRoot, pinnedRef);
            string conflictDetails = synthesized.MergeTree.HasConflicts && synthesized.MergeTree.ConflictingFiles.Count > 0
                ? $"merge conflicts with '{effectiveBaseRef}': {string.Join(", ", synthesized.MergeTree.ConflictingFiles)}"
                : (synthesized.MergeTree.ErrorMessage ?? $"could not synthesize merged tree against '{effectiveBaseRef}'");
            return new CiWorkspacePreparation(false, null, null, conflictDetails);
        }

        if (!GitRevisionScopeService.TryCreateDetachedWorktree(
                toolchainRoot,
                synthesized.MergeCommitSha,
                out var worktree,
                out string? worktreeError,
                pinnedRef: pinnedRef) ||
            worktree is null)
        {
            GitRevisionScopeService.DeleteTemporaryRef(toolchainRoot, pinnedRef);
            return new CiWorkspacePreparation(
                false,
                null,
                null,
                $"could not materialize detached worktree under Path.GetTempPath(): {worktreeError}");
        }

        string worktreeRoot = worktree.WorktreePath;
        var scopedEnvironment = new LocalCiEnvironment
        {
            RepoRoot = worktreeRoot,
            Run = (command, extra, variables) => RunProcess(worktreeRoot, toolchainRoot, command, extra, variables),
            IsToolOnPath = tool => IsToolAvailable(toolchainRoot, tool),
            ProbePostgres = ProbePostgresFromEnvironment,
            ProbeDocker = () => ProbeDockerFromEnvironment(toolchainRoot),
            ShouldRunContainerSecurity = baseRef => ResolveContainerSecurityScope(worktreeRoot, baseRef),
            FindFiles = (step, baseRef) => DiscoverFiles(worktreeRoot, step, baseRef),
            OnStepStarting = onStepStarting,
        };

        string shortSha = synthesized.MergeCommitSha[..Math.Min(12, synthesized.MergeCommitSha.Length)];
        string headLabel = dirtyPaths.Count > 0 ? $"snapshot {shortSha} ({dirtyPaths.Count} uncommitted file(s))" : $"HEAD {shortSha}";
        string message = synthesized.MergeTree.BehindCount > 0
            ? $"synthesized merged commit {shortSha} ({synthesized.MergeTree.BehindCount} commit(s) behind {effectiveBaseRef}) in {worktreeRoot}"
            : $"verified {headLabel} up to date with {effectiveBaseRef} in {worktreeRoot}";
        return new CiWorkspacePreparation(true, scopedEnvironment, worktree, message, EffectiveBaseRef: effectiveBaseRef);
    }

    private static int RunProcess(
        string workingDirectory,
        string toolchainRoot,
        CiCommand command,
        IReadOnlyList<string> extra,
        IReadOnlyDictionary<string, string> variables)
    {
        string executable = ResolveVenvToolPath(toolchainRoot, command.FileName) ?? command.FileName;
        var psi = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false };
        foreach (var argument in command.Arguments.Concat(extra))
        {
            psi.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in variables)
        {
            psi.Environment[name] = value;
        }

        string basePath = variables.TryGetValue("PATH", out string? customPath)
            ? customPath
            : (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        string augmentedPath = PrependReadOnlyToolchainPaths(toolchainRoot, basePath);
        if (augmentedPath.Length > 0)
        {
            psi.Environment["PATH"] = augmentedPath;
        }

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null) return 1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine($"Could not start '{command.FileName}': {ex.Message}");
            return 127;
        }
    }

    /// <summary>
    /// Prepends existing read-only <c>.cadence/bin</c>, <c>.cadence/venv</c>, and <c>.venv</c> binary directories from
    /// <paramref name="toolchainRoot"/> to <paramref name="existingPath"/> so commands running inside a detached temporary
    /// worktree reuse provisioned linters and Python tools without mutating or symlinking into the worktree.
    /// </summary>
    public static string PrependReadOnlyToolchainPaths(string toolchainRoot, string? existingPath)
    {
        var entries = new List<string>();
        foreach (string dir in GetToolchainDirectories(toolchainRoot))
        {
            if (Directory.Exists(dir) && !entries.Contains(dir, StringComparer.Ordinal))
            {
                entries.Add(dir);
            }
        }

        if (!string.IsNullOrEmpty(existingPath))
        {
            entries.Add(existingPath);
        }

        return string.Join(Path.PathSeparator, entries);
    }

    /// <summary>Whether <paramref name="tool"/> is in <c>.cadence/bin</c>, <c>.cadence/venv</c>, <c>.venv</c>, or a <c>PATH</c> directory.</summary>
    public static bool IsToolAvailable(string repoRoot, string tool) =>
        ResolveVenvToolPath(repoRoot, tool) is not null || IsOnPath(tool);

    private static string[] GetToolchainDirectories(string repoRoot)
    {
        string binSubdir = OperatingSystem.IsWindows() ? "Scripts" : "bin";
        return
        [
            Path.Combine(repoRoot, ".cadence", "bin"),
            Path.Combine(repoRoot, ".cadence", "venv", binSubdir),
            Path.Combine(repoRoot, ".venv", binSubdir),
        ];
    }

    private static string? ResolveVenvToolPath(string repoRoot, string tool)
    {
        string[] suffixes = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        string[] searchDirs = GetToolchainDirectories(repoRoot);

        foreach (string dir in searchDirs)
        {
            foreach (string suffix in suffixes)
            {
                string candidate = Path.Combine(dir, tool + suffix);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="tool"/> is an executable file in a <c>PATH</c> directory.</summary>
    public static bool IsOnPath(string tool) =>
        PosixProcessLauncher.ResolveExecutablePath(tool) is not null;

    /// <summary>
    /// The PostgreSQL major the database tests qualify; it equals <c>Cadence.Testing.Persistence.PostgresTestImage.Major</c>
    /// and the docker compose image, which a unit test checks (#5438).
    /// </summary>
    public const int PostgresMajor = 18;

    /// <summary>
    /// Mirrors how the test fixtures choose a database (docs/TESTING.md): <c>CADENCE_TEST_POSTGRES</c> first, then
    /// <c>CADENCE_TEST_DOCKER=1</c>. A connection string is opened once so an unreachable server, or one of another
    /// major than <see cref="PostgresMajor"/>, fails before the test run instead of turning every database test into
    /// a skip or a failure.
    /// </summary>
    public static CiPostgresStatus ProbePostgresFromEnvironment()
    {
        string? connectionString = Environment.GetEnvironmentVariable("CADENCE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Environment.GetEnvironmentVariable("CADENCE_TEST_DOCKER") == "1"
                ? new CiPostgresStatus(true, $"CADENCE_TEST_DOCKER=1: the tests start a PostgreSQL {PostgresMajor} container.")
                : new CiPostgresStatus(false, "Neither CADENCE_TEST_POSTGRES nor CADENCE_TEST_DOCKER=1 is set.");
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 5 };
            using var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            int major = connection.PostgreSqlVersion.Major;
            return major == PostgresMajor
                ? new CiPostgresStatus(true, $"CADENCE_TEST_POSTGRES reaches PostgreSQL {major} at {builder.Host}:{builder.Port}.")
                : new CiPostgresStatus(false,
                    $"CADENCE_TEST_POSTGRES reaches PostgreSQL {major} at {builder.Host}:{builder.Port}, but the tests qualify PostgreSQL {PostgresMajor} (docs/TESTING.md).");
        }
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException or InvalidOperationException or TimeoutException)
        {
            // The message names the host and the error, never the connection string, which may hold a password.
            return new CiPostgresStatus(false, $"CADENCE_TEST_POSTGRES is set but the server cannot be reached: {ex.Message}");
        }
    }

    /// <summary>Checks whether <c>docker info</c> reaches a running Docker daemon within 10 seconds.</summary>
    public static CiDockerStatus ProbeDockerFromEnvironment(string repoRoot)
    {
        string executable = ResolveVenvToolPath(repoRoot, "docker") ?? "docker";
        var psi = new ProcessStartInfo(executable)
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("info");

        try
        {
            using var process = PosixProcessLauncher.Start(psi);
            if (process is null)
            {
                return new CiDockerStatus(false, "Could not start 'docker info'.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
                return new CiDockerStatus(false, "'docker info' timed out after 10s.");
            }

            _ = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode == 0)
            {
                return new CiDockerStatus(true, "Docker daemon is reachable.");
            }

            string firstLine = stderr
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? $"exit code {process.ExitCode}";
            return new CiDockerStatus(false, $"Docker daemon is not reachable ({firstLine}).");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new CiDockerStatus(false, $"Docker daemon is not reachable ({ex.Message}).");
        }
    }

    /// <summary>
    /// Mirrors <c>classify</c> and <c>container-security</c> in <c>.github/workflows/ci.yml</c>: runs unconditionally when
    /// <see cref="GitRevisionScopeService.ResolveFileScanScope"/> falls back to all tracked files, and on a branch diff
    /// runs only when at least one changed (or deleted/renamed) path matches <see cref="CiPlan.IsContainerScanTrigger"/>.
    /// </summary>
    public static CiContainerScanScope ResolveContainerSecurityScope(string repoRoot, string baseRef)
    {
        var scope = GitRevisionScopeService.ResolveFileScanScope(repoRoot, baseRef: baseRef);
        if (scope.UsedTrackedFallback || scope.Kind != GitScopeKind.BranchDiff || string.IsNullOrEmpty(scope.CompareBase))
        {
            return new CiContainerScanScope(true, "");
        }

        var changedFiles = GitRevisionScopeService.CollectChangedFilesSinceCommit(
            repoRoot, scope.CompareBase, includeDeletions: true, includeUntracked: false);
        if (changedFiles.Any(CiPlan.IsContainerScanTrigger))
        {
            return new CiContainerScanScope(true, "");
        }

        return new CiContainerScanScope(
            false,
            $"no container or dependency changes against {scope.EffectiveBaseRef} (pass --job {CiPlan.ContainerSecurityJobKey} to force)");
    }

    /// <summary>Finds the files for <see cref="CiStep.Files"/>, using <see cref="GitRevisionScopeService"/> for changed files.</summary>
    public static IReadOnlyList<string> DiscoverFiles(string repoRoot, CiStep step, string baseRef)
    {
        IEnumerable<string> files = step.Files switch
        {
            CiFileSource.ChangedFiles or CiFileSource.ChangedFilesAcmr =>
                GitRevisionScopeService.ResolveFileScanScope(repoRoot, baseRef: baseRef).Files,
            CiFileSource.Dockerfiles => FindUnderRoot(repoRoot, "Dockerfile*",
                path => !path.Contains("/.venv/", StringComparison.Ordinal)
                    && !path.Contains("/.cadence/", StringComparison.Ordinal)
                    && !path.Contains("/node_modules/", StringComparison.Ordinal)),
            CiFileSource.ShellScripts => DiscoverShellScripts(repoRoot),
            _ => [],
        };

        return [.. files];
    }

    private static IEnumerable<string> DiscoverShellScripts(string repoRoot)
    {
        static bool KeepPath(string path) =>
            !path.Contains("/.git/", StringComparison.Ordinal)
            && !path.Contains("/.venv/", StringComparison.Ordinal)
            && !path.Contains("/.cadence/", StringComparison.Ordinal)
            && !path.Contains("/node_modules/", StringComparison.Ordinal);

        var scripts = FindUnderRoot(repoRoot, "*.sh", KeepPath);
        string gitHooksDir = Path.Combine(repoRoot, ".githooks");
        if (!Directory.Exists(gitHooksDir))
        {
            return scripts;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseSensitive,
        };

        var hooks = Directory.EnumerateFiles(gitHooksDir, "*", options)
            .Select(path => "./" + Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Where(KeepPath);

        return scripts
            .Concat(hooks)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
    }

    /// <summary>Like <c>find . -type f -name pattern</c>: regular files only, symbolic links are not followed.</summary>
    private static IEnumerable<string> FindUnderRoot(string repoRoot, string pattern, Func<string, bool> keep)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseSensitive,
        };

        return Directory.EnumerateFiles(repoRoot, pattern, options)
            .Select(path => "./" + Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Where(keep)
            .Order(StringComparer.Ordinal);
    }
}
