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

/// <summary>Whether container build and scan steps can reach a running Docker daemon.</summary>
/// <param name="Available">True when <c>docker info</c> succeeds.</param>
/// <param name="Message">What was found, for the summary.</param>
public sealed record CiDockerStatus(bool Available, string Message);

/// <summary>Whether <c>container-security</c> runs on a default <c>cadence ci</c> invocation.</summary>
/// <param name="ShouldRun">True when container/dependency inputs changed or the scope fell back to all tracked files.</param>
/// <param name="Note">Why <c>container-security</c> was skipped when <paramref name="ShouldRun"/> is false.</param>
public sealed record CiContainerScanScope(bool ShouldRun, string Note);

/// <summary>The choices given on the command line.</summary>
/// <param name="AllowMissingTools">Skip steps whose required host tool binary or Docker daemon is unavailable.</param>
/// <param name="AllowDbSkip">Allow .NET test and compliance steps to run without PostgreSQL or accept skipped database tests and missing <c>*.database.txt</c> evidence.</param>
/// <param name="BaseRef">The ref that "changed files" are compared with, as <c>origin/main</c> is in CI.</param>
/// <param name="RequestedJobs">Job keys explicitly passed with <c>--job</c> (empty or null on a full default run).</param>
public sealed record LocalCiOptions(
    bool AllowMissingTools,
    bool AllowDbSkip,
    string BaseRef,
    IReadOnlyCollection<string>? RequestedJobs = null)
{
    public LocalCiOptions(bool allowSkip, string baseRef, IReadOnlyCollection<string>? requestedJobs = null)
        : this(AllowMissingTools: allowSkip, AllowDbSkip: allowSkip, BaseRef: baseRef, RequestedJobs: requestedJobs)
    {
    }
}

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

    /// <summary>The real environment: child processes, <c>PATH</c>, PostgreSQL, Docker and git in <paramref name="repoRoot"/>.</summary>
    public static LocalCiEnvironment Create(string repoRoot, Action<CiStep, IReadOnlyList<string>>? onStepStarting = null) => new()
    {
        RepoRoot = repoRoot,
        Run = (command, extra, variables) => RunProcess(repoRoot, command, extra, variables),
        IsToolOnPath = tool => IsToolAvailable(repoRoot, tool),
        ProbePostgres = ProbePostgresFromEnvironment,
        ProbeDocker = () => ProbeDockerFromEnvironment(repoRoot),
        ShouldRunContainerSecurity = baseRef => ResolveContainerSecurityScope(repoRoot, baseRef),
        FindFiles = (step, baseRef) => DiscoverFiles(repoRoot, step, baseRef),
        OnStepStarting = onStepStarting,
    };

    private static int RunProcess(string repoRoot, CiCommand command, IReadOnlyList<string> extra, IReadOnlyDictionary<string, string> variables)
    {
        string executable = ResolveVenvToolPath(repoRoot, command.FileName) ?? command.FileName;
        var psi = new ProcessStartInfo(executable) { WorkingDirectory = repoRoot, UseShellExecute = false };
        foreach (var argument in command.Arguments.Concat(extra))
        {
            psi.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in variables)
        {
            psi.Environment[name] = value;
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

    /// <summary>Whether <paramref name="tool"/> is in <c>.cadence/bin</c>, <c>.cadence/venv</c>, <c>.venv</c>, or a <c>PATH</c> directory.</summary>
    public static bool IsToolAvailable(string repoRoot, string tool) =>
        ResolveVenvToolPath(repoRoot, tool) is not null || IsOnPath(tool);

    private static string? ResolveVenvToolPath(string repoRoot, string tool)
    {
        string[] suffixes = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        string binSubdir = OperatingSystem.IsWindows() ? "Scripts" : "bin";
        string[] searchDirs =
        [
            Path.Combine(repoRoot, ".cadence", "bin"),
            Path.Combine(repoRoot, ".cadence", "venv", binSubdir),
            Path.Combine(repoRoot, ".venv", binSubdir),
        ];

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
            CiFileSource.ShellScripts => DiscoverShellScriptFiles(repoRoot),
            _ => [],
        };

        return [.. files];
    }

    private static IEnumerable<string> DiscoverShellScriptFiles(string repoRoot)
    {
        var found = new SortedSet<string>(
            FindUnderRoot(repoRoot, "*.sh",
                path => !path.Contains("/.venv/", StringComparison.Ordinal)
                    && !path.Contains("/.cadence/", StringComparison.Ordinal)
                    && !path.Contains("/node_modules/", StringComparison.Ordinal)),
            StringComparer.Ordinal);

        var tracked = GitRevisionScopeService.TryGetTrackedFiles(repoRoot);
        if (tracked is not null)
        {
            foreach (string rel in tracked)
            {
                string fileName = Path.GetFileName(rel);
                if (!fileName.Contains('.', StringComparison.Ordinal) &&
                    File.Exists(Path.Combine(repoRoot, rel)) &&
                    CliLinterGateService.IsShellScript(repoRoot, rel))
                {
                    found.Add("./" + rel);
                }
            }
        }
        else
        {
            foreach (string path in FindUnderRoot(repoRoot, "*",
                         p => !p.Contains("/.git/", StringComparison.Ordinal)
                             && !p.Contains("/.venv/", StringComparison.Ordinal)
                             && !p.Contains("/.cadence/", StringComparison.Ordinal)
                             && !p.Contains("/node_modules/", StringComparison.Ordinal)))
            {
                string rel = path[2..];
                string fileName = Path.GetFileName(rel);
                if (!fileName.Contains('.', StringComparison.Ordinal) &&
                    CliLinterGateService.IsShellScript(repoRoot, rel))
                {
                    found.Add(path);
                }
            }
        }

        return found;
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
        var byKey = new Dictionary<string, CiStepResult>(StringComparer.Ordinal);
        CiPostgresStatus? postgres = null;
        CiDockerStatus? docker = null;

        foreach (var job in jobs)
        {
            string? blocked = UnmetNeed(job, jobs, byKey);
            bool explicitlyRequested = options.RequestedJobs?.Contains(job.Key, StringComparer.Ordinal) is true;
            CiContainerScanScope? containerScope = job.Key == CiPlan.ContainerSecurityJobKey && !explicitlyRequested && blocked is null
                ? environment.ShouldRunContainerSecurity(options.BaseRef)
                : null;
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
                else if (containerScope is { ShouldRun: false })
                {
                    result = new(step, CiOutcome.Skipped, TimeSpan.Zero, containerScope.Note);
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
                else if (step.NeedsDocker && !environment.IsToolOnPath("docker"))
                {
                    const string dockerHint = "install Docker and start the Docker daemon (docs/LOCAL_DEV_ENVIRONMENT.md)";
                    result = options.AllowMissingTools
                        ? new(step, CiOutcome.Skipped, TimeSpan.Zero, $"docker is not on PATH; {dockerHint}")
                        : new(step, CiOutcome.Failed, TimeSpan.Zero, $"docker is not on PATH; {dockerHint} (or pass --allow-missing-tools to skip)");
                }
                else
                {
                    string note = "";
                    if (step.NeedsDocker)
                    {
                        docker ??= environment.ProbeDocker();
                        if (!docker.Available)
                        {
                            result = options.AllowMissingTools
                                ? new(step, CiOutcome.Skipped, TimeSpan.Zero, $"Docker daemon unavailable (--allow-missing-tools): {docker.Message}")
                                : new(step, CiOutcome.Failed, TimeSpan.Zero,
                                    $"{docker.Message} Start the Docker daemon (docs/LOCAL_DEV_ENVIRONMENT.md), or pass --allow-missing-tools to skip.");
                            Record(result);
                            continue;
                        }
                    }

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
                        result = RejectSkippedDatabaseTests(step, result, environment);
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
            if (CiPlan.GitHubOnlyJobs.ContainsKey(need))
            {
                continue;
            }

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
    /// Fails a passing test step whose results show database tests that did not execute or omitted <c>*.database.txt</c>
    /// fixture evidence: a green run must have exercised them (#5325). Opt-in tests are exempt
    /// (<see cref="DatabaseTestEvidence.OptInTests"/>), and full-repository discovery is scoped away from
    /// <see cref="CiStep.ScopedDatabaseTest"/> steps so standalone browser E2E runs cleanly.
    /// </summary>
    private static CiStepResult RejectSkippedDatabaseTests(CiStep step, CiStepResult result, LocalCiEnvironment environment)
    {
        string resultsDir = Path.Combine(environment.RepoRoot, RtmGeneratorService.DefaultResultsDir);
        string? discoveryRoot = step.ScopedDatabaseTest ? null : environment.RepoRoot;
        var skipped = environment.FindSkippedDatabaseTests(resultsDir, discoveryRoot);
        if (skipped.Count > 0)
        {
            string examples = string.Join(", ", skipped.Take(3).Select(t => t.FullName));
            return result with
            {
                Outcome = CiOutcome.Failed,
                Note = Join(result.Note, $"{skipped.Count} database test(s) did not execute ({examples}{(skipped.Count > 3 ? ", ..." : "")}); "
                    + "pass --allow-db-skip to accept a run without them."),
            };
        }

        if (!step.ScopedDatabaseTest && !environment.HasRecordedDatabaseEnvironment(resultsDir))
        {
            return result with
            {
                Outcome = CiOutcome.Failed,
                Note = Join(result.Note, $"No PostgreSQL fixture evidence (*.database.txt) found under {RtmGeneratorService.DefaultResultsDir} "
                    + $"for all {DatabaseTestEvidence.DatabaseAssemblies.Count} database assemblies; pass --allow-db-skip to accept a run without them."),
            };
        }

        return result;
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
