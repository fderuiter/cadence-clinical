using System.ComponentModel;
using System.Diagnostics;

using Spectre.Console;
using Spectre.Console.Cli;

namespace Cadence.Cli.Commands;

public class TestCommand : Command<TestCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [CommandOption("-f|--fast")]
        [Description("Run fast unit tests excluding integration tests")]
        public bool Fast { get; set; }

        [CommandOption("-i|--integration")]
        [Description("Run integration test suites only")]
        public bool Integration { get; set; }
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        string filter = "";
        if (settings.Fast)
        {
            filter = "--filter \"Category!=Integration\"";
        }
        else if (settings.Integration)
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
