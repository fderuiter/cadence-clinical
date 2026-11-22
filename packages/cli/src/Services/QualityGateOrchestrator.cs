using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Services;

/// <summary>Runs the gates of <see cref="QualityGates"/> for <c>cadence check</c>.</summary>
public static class QualityGateOrchestrator
{
    public static int RunQualityGates(
        string? gateName = null,
        string[]? targetFiles = null,
        bool preCommit = false,
        string[]? excludedGates = null,
        string? repoRoot = null)
    {
        AnsiConsole.MarkupLine("[bold cyan]Executing Cadence Quality Sentinels & Policy Gates...[/]\n");

        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (excludedGates is { Length: > 0 })
        {
            if (!string.IsNullOrEmpty(gateName))
            {
                AnsiConsole.MarkupLine("[red]--exclude applies only when running all quality gates (omit the gate argument).[/]");
                return 1;
            }

            foreach (string raw in excludedGates.SelectMany(g => g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            {
                var found = QualityGates.Find(raw);
                if (found is null)
                {
                    AnsiConsole.MarkupLine($"[red]Unknown quality gate '{Markup.Escape(raw)}'. Gates: {Markup.Escape(QualityGates.NameList)}.[/]");
                    return 1;
                }

                excluded.Add(found.Name);
            }
        }

        repoRoot ??= FindRepoRoot();
        if (preCommit && targetFiles is null)
        {
            targetFiles = [.. PreCommitOpenApiService.GetStagedFiles(repoRoot)];
        }

        var context = new GateContext(repoRoot, targetFiles, preCommit);

        if (!string.IsNullOrEmpty(gateName))
        {
            var gate = QualityGates.Find(gateName);
            if (gate is null)
            {
                AnsiConsole.MarkupLine($"[red]Unknown quality gate '{Markup.Escape(gateName)}'. Gates: {Markup.Escape(QualityGates.NameList)}.[/]");
                return 1;
            }

            return gate.Run(context) ? 0 : 1;
        }

        return RunAll(context, excluded);
    }

    private static int RunAll(GateContext context, IReadOnlySet<string> excluded)
    {
        int total = QualityGates.All.Count;
        int failed = 0;
        for (int i = 0; i < total; i++)
        {
            var gate = QualityGates.All[i];
            AnsiConsole.MarkupLine($"\n[bold]{i + 1}/{total} {Markup.Escape(gate.Name)}[/]: {Markup.Escape(gate.Checks)}");
            if (excluded.Contains(gate.Name))
            {
                AnsiConsole.MarkupLine("Skipped: excluded via --exclude.");
                continue;
            }

            if (context.PreCommit && gate.PreCommit == PreCommitMode.Skipped)
            {
                AnsiConsole.MarkupLine($"Skipped: {Markup.Escape(gate.PreCommitNote ?? "")}.");
                continue;
            }

            if (!gate.Run(context))
            {
                AnsiConsole.MarkupLine($"[red]Gate {i + 1} ({Markup.Escape(gate.Name)}) failed![/]");
                failed++;
            }
        }

        if (failed > 0)
        {
            string suffix = excluded.Count > 0 ? $" ({excluded.Count} excluded)" : "";
            AnsiConsole.MarkupLine($"\n[bold red]{failed} of {total - excluded.Count} quality gates failed{suffix}.[/]");
            return 1;
        }

        if (excluded.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n[bold green]All {total - excluded.Count} non-excluded quality gates passed ({excluded.Count} excluded).[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"\n[bold green]All {total} quality gates passed.[/]");
        }

        return 0;
    }

    public static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
