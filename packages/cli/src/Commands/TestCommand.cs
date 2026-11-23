using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Cadence.Cli.Services;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class TestCommand
{
    public const string SolutionTarget = "Cadence.sln";
    public const string IntegrationTarget = "tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj";

    public static IReadOnlyList<string> FastTargets { get; } =
    [
        "tests/Cadence.UnitTests/Cadence.UnitTests.csproj",
        "tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj",
    ];

    public static void Configure(Command command)
    {
        var fast = new Option<bool>("--fast", "-f")
        {
            Description = "Run unit and architecture tests only (tests/Cadence.UnitTests and tests/Cadence.ArchitectureTests).",
        };
        var integration = new Option<bool>("--integration", "-i")
        {
            Description = "Run only the PostgreSQL integration tests (tests/Cadence.IntegrationTests).",
        };
        var project = new Option<string[]>("--project", "-p")
        {
            Description = "Run specific test project or directory path (repeatable; overrides --fast and --integration).",
            AllowMultipleArgumentsPerToken = false,
        };
        var filter = new Option<string?>("--filter")
        {
            Description = "Forward a VSTest/xUnit filter expression to dotnet test.",
        };
        var noBuild = new Option<bool>("--no-build")
        {
            Description = "Skip building the target project(s) before running dotnet test.",
        };

        project.Validators.Add(CheckCommand.RejectOptionLikeTokens);
        filter.Validators.Add(CheckCommand.RejectOptionLikeTokens);

        command.Options.Add(fast);
        command.Options.Add(integration);
        command.Options.Add(project);
        command.Options.Add(filter);
        command.Options.Add(noBuild);
        command.SetAction(result => Execute(
            result.GetValue(fast),
            result.GetValue(integration),
            result.GetValue(project),
            result.GetValue(filter),
            result.GetValue(noBuild)));
    }

    public static IReadOnlyList<string> ResolveTargets(
        bool fast,
        bool integration,
        IReadOnlyList<string>? projects = null)
    {
        string[] explicitProjects = projects?
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray() ?? [];
        if (explicitProjects.Length > 0)
        {
            return explicitProjects;
        }

        if (fast)
        {
            return FastTargets;
        }

        return integration ? [IntegrationTarget] : [SolutionTarget];
    }

    public static IReadOnlyList<string> BuildDotnetTestArguments(string targetPath, string? filter, bool noBuild)
    {
        var args = new List<string> { "test", targetPath };
        if (noBuild)
        {
            args.Add("--no-build");
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            args.Add("--filter");
            args.Add(filter);
        }

        return args;
    }

    private static int Execute(bool fast, bool integration, string[]? projects, string? filter, bool noBuild)
    {
        IReadOnlyList<string> targets = ResolveTargets(fast, integration, projects);
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();

        foreach (string target in targets)
        {
            string targetPath = Path.IsPathRooted(target) ? target : Path.Combine(repoRoot, target);
            IReadOnlyList<string> args = BuildDotnetTestArguments(targetPath, filter, noBuild);
            string summary = string.Join(' ', new[] { target }
                .Concat(noBuild ? ["--no-build"] : Array.Empty<string>())
                .Concat(!string.IsNullOrWhiteSpace(filter) ? [$"--filter \"{filter}\""] : Array.Empty<string>()));
            AnsiConsole.MarkupLine($"[cyan]Executing .NET tests: {Markup.Escape(summary)}...[/]\n");

            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
            };
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return 1;
            }

            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                return proc.ExitCode;
            }
        }

        return 0;
    }
}
