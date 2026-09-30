using System.CommandLine;
using System.Diagnostics;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class TestCommand
{
    public static void Configure(Command command)
    {
        var fast = new Option<bool>("--fast", "-f") { Description = "Run fast unit tests excluding integration tests" };
        var integration = new Option<bool>("--integration", "-i") { Description = "Run integration test suites only" };
        command.Options.Add(fast);
        command.Options.Add(integration);
        command.SetAction(result => Execute(result.GetValue(fast), result.GetValue(integration)));
    }

    private static int Execute(bool fast, bool integration)
    {
        string filter = "";
        if (fast)
        {
            filter = "--filter \"Category!=Integration\"";
        }
        else if (integration)
        {
            filter = "--filter \"Category=Integration\"";
        }

        AnsiConsole.MarkupLine($"[cyan]Executing .NET Test Suites {(string.IsNullOrEmpty(filter) ? "(All)" : filter)}...[/]\n");

        string repoRoot = Cadence.Cli.Services.QualityGateOrchestrator.FindRepoRoot();
        string slnPath = System.IO.Path.Combine(repoRoot, "Cadence.sln");

        var psi = new ProcessStartInfo("dotnet", $"test \"{slnPath}\" {filter}".Trim())
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi);
        if (proc == null) return 1;
        proc.WaitForExit();
        return proc.ExitCode;
    }
}
