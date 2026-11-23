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

/// <summary>What happened to one step of a local CI run.</summary>
public enum CiOutcome
{
    /// <summary>Every command exited 0.</summary>
    Passed,

    /// <summary>A command exited non-zero, or the step could not start (for example, no PostgreSQL).</summary>
    Failed,

    /// <summary>The step was not run: <c>--allow-missing-tools</c> skipped a missing tool, or the step only runs on GitHub.</summary>
    Skipped,

    /// <summary>Another step that runs the same check passed earlier in this run.</summary>
    Covered,

    /// <summary>An earlier step of the job, or a job it needs, failed, and CI would not run it either.</summary>
    NotRun,
}

/// <summary>The result of one step.</summary>
/// <param name="Step">The step.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Duration">How long it ran.</param>
/// <param name="Note">Why it was skipped or failed, or what it covered.</param>
public sealed record CiStepResult(CiStep Step, CiOutcome Outcome, TimeSpan Duration, string Note);

/// <summary>Whether the .NET tests can reach PostgreSQL.</summary>
/// <param name="Available">True when <c>CADENCE_TEST_POSTGRES</c> connects or <c>CADENCE_TEST_DOCKER=1</c> is set.</param>
/// <param name="Message">What was found, for the summary.</param>
public sealed record CiPostgresStatus(bool Available, string Message);

/// <summary>The choices given on the command line.</summary>
/// <param name="AllowMissingTools">Skip steps whose required host tool binary is missing on <c>PATH</c>.</param>
/// <param name="AllowDbSkip">Allow .NET test steps to run without PostgreSQL or accept skipped database tests.</param>
/// <param name="BaseRef">The ref that "changed files" and merged worktree synthesis are compared with, as <c>origin/main</c> is in CI.</param>
/// <param name="Offline">Skip <c>git fetch</c> before synthesizing the merged worktree (explicit air-gapped debugging only).</param>
public sealed record LocalCiOptions(bool AllowMissingTools, bool AllowDbSkip, string BaseRef, bool Offline = false)
{
    public LocalCiOptions(bool AllowSkip, string BaseRef, bool Offline = false)
        : this(AllowMissingTools: AllowSkip, AllowDbSkip: AllowSkip, BaseRef: BaseRef, Offline: Offline)
    {
    }
}

/// <summary>Result of preparing a disposable merged worktree before running local CI jobs.</summary>
/// <param name="Succeeded">Whether remote fetch (when online), merge-tree synthesis, and worktree creation succeeded.</param>
/// <param name="Environment">Worktree-scoped <see cref="LocalCiEnvironment"/> when <paramref name="Succeeded"/> is <c>true</c>.</param>
/// <param name="Lease">Disposable lease that removes and prunes the temporary worktree after the run finishes.</param>
/// <param name="Message">Diagnostic summary or failure details (including conflicted file paths).</param>
public sealed record CiWorkspacePreparation(
    bool Succeeded,
    LocalCiEnvironment? Environment,
    IDisposable? Lease,
    string Message);

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

    /// <summary>Finds the files a step appends to its command.</summary>
    public required Func<CiStep, string, IReadOnlyList<string>> FindFiles { get; init; }

    /// <summary>Called before each step that runs, with the step and the commands it is about to run.</summary>
    public Action<CiStep, IReadOnlyList<string>>? OnStepStarting { get; init; }

    /// <summary>
    /// Lists the database tests that did not execute in the TRX files under a results directory (#5325). Called after
    /// a PostgreSQL step passes, unless <c>--allow-db-skip</c> was given.
    /// </summary>
    public Func<string, IReadOnlyList<SkippedDatabaseTest>> FindSkippedDatabaseTests { get; init; } = DatabaseTestEvidence.FindSkipped;

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

    /// <summary>The real environment: child processes, <c>PATH</c>, PostgreSQL and git in a merged detached worktree for <paramref name="repoRoot"/>.</summary>
    public static LocalCiEnvironment Create(string repoRoot, Action<CiStep, IReadOnlyList<string>>? onStepStarting = null) => new()
    {
        RepoRoot = repoRoot,
        Run = (command, extra, variables) => RunProcess(repoRoot, repoRoot, command, extra, variables),
        IsToolOnPath = tool => IsToolAvailable(repoRoot, tool),
        ProbePostgres = ProbePostgresFromEnvironment,
        FindFiles = (step, baseRef) => DiscoverFiles(repoRoot, step, baseRef),
        OnStepStarting = onStepStarting,
        PrepareWorkspace = options => PrepareMergedWorktree(repoRoot, options, onStepStarting),
    };

    /// <summary>
    /// Prepares an ephemeral detached Git worktree under <see cref="Path.GetTempPath"/> representing <c>HEAD</c> merged
    /// with <see cref="LocalCiOptions.BaseRef"/>, reusing read-only <c>.cadence</c> toolchains from <paramref name="toolchainRoot"/>.
    /// </summary>
    public static CiWorkspacePreparation PrepareMergedWorktree(
        string toolchainRoot,
        LocalCiOptions options,
        Action<CiStep, IReadOnlyList<string>>? onStepStarting = null)
    {
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
        }

        var synthesized = GitRevisionScopeService.SynthesizeMergedCommit(toolchainRoot, effectiveBaseRef, "HEAD");
        if (!synthesized.MergeTree.Succeeded || string.IsNullOrEmpty(synthesized.MergeCommitSha))
        {
            string conflictDetails = synthesized.MergeTree.HasConflicts && synthesized.MergeTree.ConflictingFiles.Count > 0
                ? $"merge conflicts with '{effectiveBaseRef}': {string.Join(", ", synthesized.MergeTree.ConflictingFiles)}"
                : (synthesized.MergeTree.ErrorMessage ?? $"could not synthesize merged tree against '{effectiveBaseRef}'");
            return new CiWorkspacePreparation(false, null, null, conflictDetails);
        }

        if (!GitRevisionScopeService.TryCreateDetachedWorktree(
                toolchainRoot,
                synthesized.MergeCommitSha,
                out var worktree,
                out string? worktreeError) ||
            worktree is null)
        {
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
            FindFiles = (step, baseRef) => DiscoverFiles(worktreeRoot, step, baseRef),
            OnStepStarting = onStepStarting,
        };

        string shortSha = synthesized.MergeCommitSha[..Math.Min(12, synthesized.MergeCommitSha.Length)];
        string message = synthesized.MergeTree.BehindCount > 0
            ? $"synthesized merged commit {shortSha} ({synthesized.MergeTree.BehindCount} commit(s) behind {effectiveBaseRef}) in {worktreeRoot}"
            : $"verified HEAD {shortSha} up to date with {effectiveBaseRef} in {worktreeRoot}";
        return new CiWorkspacePreparation(true, scopedEnvironment, worktree, message);
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
    public static bool IsOnPath(string tool)
    {
        string[] suffixes = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return directories.Any(dir => suffixes.Any(suffix => File.Exists(Path.Combine(dir, tool + suffix))));
    }

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
            CiFileSource.ShellScripts => FindUnderRoot(repoRoot, "*.sh",
                path => !path.Contains("/.venv/", StringComparison.Ordinal)
                    && !path.Contains("/.cadence/", StringComparison.Ordinal)
                    && !path.Contains("/node_modules/", StringComparison.Ordinal)),
            _ => [],
        };

        return [.. files];
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

/// <summary>
/// Runs the steps of <see cref="CiPlan"/> the way GitHub Actions runs <c>ci.yml</c>: jobs in order, and inside a job a
/// failed step stops the later steps unless they have <c>if: always()</c>. Nothing stops the run as a whole, so the
/// summary shows every failure at once.
/// </summary>
public static class LocalCiRunner
{
    /// <summary>Runs <paramref name="jobs"/> and returns one result per step, in order.</summary>
    public static IReadOnlyList<CiStepResult> Run(IReadOnlyList<CiJob> jobs, LocalCiOptions options, LocalCiEnvironment environment)
    {
        var results = new List<CiStepResult>();
        IDisposable? workspaceLease = null;
        var activeEnvironment = environment;

        if (environment.PrepareWorkspace is not null)
        {
            var preflightWatch = Stopwatch.StartNew();
            var preflightStep = new CiStep("merge-readiness", "Prepare merged worktree");
            string modeNote = options.Offline ? $"--offline --base {options.BaseRef}" : $"git fetch + git merge-tree --write-tree ({options.BaseRef})";
            environment.OnStepStarting?.Invoke(preflightStep, [modeNote]);

            var prepared = environment.PrepareWorkspace(options);
            if (!prepared.Succeeded || prepared.Environment is null)
            {
                prepared.Lease?.Dispose();
                results.Add(new CiStepResult(preflightStep, CiOutcome.Failed, preflightWatch.Elapsed, prepared.Message));
                foreach (var skippedStep in jobs.SelectMany(j => j.Steps))
                {
                    results.Add(new CiStepResult(skippedStep, CiOutcome.NotRun, TimeSpan.Zero, "merged worktree preparation failed"));
                }

                return results;
            }

            workspaceLease = prepared.Lease;
            activeEnvironment = prepared.Environment;
        }

        using (workspaceLease)
        {
            return RunJobsInWorkspace(jobs, options, activeEnvironment, results);
        }
    }

    private static List<CiStepResult> RunJobsInWorkspace(
        IReadOnlyList<CiJob> jobs,
        LocalCiOptions options,
        LocalCiEnvironment environment,
        List<CiStepResult> results)
    {
        var byKey = new Dictionary<string, CiStepResult>(StringComparer.Ordinal);
        CiPostgresStatus? postgres = null;

        foreach (var job in jobs)
        {
            string? blocked = UnmetNeed(job, jobs, byKey);
            bool jobFailed = false;

            foreach (var step in job.Steps)
            {
                CiStepResult result;
                if (blocked is not null)
                {
                    result = new(step, CiOutcome.NotRun, TimeSpan.Zero, blocked);
                }
                else if (step.NotRunLocally is not null)
                {
                    result = new(step, CiOutcome.Skipped, TimeSpan.Zero, step.NotRunLocally);
                }
                else if (jobFailed && !step.Always)
                {
                    result = new(step, CiOutcome.NotRun, TimeSpan.Zero, "an earlier step of this job failed");
                }
                else if (step.CoveredBy is not null && byKey.TryGetValue(step.CoveredBy, out var cover) && cover.Outcome == CiOutcome.Passed)
                {
                    result = new(step, CiOutcome.Covered, TimeSpan.Zero, $"passed in '{cover.Step.Name}' ({cover.Step.Job})");
                }
                else if (step.RequiredTool is not null && !environment.IsToolOnPath(step.RequiredTool))
                {
                    string failSuffix = step.ToolHint?.Contains("--allow-missing-tools", StringComparison.Ordinal) is true
                        ? ""
                        : " (or pass --allow-missing-tools to skip)";
                    result = options.AllowMissingTools
                        ? new(step, CiOutcome.Skipped, TimeSpan.Zero, $"{step.RequiredTool} is not on PATH; {step.ToolHint}")
                        : new(step, CiOutcome.Failed, TimeSpan.Zero, $"{step.RequiredTool} is not on PATH; {step.ToolHint}{failSuffix}");
                }
                else
                {
                    string note = "";
                    if (step.NeedsPostgres)
                    {
                        postgres ??= environment.ProbePostgres();
                        if (!postgres.Available && !options.AllowDbSkip)
                        {
                            result = new(step, CiOutcome.Failed, TimeSpan.Zero,
                                $"{postgres.Message} CI gives these tests a database; set CADENCE_TEST_POSTGRES=<connection string> "
                                + "or CADENCE_TEST_DOCKER=1 (docs/TESTING.md), or pass --allow-db-skip to run them as skipped.");
                            Record(result);
                            continue;
                        }

                        note = postgres.Available ? postgres.Message : $"PostgreSQL tests skipped (--allow-db-skip): {postgres.Message}";
                    }

                    result = RunStep(step, options, environment, note, byKey);
                    if (step.NeedsPostgres && result.Outcome == CiOutcome.Passed && !options.AllowDbSkip)
                    {
                        result = RejectSkippedDatabaseTests(result, environment);
                    }
                }

                Record(result);
            }

            void Record(CiStepResult result)
            {
                results.Add(result);
                byKey[result.Step.Key] = result;
                jobFailed |= result.Outcome == CiOutcome.Failed;
            }
        }

        return results;
    }

    /// <summary>The reason a job cannot run because a job it needs did not pass, or null.</summary>
    private static string? UnmetNeed(CiJob job, IReadOnlyList<CiJob> jobs, Dictionary<string, CiStepResult> byKey)
    {
        foreach (var need in job.Needs)
        {
            if (job.NeedsStep.TryGetValue(need, out var stepKey))
            {
                if (!byKey.TryGetValue(stepKey, out var needed) || needed.Outcome != CiOutcome.Passed)
                {
                    return $"needs '{stepKey}' to pass, as in CI";
                }
            }
            else
            {
                var neededJob = jobs.FirstOrDefault(j => j.Key == need);
                if (neededJob is null || neededJob.Steps.Any(s => !byKey.TryGetValue(s.Key, out var r) || r.Outcome is CiOutcome.Failed or CiOutcome.NotRun))
                {
                    return $"needs job '{need}' to pass, as in CI";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fails a passing test step whose results show database tests that did not execute: a green run must have
    /// exercised them (#5325). Opt-in tests are exempt (<see cref="DatabaseTestEvidence.OptInTests"/>).
    /// </summary>
    private static CiStepResult RejectSkippedDatabaseTests(CiStepResult result, LocalCiEnvironment environment)
    {
        var skipped = environment.FindSkippedDatabaseTests(Path.Combine(environment.RepoRoot, RtmGeneratorService.DefaultResultsDir));
        if (skipped.Count == 0)
        {
            return result;
        }

        string examples = string.Join(", ", skipped.Take(3).Select(t => t.FullName));
        return result with
        {
            Outcome = CiOutcome.Failed,
            Note = Join(result.Note, $"{skipped.Count} database test(s) did not execute ({examples}{(skipped.Count > 3 ? ", ..." : "")}); "
                + "pass --allow-db-skip to accept a run without them."),
        };
    }

    private static CiStepResult RunStep(
        CiStep step, LocalCiOptions options, LocalCiEnvironment environment, string note, Dictionary<string, CiStepResult> earlier)
    {
        IReadOnlyList<string> files = [];
        if (step.Files != CiFileSource.None)
        {
            files = environment.FindFiles(step, options.BaseRef);
            if (files.Count == 0)
            {
                return new(step, CiOutcome.Passed, TimeSpan.Zero, step.NoFilesMessage ?? "no matching files");
            }
        }

        var display = step.Commands
            .Select((c, i) =>
            {
                string line = c.ForwardsBaseRef && !string.IsNullOrWhiteSpace(options.BaseRef)
                    ? $"{c.Display} --base {CiCommand.QuoteArgument(options.BaseRef)}"
                    : c.Display;
                return i == step.Commands.Count - 1 && files.Count > 0 ? $"{line} <{files.Count} files>" : line;
            })
            .ToList();
        if (step.InProcess is not null)
        {
            display.Add(step.InProcessDisplay ?? step.Name);
        }

        var variables = new Dictionary<string, string>(step.Environment, StringComparer.Ordinal);
        foreach (var conditional in step.EnvironmentIfPassed.Where(e => earlier.TryGetValue(e.StepKey, out var r) && r.Outcome == CiOutcome.Passed))
        {
            variables[conditional.Name] = conditional.Value;
        }
        if (step.NeedsPostgres)
        {
            // The database fixtures record the server, extensions, migrations and role they used next to the TRX files.
            variables["CADENCE_TEST_EVIDENCE_DIR"] = Path.Combine(environment.RepoRoot, RtmGeneratorService.DefaultResultsDir);
        }
        display.AddRange(variables.Select(v => $"(with {v.Key}={v.Value})"));

        environment.OnStepStarting?.Invoke(step, display);

        foreach (var directory in step.CleanDirectories)
        {
            environment.DeleteDirectory(Path.Combine(environment.RepoRoot, directory));
        }

        var saved = step.RestoreFiles
            .Select(file => Path.Combine(environment.RepoRoot, file))
            .Select(path => (Path: path, Content: environment.ReadFile(path)))
            .ToList();
        try
        {
            return RunCommands(step, options, environment, note, files, variables);
        }
        finally
        {
            foreach (var (path, content) in saved)
            {
                environment.RestoreFile(path, content);
            }
        }
    }

    private static CiStepResult RunCommands(
        CiStep step,
        LocalCiOptions options,
        LocalCiEnvironment environment,
        string note,
        IReadOnlyList<string> files,
        IReadOnlyDictionary<string, string> variables)
    {
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < step.Commands.Count; i++)
        {
            var command = step.Commands[i];
            var extra = BuildExtraArguments(step, i, options, files);
            int exitCode = environment.Run(command, extra, variables);
            bool succeeded = command.AllowedExitCodes is { Count: > 0 }
                ? command.AllowedExitCodes.Contains(exitCode)
                : exitCode == 0;
            if (!succeeded)
            {
                return new(step, CiOutcome.Failed, watch.Elapsed, Join(note, $"'{command.Display}' exited {exitCode}"));
            }
        }

        if (step.InProcess is not null)
        {
            int exitCode = step.InProcess(environment.RepoRoot, options);
            if (exitCode != 0)
            {
                return new(step, CiOutcome.Failed, watch.Elapsed, Join(note, $"exited {exitCode}"));
            }
        }

        return new(step, CiOutcome.Passed, watch.Elapsed, note);
    }

    private static List<string> BuildExtraArguments(
        CiStep step,
        int commandIndex,
        LocalCiOptions options,
        IReadOnlyList<string> files)
    {
        var command = step.Commands[commandIndex];
        var extra = new List<string>();
        if (command.ForwardsBaseRef && !string.IsNullOrWhiteSpace(options.BaseRef))
        {
            extra.Add("--base");
            extra.Add(options.BaseRef);
        }

        if (commandIndex == step.Commands.Count - 1 && files.Count > 0)
        {
            extra.AddRange(files);
        }

        return extra;
    }

    private static string Join(string first, string second) => first.Length == 0 ? second : $"{first} {second}";
}
