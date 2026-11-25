using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;

using Cadence.Cli.Services;
using Cadence.Compliance;
using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

/// <summary>
/// <c>cadence ci</c>: mirrors the pull-request jobs of <c>.github/workflows/ci.yml</c> locally (#5017) so contributors
/// can verify full workflow parity before pushing to GitHub Actions.
/// </summary>
public static class LocalCiCommand
{
    public static void Configure(Command command)
    {
        var jobs = new Option<string[]>("--job", "-j")
        {
            Description = $"Run only this job and the jobs it needs; repeat for more ({string.Join(", ", CiPlan.Jobs.Select(j => j.Key))})",
            AllowMultipleArgumentsPerToken = false,
        };
        var allowMissingTools = new Option<bool>("--allow-missing-tools")
        {
            Description = "Skip steps whose required host tool binary or Docker daemon is unavailable (PostgreSQL checks stay fail-closed)",
        };
        var allowDbSkip = new Option<bool>("--allow-db-skip")
        {
            Description = "Allow .NET tests and compliance reports to run without PostgreSQL and accept skipped database tests or missing *.database.txt evidence",
        };
        var allowSkip = new Option<bool>("--allow-skip")
        {
            Description = "Shorthand for --allow-missing-tools and --allow-db-skip",
        };
        var baseRef = new Option<string>("--base")
        {
            Description = "Ref that changed files, merged worktree synthesis, and CLI check steps are compared with (CI uses origin/<base_ref>)",
            DefaultValueFactory = _ => GitRevisionScopeService.DefaultBaseRef,
        };
        baseRef.Validators.Add(RejectOptionLikeValue);
        var offline = new Option<bool>("--offline")
        {
            Description = "Skip 'git fetch' before synthesizing the merged worktree (air-gapped debugging only)",
        };
        var includeUncommitted = new Option<bool>("--include-uncommitted")
        {
            Description = "Synthesize an ephemeral snapshot commit from uncommitted working-tree and index changes without mutating your checkout",
        };
        var attest = new Option<bool>("--attest")
        {
            Description = $"Publish a '{RulesetService.LocalCiStatusContext}' success commit status on caller HEAD via 'gh api' after all {CiPlan.Jobs.Count} online CI jobs pass on a clean checkout",
        };
        var list = new Option<bool>("--list") { Description = "Print the steps and what each runs locally, without running them" };

        command.Options.Add(jobs);
        command.Options.Add(allowMissingTools);
        command.Options.Add(allowDbSkip);
        command.Options.Add(allowSkip);
        command.Options.Add(baseRef);
        command.Options.Add(offline);
        command.Options.Add(includeUncommitted);
        command.Options.Add(attest);
        command.Options.Add(list);
        command.Validators.Add(commandResult =>
        {
            bool hasAttest = IsFlagEnabled(commandResult, attest);
            if (!hasAttest)
            {
                return;
            }

            bool hasJobFilter = commandResult.GetResult(jobs) is { Implicit: false, Tokens.Count: > 0 };
            if (IsFlagEnabled(commandResult, offline)
                || IsFlagEnabled(commandResult, includeUncommitted)
                || IsFlagEnabled(commandResult, allowMissingTools)
                || IsFlagEnabled(commandResult, allowDbSkip)
                || IsFlagEnabled(commandResult, allowSkip)
                || IsFlagEnabled(commandResult, list)
                || hasJobFilter)
            {
                commandResult.AddError(
                    "Option '--attest' requires a clean, full online CI run and cannot be combined with '--offline', "
                    + "'--include-uncommitted', '--allow-missing-tools', '--allow-db-skip', '--allow-skip', '--job', or '--list'.");
            }
        });
        command.SetAction(result =>
        {
            bool skipAll = result.GetValue(allowSkip);
            return Execute(
                result.GetValue(jobs) ?? [],
                allowMissingTools: result.GetValue(allowMissingTools) || skipAll,
                allowDbSkip: result.GetValue(allowDbSkip) || skipAll,
                baseRef: result.GetValue(baseRef) ?? GitRevisionScopeService.DefaultBaseRef,
                list: result.GetValue(list),
                offline: result.GetValue(offline),
                includeUncommitted: result.GetValue(includeUncommitted),
                attest: result.GetValue(attest));
        });
    }

    private static bool IsFlagEnabled(CommandResult commandResult, Option<bool> option) =>
        commandResult.GetResult(option) is { Implicit: false } optResult
        && optResult.Tokens.All(t => !string.Equals(t.Value, "false", StringComparison.OrdinalIgnoreCase));

    private static void RejectOptionLikeValue(OptionResult optionResult)
    {
        foreach (string value in optionResult.Tokens.Select(token => token.Value))
        {
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-'))
            {
                optionResult.AddError(
                    $"Option '{optionResult.Option.Name}' requires a value, got '{value}'.");
            }
        }
    }

    public static int Execute(
        string[] jobKeys,
        bool allowSkip,
        string baseRef,
        bool list,
        LocalCiEnvironment? environment = null,
        bool offline = false,
        bool includeUncommitted = false,
        bool attest = false) =>
        Execute(
            jobKeys,
            allowMissingTools: allowSkip,
            allowDbSkip: allowSkip,
            baseRef: baseRef,
            list: list,
            environment: environment,
            offline: offline,
            includeUncommitted: includeUncommitted,
            attest: attest);

    public static int Execute(
        string[] jobKeys,
        bool allowMissingTools,
        bool allowDbSkip,
        string baseRef,
        bool list,
        LocalCiEnvironment? environment = null,
        bool offline = false,
        bool includeUncommitted = false,
        bool attest = false)
    {
        if (attest && !ValidateAttestFlags(jobKeys, allowMissingTools, allowDbSkip, list, offline, includeUncommitted))
        {
            return 1;
        }

        IReadOnlyList<CiJob> jobs;
        try
        {
            jobs = CiPlan.Select(jobKeys);
        }
        catch (ArgumentException ex)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return 1;
        }

        if (list)
        {
            PrintPlan(jobs);
            return 0;
        }

        environment ??= LocalCiEnvironment.Create(QualityGateOrchestrator.FindRepoRoot(), (step, commands) =>
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold cyan]==> {Markup.Escape(step.Job)} / {Markup.Escape(step.Name)}[/]");
            foreach (var line in commands)
            {
                AnsiConsole.MarkupLine($"[grey]$ {Markup.Escape(line)}[/]");
            }
        });

        string? preRunHeadSha = null;
        string? preRunRepo = null;
        if (attest && !VerifyCleanAttestationTarget(environment, out preRunHeadSha, out preRunRepo))
        {
            return 1;
        }

        var results = LocalCiRunner.Run(
            jobs,
            new LocalCiOptions(
                allowMissingTools,
                allowDbSkip,
                baseRef,
                Offline: offline,
                RequestedJobs: jobKeys,
                IncludeUncommitted: includeUncommitted,
                Attest: attest),
            environment);
        PrintSummary(results, jobKeys.Length == 0);
        if (results.Any(r => r.Outcome is CiOutcome.Failed or CiOutcome.NotRun))
        {
            return 1;
        }

        if (!attest)
        {
            return 0;
        }

        if (jobs.Count != CiPlan.Jobs.Count)
        {
            AnsiConsole.MarkupLine($"[red]Refusing '--attest': expected all {CiPlan.Jobs.Count} CI jobs to execute, but ran {jobs.Count}.[/]");
            return 1;
        }

        return PublishAttestation(environment, preRunRepo, preRunHeadSha);
    }

    private static bool ValidateAttestFlags(
        string[] jobKeys,
        bool allowMissingTools,
        bool allowDbSkip,
        bool list,
        bool offline,
        bool includeUncommitted)
    {
        var disqualifiers = new List<string>();
        if (offline) disqualifiers.Add("--offline");
        if (includeUncommitted) disqualifiers.Add("--include-uncommitted");
        if (allowMissingTools) disqualifiers.Add("--allow-missing-tools");
        if (allowDbSkip) disqualifiers.Add("--allow-db-skip");
        if (jobKeys.Length > 0) disqualifiers.Add("--job");
        if (list) disqualifiers.Add("--list");

        if (disqualifiers.Count == 0)
        {
            return true;
        }

        AnsiConsole.MarkupLine(
            $"[red]Refusing '--attest': cannot combine with {Markup.Escape(string.Join(", ", disqualifiers))}. "
            + $"Run all {CiPlan.Jobs.Count} CI jobs online on a clean checkout with no skips.[/]");
        return false;
    }

    private static bool VerifyCleanAttestationTarget(
        LocalCiEnvironment environment,
        out string? headSha,
        out string? repository)
    {
        headSha = null;
        repository = null;

        var dirtyPaths = environment.GetWorkingTreeChanges?.Invoke() ?? [];
        if (dirtyPaths.Count > 0)
        {
            AnsiConsole.MarkupLine(
                $"[red]Refusing '--attest': checkout has {dirtyPaths.Count} uncommitted file(s). Commit or stash changes before attesting.[/]");
            return false;
        }

        headSha = environment.ResolveAttestationHeadSha?.Invoke();
        if (string.IsNullOrWhiteSpace(headSha) || headSha.Length != 40 || !headSha.All(Uri.IsHexDigit))
        {
            AnsiConsole.MarkupLine("[red]Refusing '--attest': could not resolve a 40-character caller HEAD commit SHA.[/]");
            return false;
        }

        repository = environment.ResolveAttestationRepository?.Invoke();
        if (string.IsNullOrWhiteSpace(repository))
        {
            AnsiConsole.MarkupLine("[red]Refusing '--attest': could not resolve GitHub repository slug ('owner/repo') from GITHUB_REPOSITORY or git remote 'origin'.[/]");
            return false;
        }

        return true;
    }

    private static int PublishAttestation(LocalCiEnvironment environment, string? preRunRepo, string? preRunHeadSha)
    {
        if (!VerifyCleanAttestationTarget(environment, out string? postRunHeadSha, out string? postRunRepo))
        {
            return 1;
        }

        if (!string.Equals(preRunHeadSha, postRunHeadSha, StringComparison.Ordinal))
        {
            AnsiConsole.MarkupLine("[red]Refusing '--attest': caller HEAD changed while local CI was running.[/]");
            return 1;
        }

        string repo = postRunRepo ?? preRunRepo!;
        string sha = postRunHeadSha!;
        if (environment.PublishCommitStatus is null)
        {
            AnsiConsole.MarkupLine("[red]Refusing '--attest': no commit status publisher is configured on LocalCiEnvironment.[/]");
            return 1;
        }

        string description = $"Verified all {CiPlan.Jobs.Count} local CI jobs in merged worktree";
        var outcome = environment.PublishCommitStatus(repo, sha, RulesetService.LocalCiStatusContext, description);
        if (outcome.ExitCode != 0)
        {
            string detail = !string.IsNullOrWhiteSpace(outcome.Stderr) ? outcome.Stderr.Trim() : outcome.Stdout.Trim();
            AnsiConsole.MarkupLine(
                $"[red]Failed to publish '{Markup.Escape(RulesetService.LocalCiStatusContext)}' commit status on {Markup.Escape(repo)}@{Markup.Escape(sha)} "
                + $"(exit code {outcome.ExitCode}): {Markup.Escape(detail)}[/]");
            return 1;
        }

        string shortSha = sha[..12];
        AnsiConsole.MarkupLine(
            $"[bold green]Attested '{Markup.Escape(RulesetService.LocalCiStatusContext)}' (state=success) on {Markup.Escape(repo)}@{Markup.Escape(shortSha)}.[/]");
        return 0;
    }

    // Plain lines rather than a Spectre table: agent sessions and redirected output report a console a few characters
    // wide, which collapses a table to an ellipsis.
    private static void PrintPlan(IReadOnlyList<CiJob> jobs)
    {
        foreach (var job in jobs)
        {
            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(job.Key)}[/] ({Markup.Escape(job.Name)})");
            foreach (var step in job.Steps)
            {
                AnsiConsole.MarkupLine($"  {Markup.Escape(step.Name)}");
                foreach (var line in Describe(step))
                {
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(line)}[/]");
                }
            }
        }

        PrintNotRunJobs(jobs);
    }

    private static List<string> Describe(CiStep step)
    {
        if (step.NotRunLocally is not null) return [$"skipped: {step.NotRunLocally}"];

        var parts = step.Commands.Select(c => c.Display).ToList();
        if (step.InProcessDisplay is not null) parts.Add(step.InProcessDisplay);
        if (step.Files != CiFileSource.None) parts.Add($"(with {step.Files} files)");
        if (step.RequiredTool is not null) parts.Add($"(requires {step.RequiredTool} on PATH; skipped with --allow-missing-tools)");
        if (step.NeedsDocker) parts.Add("(requires a running Docker daemon; skipped with --allow-missing-tools)");
        if (step.Job == CiPlan.ContainerSecurityJobKey) parts.Add("(runs on container/dependency changes against --base, on tracked-files fallback, or with --job container-security)");
        if (step.NeedsPostgres && !step.ScopedDatabaseTest) parts.Add($"(verifies PostgreSQL tests and *.database.txt evidence in {RtmGeneratorService.DefaultResultsDir}; skipped with --allow-db-skip)");
        if (step.CoveredBy is not null) parts.Add($"(covered when '{step.CoveredBy}' passed)");
        parts.AddRange(step.Environment.Select(e => $"(with {e.Key}={e.Value})"));
        parts.AddRange(step.EnvironmentIfPassed.Select(e => $"(with {e.Name}={e.Value} when '{e.StepKey}' passed)"));
        if (step.RestoreFiles.Count > 0) parts.Add($"(then restores {string.Join(", ", step.RestoreFiles)})");
        return parts;
    }

    private static void PrintSummary(IReadOnlyList<CiStepResult> results, bool allJobs)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Local CI summary[/]");
        foreach (var result in results)
        {
            string outcome = result.Outcome switch
            {
                CiOutcome.Passed => "[green]passed [/]",
                CiOutcome.Failed => "[red]FAILED [/]",
                CiOutcome.Covered => "[green]covered[/]",
                CiOutcome.Skipped => "[yellow]skipped[/]",
                _ => "[grey]not run[/]",
            };
            string time = result.Duration == TimeSpan.Zero ? "" : $" ({result.Duration.TotalSeconds:0}s)";
            string note = result.Note.Length == 0 ? "" : $": {result.Note}";
            AnsiConsole.MarkupLine($"  {outcome} {Markup.Escape($"{result.Step.Job} / {result.Step.Name}{time}{note}")}");
        }

        if (allJobs)
        {
            PrintNotRunJobs(CiPlan.Jobs);
        }

        var counts = results.GroupBy(r => r.Outcome).ToDictionary(g => g.Key, g => g.Count());
        string Count(CiOutcome outcome) => counts.TryGetValue(outcome, out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0";
        bool failed = counts.ContainsKey(CiOutcome.Failed);
        AnsiConsole.MarkupLine(
            $"\n{(failed ? "[bold red]Local CI failed[/]" : "[bold green]Local CI passed[/]")}: {Count(CiOutcome.Passed)} passed, "
            + $"{Count(CiOutcome.Covered)} covered, {Count(CiOutcome.Failed)} failed, {Count(CiOutcome.Skipped)} skipped, {Count(CiOutcome.NotRun)} not run.");
    }

    private static void PrintNotRunJobs(IReadOnlyList<CiJob> shown)
    {
        foreach (var job in CiPlan.Jobs.Where(j => j.OnRequestOnly is not null && shown.All(s => s.Key != j.Key)))
        {
            AnsiConsole.MarkupLine($"[grey]Job '{Markup.Escape(job.Key)}' not run: {Markup.Escape(job.OnRequestOnly!)}.[/]");
        }

        foreach (var (key, why) in CiPlan.GitHubOnlyJobs)
        {
            AnsiConsole.MarkupLine($"[grey]Job '{Markup.Escape(key)}' has no local equivalent: it {Markup.Escape(why)}.[/]");
        }
    }
}
