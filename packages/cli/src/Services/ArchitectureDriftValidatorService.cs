using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class ArchitectureDriftValidatorService
{
    public static bool ValidateArchitectureDrift()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string composePath = Path.Combine(repoRoot, "docker", "docker-compose.yml");
        string archPath = Path.Combine(repoRoot, "ARCHITECTURE.md");
        string tddPath = Path.Combine(repoRoot, "docs", "SDLC", "02_Technical_Design_Document_TDD.md");

        AnsiConsole.MarkupLine("[cyan]Validating Architecture Drift...[/]");

        if (!File.Exists(composePath))
        {
            AnsiConsole.MarkupLine($"[red]Error: docker-compose.yml not found at {composePath}[/]");
            return false;
        }

        var composeServices = ExtractDockerComposeServices(composePath);
        if (composeServices.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]Error: No services found in docker-compose.yml.[/]");
            return false;
        }

        bool allPassed = true;

        if (File.Exists(archPath))
        {
            string archText = File.ReadAllText(archPath);
            foreach (var svc in composeServices)
            {
                if (!archText.Contains(svc, StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Compose service '{svc}' not explicitly mentioned in ARCHITECTURE.md[/]");
                }
            }
        }

        if (File.Exists(tddPath))
        {
            string tddText = File.ReadAllText(tddPath);
            foreach (var svc in composeServices)
            {
                if (!tddText.Contains(svc, StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Compose service '{svc}' not explicitly mentioned in TDD.md[/]");
                }
            }
        }

        AnsiConsole.MarkupLine($"  [green][[OK]] Architecture drift validation completed across {composeServices.Count} services.[/]");
        return allPassed;
    }

    private static HashSet<string> ExtractDockerComposeServices(string composePath)
    {
        var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool inServicesBlock = false;
        var serviceRegex = new Regex(@"^\s\s([a-zA-Z0-9_\-]+):\s*$", RegexOptions.Compiled);

        foreach (var line in File.ReadLines(composePath))
        {
            if (line.TrimStart().StartsWith("services:", StringComparison.Ordinal))
            {
                inServicesBlock = true;
                continue;
            }

            if (inServicesBlock)
            {
                if (line.Length > 0 && !line.StartsWith(' ') && !line.StartsWith('\t') && !line.StartsWith('#'))
                {
                    inServicesBlock = false;
                    continue;
                }

                var match = serviceRegex.Match(line);
                if (match.Success)
                {
                    services.Add(match.Groups[1].Value);
                }
            }
        }

        return services;
    }
}
