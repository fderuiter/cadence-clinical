using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;

using Cadence.Cli.Services;
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
            Description = "Skip steps whose required host tool binary is missing on PATH (PostgreSQL checks stay fail-closed)",
        };
        var allowDbSkip = new Option<bool>("--allow-db-skip")
        {
            Description = "Allow .NET tests to run without PostgreSQL and accept skipped database tests in TRX results",
        };
        var allowSkip = new Option<bool>("--allow-skip")
        {
            Description = "Shorthand for --allow-missing-tools and --allow-db-skip",
        };
        var baseRef = new Option<string>("--base")
        {
            Description = "Ref that changed files and CLI check steps are compared with (CI uses origin/<base_ref>)",
            DefaultValueFactory = _ => GitRevisionScopeService.DefaultBaseRef,
        };
        baseRef.Validators.Add(RejectOptionLikeValue);
        var list = new Option<bool>("--list") { Description = "Print the steps and what each runs locally, without running them" };

        command.Options.Add(jobs);
        command.Options.Add(allowMissingTools);
        command.Options.Add(allowDbSkip);
        command.Options.Add(allowSkip);
        command.Options.Add(baseRef);
        command.Options.Add(list);
        command.SetAction(result =>
        {
            bool skipAll = result.GetValue(allowSkip);
            return Execute(
                result.GetValue(jobs) ?? [],
                allowMissingTools: result.GetValue(allowMissingTools) || skipAll,
                allowDbSkip: result.GetValue(allowDbSkip) || skipAll,
                baseRef: result.GetValue(baseRef) ?? GitRevisionScopeService.DefaultBaseRef,
                list: result.GetValue(list));
        });
    }

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
        LocalCiEnvironment? environment = null) =>
        Execute(jobKeys, allowMissingTools: allowSkip, allowDbSkip: allowSkip, baseRef: baseRef, list: list, environment: environment);

    public static int Execute(
        string[] jobKeys,
        bool allowMissingTools,
        bool allowDbSkip,
        string baseRef,
        bool list,
        LocalCiEnvironment? environment = null)
    {
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

        var results = LocalCiRunner.Run(jobs, new LocalCiOptions(allowMissingTools, allowDbSkip, baseRef), environment);
        PrintSummary(results, jobKeys.Length == 0);
        return results.Any(r => r.Outcome == CiOutcome.Failed) ? 1 : 0;
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
