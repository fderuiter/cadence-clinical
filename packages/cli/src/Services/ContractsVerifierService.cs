using System;

using Cadence.ToolingCore.Contracts;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class ContractsVerifierService
{
    public static bool VerifyContracts()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        AnsiConsole.MarkupLine("[cyan]Executing Backend Port Contract Static Verification...[/]");

        var result = ContractVerifier.VerifyBackendContracts(repoRoot);

        foreach (var err in result.Errors)
        {
            AnsiConsole.MarkupLine($"[red]{err}[/]");
        }

        if (result.IsValid)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Hexagonal port contracts static verification passed.[/]");
        }

        return result.IsValid;
    }
}

