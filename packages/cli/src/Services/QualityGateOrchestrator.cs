using System;
using System.Diagnostics;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class QualityGateOrchestrator
{
    public static int RunQualityGates()
    {
        AnsiConsole.MarkupLine("[bold cyan]Executing Cadence Architecture Sentinels & Quality Gates...[/]\n");

        // 1. C# Code Formatting
        AnsiConsole.MarkupLine("  1. Validating C# Code Formatting (.editorconfig)...");
        int formatExit = RunProcess("dotnet", "format Cadence.sln --verify-no-changes");
        if (formatExit != 0)
        {
            AnsiConsole.MarkupLine("  [red]Formatting check failed! Run 'dotnet format Cadence.sln' to auto-fix.[/]");
            return formatExit;
        }
        AnsiConsole.MarkupLine("  [green][[OK]] Code style compliant.[/]");

        // 2. ADR Validation
        AnsiConsole.MarkupLine("\n  2. Validating Architectural Decision Records (ADRs)...");
        bool adrOk = AdrValidatorService.ValidateAdrs(checkGitDiff: true);
        if (!adrOk)
        {
            AnsiConsole.MarkupLine("  [red]ADR validation failed! Check docs/adr/ and index.md.[/]");
            return 1;
        }

        // 3. Clean Architecture Sentinels
        AnsiConsole.MarkupLine("\n  3. Executing Clean Architecture Sentinel Unit Tests...");
        int archExit = RunProcess("dotnet", "test tests/Cadence.ArchitectureTests.csproj --no-build");
        if (archExit != 0)
        {
            AnsiConsole.MarkupLine("  [red]Architecture boundary violations detected![/]");
            return archExit;
        }
        AnsiConsole.MarkupLine("  [green][[OK]] Clean Architecture boundaries preserved.[/]");

        // 4. Fast Unit Tests
        AnsiConsole.MarkupLine("\n  4. Executing Fast Unit Test Suite...");
        int unitExit = RunProcess("dotnet", "test tests/Cadence.UnitTests.csproj --no-build --filter \"Category!=Integration\"");
        if (unitExit != 0)
        {
            AnsiConsole.MarkupLine("  [red]Unit test failures detected![/]");
            return unitExit;
        }
        AnsiConsole.MarkupLine("  [green][[OK]] All unit tests passed.[/]");

        AnsiConsole.MarkupLine("\n[bold green]All quality sentinels passed.[/]");
        return 0;
    }

    public static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(current, "Cadence.sln")))
            {
                return current;
            }
            string? parent = System.IO.Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return System.IO.Directory.GetCurrentDirectory();
    }

    private static int RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = FindRepoRoot(),
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null) return 1;
        process.WaitForExit();
        return process.ExitCode;
    }
}
