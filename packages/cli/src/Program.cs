using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;

using Spectre.Console;

namespace Cadence.Cli;

/// <summary>
/// Entry point of the <c>cadence</c> developer CLI. Commands are parsed with System.CommandLine, which is trim- and
/// Native AOT-safe; Spectre.Console is used only to render output. Domain command registrars under
/// <c>Cadence.Cli.Commands</c> populate <see cref="BuildRootCommand"/>, and the <c>cli-docs</c> gate inspects
/// <see cref="GetCommandNames"/> directly to verify that <c>docs/cli.md</c> documents every command and subcommand.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        // Without a terminal (a pipe, a log file, a git hook, an agent shell) Spectre can measure the console as -1
        // columns and drop every markup line, so pick a plain console first (#5417).
        CliConsole.ConfigureForCurrentProcess();

        // Hooks talk to an agent runtime over standard streams, so they print no banner.
        if (args is not ["hook", ..])
        {
            WriteBanner();
        }

        return BuildRootCommand().Parse(args).Invoke();
    }

    private static void WriteBanner()
    {
        AnsiConsole.Write(
            new FigletText("Cadence")
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[bold cyan]Cadence Clinical Research Software Platform[/] [grey](.NET 10 / C# 14)[/]\n");
    }

    /// <summary>Builds the full command tree; exposed so tests and docs generators can inspect commands without running them.</summary>
    public static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("Cadence developer CLI: quality gates, tests, documentation generators and wizards");
        CoreWorkflowCommands.Register(root);
        ValidationAndSecurityCommands.Register(root);
        DocumentationAndContractCommands.Register(root);
        ComplianceAndGxpCommands.Register(root);
        WorkspaceAndHookCommands.Register(root);
        return root;
    }

    /// <summary>Returns all distinct command and branch names registered under <see cref="BuildRootCommand"/>.</summary>
    public static IReadOnlyList<string> GetCommandNames()
    {
        var names = new List<string>();
        CollectSubcommandNames(BuildRootCommand(), names);
        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void CollectSubcommandNames(Command parent, List<string> names)
    {
        foreach (Command subcommand in parent.Subcommands)
        {
            names.Add(subcommand.Name);
            CollectSubcommandNames(subcommand, names);
        }
    }
}
