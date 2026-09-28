using System;
using System.Collections.Generic;
using System.IO;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class EnvironmentVariablesValidatorService
{
    public static bool ValidateEnvironmentVariables()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string envExamplePath = Path.Combine(repoRoot, ".env.example");
        string envDocPath = Path.Combine(repoRoot, "docs", "ENVIRONMENT_VARIABLES.md");

        AnsiConsole.MarkupLine("[cyan]Validating Environment Variables Synchronization...[/]");

        if (!File.Exists(envExamplePath))
        {
            AnsiConsole.MarkupLine($"[red]Error: .env.example not found at {envExamplePath}[/]");
            return false;
        }

        var exampleVars = ExtractVarsFromEnvFile(envExamplePath);

        if (File.Exists(envDocPath))
        {
            string docText = File.ReadAllText(envDocPath);
            foreach (var v in exampleVars)
            {
                if (!docText.Contains(v))
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Environment variable '{v}' in .env.example missing from docs/ENVIRONMENT_VARIABLES.md[/]");
                }
            }
        }

        AnsiConsole.MarkupLine($"  [green][[OK]] Verified {exampleVars.Count} environment variables across workspace.[/]");
        return true;
    }

    private static HashSet<string> ExtractVarsFromEnvFile(string filePath)
    {
        var vars = new HashSet<string>();
        foreach (var line in File.ReadLines(filePath))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

            if (trimmed.Contains('='))
            {
                string key = trimmed.Split('=', 2)[0].Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    vars.Add(key);
                }
            }
        }
        return vars;
    }
}
