using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Compliance;

namespace Cadence.Cli.Services;

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
        var activeOptions = options;

        if (environment.PrepareWorkspace is not null)
        {
            var preflightWatch = Stopwatch.StartNew();
            var preflightStep = new CiStep("merge-readiness", "Prepare merged worktree");
            string modeNote = options.Offline ? $"--offline --base {options.BaseRef}" : $"git fetch + git merge-tree --write-tree ({options.BaseRef})";
            if (options.IncludeUncommitted)
            {
                modeNote += " --include-uncommitted";
            }

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
            if (!string.IsNullOrWhiteSpace(prepared.EffectiveBaseRef))
            {
                activeOptions = options with { BaseRef = prepared.EffectiveBaseRef };
            }
        }

        using (workspaceLease)
        {
            return RunJobsInWorkspace(jobs, activeOptions, activeEnvironment, results);
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
