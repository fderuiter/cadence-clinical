using System;
using System.Diagnostics;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        AnsiConsole.Write(
            new FigletText("Cadence")
                .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[bold cyan]Cadence Clinical Research Software Platform[/] [grey](.NET 10 / C# 14)[/]\n");

        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Usage:[/] [green]cadence[/] [cyan]<command>[/]");
            AnsiConsole.MarkupLine("\n[bold]Available Commands:[/] ");
            AnsiConsole.MarkupLine("  [cyan]check[/]                  Runs architecture sentinels and quality gates");
            AnsiConsole.MarkupLine("  [cyan]doctor[/]                 Validates system dependencies and local datastores");
            AnsiConsole.MarkupLine("  [cyan]gxp sync[/]               Executes test suites and regenerates RTM compliance docs");
            AnsiConsole.MarkupLine("  [cyan]test[/]                   Runs fast unit tests or testcontainer integration suites");
            AnsiConsole.MarkupLine("  [cyan]wizard onboarding[/]      Interactive full-stack (.NET/Node/Python) developer setup wizard");
            AnsiConsole.MarkupLine("  [cyan]wizard dotnet[/]          Interactive .NET 10-only onboarding wizard (SDK, tools, certs)");
            AnsiConsole.MarkupLine("  [cyan]wizard migrate[/]         Interactive microservice transcription & scaffolding wizard");
            AnsiConsole.MarkupLine("  [cyan]wizard decommission[/]    Interactive legacy Python decommissioning & cleanup wizard");
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "doctor":
                return RunDoctor();

            case "check":
                return RunCheck();

            case "test":
                return RunTest(args);

            case "gxp":
                string gxpSub = args.Length > 1 ? args[1].ToLowerInvariant() : "sync";
                return RunGxp(gxpSub);

            case "wizard":
                string subCommand = args.Length > 1 ? args[1].ToLowerInvariant() : "";
                string[] wizardArgs = args.Length > 2 ? args[2..] : [];
                return RunWizard(subCommand, wizardArgs);

            default:
                AnsiConsole.MarkupLine($"[red]Unknown command:[/] {command}");
                return 1;
        }
    }

    private static int RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);
        if (process == null) return 1;
        process.WaitForExit();
        return process.ExitCode;
    }

    private static int RunDoctor()
    {
        AnsiConsole.MarkupLine("[cyan]=== Cadence Clinical System Diagnostics ===[/]\n");

        // 1. .NET SDK
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            string output = p?.StandardOutput.ReadToEnd().Trim() ?? "";
            p?.WaitForExit();
            if (output.StartsWith("10."))
                AnsiConsole.MarkupLine($"  .NET 10 SDK: [green]Active ({output})[/]");
            else if (!string.IsNullOrEmpty(output))
                AnsiConsole.MarkupLine($"  .NET SDK: [yellow]Active ({output}) - .NET 10 preview recommended[/]");
            else
                AnsiConsole.MarkupLine("  .NET 10 SDK: [red]Not Found[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("  .NET 10 SDK: [red]Not Found[/]");
        }

        // 2. Central Package Management
        bool cpm = File.Exists("Directory.Packages.props");
        AnsiConsole.MarkupLine($"  Central Package Management: {(cpm ? "[green]Enforced[/]" : "[red]Missing[/]")}");

        // 3. PostgreSQL connectivity
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var asyncResult = client.BeginConnect("127.0.0.1", 5432, null, null);
            bool success = asyncResult.AsyncWaitHandle.WaitOne(500, false);
            if (success && client.Connected)
                AnsiConsole.MarkupLine("  PostgreSQL (5432): [green]Connected[/]");
            else
                AnsiConsole.MarkupLine("  PostgreSQL (5432): [grey]Unreachable (start via docker compose up -d postgres)[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("  PostgreSQL (5432): [grey]Unreachable[/]");
        }

        // 4. Neo4j connectivity
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var asyncResult = client.BeginConnect("127.0.0.1", 7687, null, null);
            bool success = asyncResult.AsyncWaitHandle.WaitOne(500, false);
            if (success && client.Connected)
                AnsiConsole.MarkupLine("  Neo4j Bolt (7687): [green]Connected[/]");
            else
                AnsiConsole.MarkupLine("  Neo4j Bolt (7687): [grey]Unreachable (start via docker compose up -d neo4j)[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine("  Neo4j Bolt (7687): [grey]Unreachable[/]");
        }

        return 0;
    }

    private static int RunCheck()
    {
        AnsiConsole.MarkupLine("[cyan]Executing Cadence Architecture Sentinels & Quality Gates...[/]\n");

        AnsiConsole.MarkupLine("  1. Validating C# Code Formatting (.editorconfig)...");
        int formatExit = RunProcess("dotnet", "format Cadence.sln --verify-no-changes");
        if (formatExit != 0)
        {
            AnsiConsole.MarkupLine("  [red]Formatting check failed! Run 'dotnet format Cadence.sln' to auto-fix.[/]");
            return formatExit;
        }
        AnsiConsole.MarkupLine("  [green][OK] Code style compliant.[/]");

        AnsiConsole.MarkupLine("\n  2. Validating Architectural Decision Records (ADRs)...");
        int adrExit = RunProcess("python", "scripts/validate_adrs.py");
        if (adrExit != 0)
        {
            AnsiConsole.MarkupLine("  [red]ADR validation failed! Check docs/adr/ and index.md.[/]");
            return adrExit;
        }
        AnsiConsole.MarkupLine("  [green][OK] ADRs compliant.[/]");

        AnsiConsole.MarkupLine("\n  3. Executing Clean Architecture Sentinel Unit Tests...");
        int archExit = RunProcess("dotnet", "test tests/Cadence.ArchitectureTests.csproj --no-build");
        if (archExit != 0)
        {
            AnsiConsole.MarkupLine("  [red]Architecture boundary violations detected![/]");
            return archExit;
        }
        AnsiConsole.MarkupLine("  [green][OK] Clean Architecture boundaries preserved.[/]");

        AnsiConsole.MarkupLine("\n[bold green]All quality sentinels passed.[/]");
        return 0;
    }

    private static int RunTest(string[] args)
    {
        string filter = "";
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            if (arg == "--fast")
            {
                filter = "--filter \"Category!=Integration\"";
            }
            else if (arg == "--integration")
            {
                filter = "--filter \"Category=Integration\"";
            }
        }

        AnsiConsole.MarkupLine($"[cyan]Executing .NET Test Suites {(string.IsNullOrEmpty(filter) ? "(All)" : filter)}...[/]\n");
        return RunProcess("dotnet", $"test Cadence.sln {filter}".Trim());
    }

    private static int RunGxp(string subCommand)
    {
        if (subCommand == "validate")
        {
            AnsiConsole.MarkupLine("[cyan]Validating Requirements Traceability Matrix (read-only)...[/]\n");
            return RunProcess("python", "scripts/generate_rtm.py --validate");
        }

        AnsiConsole.MarkupLine("[cyan]Synchronizing GxP Compliance Documentation (PRD-SYS-053)...[/]\n");
        return RunProcess("python", "scripts/sync_gxp.py");
    }

    private static int RunWizard(string subCommand, string[] extraArgs)
    {
        bool isWindows = OperatingSystem.IsWindows();
        string scriptDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts");
        if (!Directory.Exists(scriptDir))
        {
            scriptDir = Path.GetFullPath("scripts");
        }

        string scriptName = subCommand switch
        {
            "onboarding" => isWindows ? "setup_wizard.ps1" : "setup_wizard.sh",
            "dotnet" => isWindows ? "setup_dotnet_wizard.ps1" : "setup_dotnet_wizard.sh",
            "migrate" => isWindows ? "migrate_service_wizard.ps1" : "migrate_service_wizard.sh",
            "decommission" => isWindows ? "decommission_service_wizard.ps1" : "decommission_service_wizard.sh",
            _ => ""
        };

        if (string.IsNullOrEmpty(scriptName))
        {
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a [cyan]Cadence Developer Wizard[/]:")
                    .AddChoices(
                        "1. Onboarding (Full-Stack: .NET + Node + Python)",
                        "2. .NET Only (SDK, Global Tools, Dev Certs)",
                        "3. Migrate Service (Scaffold Clean Architecture)",
                        "4. Decommission Service (Retire Legacy Python)",
                        "5. Exit"));

            if (selected.StartsWith('1')) return RunWizard("onboarding", []);
            if (selected.StartsWith('2')) return RunWizard("dotnet", []);
            if (selected.StartsWith('3')) return RunWizard("migrate", []);
            if (selected.StartsWith('4')) return RunWizard("decommission", []);
            return 0;
        }

        string scriptPath = Path.Combine(scriptDir, scriptName);
        if (!File.Exists(scriptPath))
        {
            AnsiConsole.MarkupLine($"[red]Wizard script not found at:[/] {scriptPath}");
            return 1;
        }

        // The migrate and decommission wizards take a single positional service name on
        // Windows; the onboarding and dotnet wizards take switches that are forwarded as-is.
        bool isServiceWizard = subCommand is "migrate" or "decommission";
        string windowsExtra = isServiceWizard && extraArgs.Length > 0
            ? $"-ServiceName {extraArgs[0]}"
            : string.Join(' ', extraArgs);
        string arguments = isWindows
            ? $"-ExecutionPolicy Bypass -File \"{scriptPath}\" {windowsExtra}"
            : $"\"{scriptPath}\" {string.Join(' ', extraArgs)}";

        var processInfo = new ProcessStartInfo
        {
            FileName = isWindows ? "powershell.exe" : "bash",
            Arguments = arguments,
            UseShellExecute = false
        };

        using var process = Process.Start(processInfo);
        process?.WaitForExit();
        return process?.ExitCode ?? 0;
    }
}
