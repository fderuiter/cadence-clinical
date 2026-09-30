using System.CommandLine;
using System.Diagnostics;

using Spectre.Console;

namespace Cadence.Cli.Commands;

public static class TestCommand
{
    public static void Configure(Command command)
    {
        var fast = new Option<bool>("--fast", "-f") { Description = "Run every test except the integration tests" };
        var integration = new Option<bool>("--integration", "-i") { Description = "Run only the PostgreSQL integration tests (tests/Cadence.IntegrationTests)" };
        command.Options.Add(fast);
        command.Options.Add(integration);
        command.SetAction(result => Execute(result.GetValue(fast), result.GetValue(integration)));
    }

    private static int Execute(bool fast, bool integration)
    {
        // Integration tests live in their own project (docs/TESTING.md); every test in it carries Category=Integration.
        string target = integration && !fast ? "tests/Cadence.IntegrationTests/Cadence.IntegrationTests.csproj" : "Cadence.sln";
        string filter = fast ? "--filter \"Category!=Integration\"" : "";

        AnsiConsole.MarkupLine($"[cyan]Executing .NET tests: {Markup.Escape($"{target} {filter}".Trim())}...[/]\n");

        string repoRoot = Cadence.Cli.Services.QualityGateOrchestrator.FindRepoRoot();
        string targetPath = System.IO.Path.Combine(repoRoot, target);

        var psi = new ProcessStartInfo("dotnet", $"test \"{targetPath}\" {filter}".Trim())
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
