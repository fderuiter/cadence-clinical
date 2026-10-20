using System;
using System.IO;

using Cadence.ToolingCore.Services;

using Spectre.Console;

namespace Cadence.Cli.Services;

/// <summary>Runs the gates of <see cref="QualityGates"/> for <c>cadence check</c>.</summary>
public static class QualityGateOrchestrator
{
    public static int RunQualityGates(string? gateName = null, string[]? targetFiles = null, bool preCommit = false)
    {
        AnsiConsole.MarkupLine("[bold cyan]Executing Cadence Quality Sentinels & Policy Gates...[/]\n");

        string repoRoot = FindRepoRoot();
        if (preCommit && (targetFiles == null || targetFiles.Length == 0))
        {
            var staged = PreCommitOpenApiService.GetStagedFiles(repoRoot);
            if (staged.Count > 0)
            {
                targetFiles = staged.ToArray();
            }
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

        return RunAll(context);
    }

    private static int RunAll(GateContext context)
    {
        int total = QualityGates.All.Count;
        int failed = 0;
        for (int i = 0; i < total; i++)
        {
            var gate = QualityGates.All[i];
            AnsiConsole.MarkupLine($"\n[bold]{i + 1}/{total} {Markup.Escape(gate.Name)}[/]: {Markup.Escape(gate.Checks)}");
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
            AnsiConsole.MarkupLine($"\n[bold red]{failed} of {total} quality gates failed.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"\n[bold green]All {total} quality gates passed.[/]");
        return 0;
    }

    public static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
