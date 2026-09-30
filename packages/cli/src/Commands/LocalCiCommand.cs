using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

using Cadence.Cli.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

/// <summary>
/// <c>cadence ci</c>: runs the jobs of <c>.github/workflows/ci.yml</c> on this machine (#5017), because GitHub Actions
/// does not run jobs for this repository until 2027.
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
        var allowSkip = new Option<bool>("--allow-skip")
        {
            Description = "Run the .NET tests without PostgreSQL, so the database tests report as skipped instead of failing the run",
        };
        var baseRef = new Option<string>("--base")
        {
            Description = "Ref that changed files are compared with (CI uses origin/main)",
            DefaultValueFactory = _ => "origin/main",
        };
        var list = new Option<bool>("--list") { Description = "Print the steps and what each runs locally, without running them" };

        command.Options.Add(jobs);
        command.Options.Add(allowSkip);
        command.Options.Add(baseRef);
        command.Options.Add(list);
        command.SetAction(result => Execute(
            result.GetValue(jobs) ?? [],
            result.GetValue(allowSkip),
            result.GetValue(baseRef) ?? "origin/main",
            result.GetValue(list)));
    }

    private static int Execute(string[] jobKeys, bool allowSkip, string baseRef, bool list)
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

        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        var environment = LocalCiEnvironment.Create(repoRoot, (step, commands) =>
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold cyan]==> {Markup.Escape(step.Job)} / {Markup.Escape(step.Name)}[/]");
            foreach (var line in commands)
            {
                AnsiConsole.MarkupLine($"[grey]$ {Markup.Escape(line)}[/]");
            }
        });

        var results = LocalCiRunner.Run(jobs, new LocalCiOptions(allowSkip, baseRef), environment);
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
        if (step.RequiredTool is not null) parts.Add($"(when {step.RequiredTool} is on PATH)");
        if (step.CoveredBy is not null) parts.Add($"(covered when '{step.CoveredBy}' passed)");
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
