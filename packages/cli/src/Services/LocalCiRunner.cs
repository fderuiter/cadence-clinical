using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;

using Npgsql;

namespace Cadence.Cli.Services;

/// <summary>What happened to one step of a local CI run.</summary>
public enum CiOutcome
{
    /// <summary>Every command exited 0.</summary>
    Passed,

    /// <summary>A command exited non-zero, or the step could not start (for example, no PostgreSQL).</summary>
    Failed,

    /// <summary>The step was not run: a tool is missing or the step only runs on GitHub.</summary>
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
/// <param name="AllowSkip">Run the .NET tests without PostgreSQL, so its tests report as skipped.</param>
/// <param name="BaseRef">The ref that "changed files" are compared with, as <c>origin/main</c> is in CI.</param>
public sealed record LocalCiOptions(bool AllowSkip, string BaseRef);

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
    /// a PostgreSQL step passes, unless <c>--allow-skip</c> was given.
    /// </summary>
    public Func<string, IReadOnlyList<SkippedDatabaseTest>> FindSkippedDatabaseTests { get; init; } = DatabaseTestEvidence.FindSkipped;

    /// <summary>Deletes a directory under the repository root, if it exists.</summary>
    public Action<string> DeleteDirectory { get; init; } = path =>
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    };

    /// <summary>The real environment: child processes, <c>PATH</c>, PostgreSQL and git in <paramref name="repoRoot"/>.</summary>
    public static LocalCiEnvironment Create(string repoRoot, Action<CiStep, IReadOnlyList<string>>? onStepStarting = null) => new()
    {
        RepoRoot = repoRoot,
        Run = (command, extra, variables) => RunProcess(repoRoot, command, extra, variables),
        IsToolOnPath = IsOnPath,
        ProbePostgres = ProbePostgresFromEnvironment,
        FindFiles = (step, baseRef) => DiscoverFiles(repoRoot, step, baseRef),
        OnStepStarting = onStepStarting,
    };

    private static int RunProcess(string repoRoot, CiCommand command, IReadOnlyList<string> extra, IReadOnlyDictionary<string, string> variables)
    {
        var psi = new ProcessStartInfo(command.FileName) { WorkingDirectory = repoRoot, UseShellExecute = false };
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
            using var process = Process.Start(psi);
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

    /// <summary>Finds the files for <see cref="CiStep.Files"/>, the way the step's CI script does.</summary>
    public static IReadOnlyList<string> DiscoverFiles(string repoRoot, CiStep step, string baseRef)
    {
        IEnumerable<string> files = step.Files switch
        {
            CiFileSource.ChangedFiles => ChangedFiles(repoRoot, baseRef, acmrOnly: false),
            CiFileSource.ChangedFilesAcmr => ChangedFiles(repoRoot, baseRef, acmrOnly: true),
            CiFileSource.Dockerfiles => FindUnderRoot(repoRoot, "Dockerfile", _ => true),
            CiFileSource.ShellScripts => FindUnderRoot(repoRoot, "*.sh",
                path => !path.Contains("/.venv/", StringComparison.Ordinal) && !path.Contains("/node_modules/", StringComparison.Ordinal)),
            _ => [],
        };

        return [.. files.Where(path => CiPlan.MatchesFilePattern(step, path))];
    }

    private static IEnumerable<string> ChangedFiles(string repoRoot, string baseRef, bool acmrOnly)
    {
        string[] diff = acmrOnly
            ? ["diff", "--name-only", "--diff-filter=ACMR", $"{baseRef}...HEAD"]
            : ["diff", "--name-only", $"{baseRef}...HEAD"];
        var changed = Git(repoRoot, diff);
        if (changed.Count > 0 || acmrOnly)
        {
            return changed;
        }

        // CI falls back to the working tree ('git status --porcelain | awk {print $2}') when the diff is empty.
        return Git(repoRoot, ["status", "--porcelain"])
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length > 1)
            .Select(parts => parts[1]);
    }

    private static List<string> Git(string repoRoot, string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return [];
            var stderr = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = stderr.Result;
            return process.ExitCode == 0
                ? [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
                : [];
        }
        catch (Win32Exception)
        {
            return [];
        }
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
                    result = new(step, CiOutcome.Skipped, TimeSpan.Zero, $"{step.RequiredTool} is not on PATH; {step.ToolHint}");
                }
                else
                {
                    string note = "";
                    if (step.NeedsPostgres)
                    {
                        postgres ??= environment.ProbePostgres();
                        if (!postgres.Available && !options.AllowSkip)
                        {
                            result = new(step, CiOutcome.Failed, TimeSpan.Zero,
                                $"{postgres.Message} CI gives these tests a database; set CADENCE_TEST_POSTGRES=<connection string> "
                                + "or CADENCE_TEST_DOCKER=1 (docs/TESTING.md), or pass --allow-skip to run them as skipped.");
                            Record(result);
                            continue;
                        }

                        note = postgres.Available ? postgres.Message : $"PostgreSQL tests skipped (--allow-skip): {postgres.Message}";
                    }

                    result = RunStep(step, options, environment, note, byKey);
                    if (step.NeedsPostgres && result.Outcome == CiOutcome.Passed && !options.AllowSkip)
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
    /// exercised them (#5325). The opt-in benchmark is exempt (<see cref="DatabaseTestEvidence.OptInTests"/>).
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
                + "pass --allow-skip to accept a run without them."),
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
            .Select((c, i) => i == step.Commands.Count - 1 && files.Count > 0 ? $"{c.Display} <{files.Count} files>" : c.Display)
            .ToList();
        if (step.InProcess is not null)
        {
            display.Add(step.InProcessDisplay ?? step.Name);
        }

        var variables = step.EnvironmentIfPassed
            .Where(e => earlier.TryGetValue(e.StepKey, out var r) && r.Outcome == CiOutcome.Passed)
            .ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);
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
            return RunCommands(step, environment, note, files, variables);
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
        CiStep step, LocalCiEnvironment environment, string note, IReadOnlyList<string> files, IReadOnlyDictionary<string, string> variables)
    {
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < step.Commands.Count; i++)
        {
            var command = step.Commands[i];
            int exitCode = environment.Run(command, i == step.Commands.Count - 1 ? files : [], variables);
            if (exitCode != 0)
            {
                return new(step, CiOutcome.Failed, watch.Elapsed, Join(note, $"'{command.Display}' exited {exitCode}"));
            }
        }

        if (step.InProcess is not null)
        {
            int exitCode = step.InProcess(environment.RepoRoot);
            if (exitCode != 0)
            {
                return new(step, CiOutcome.Failed, watch.Elapsed, Join(note, $"exited {exitCode}"));
            }
        }

        return new(step, CiOutcome.Passed, watch.Elapsed, note);
    }

    private static string Join(string first, string second) => first.Length == 0 ? second : $"{first} {second}";
}
