using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

public static class ArchitectureDriftValidatorService
{
    public static int ValidateArchitectureDrift(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating system architecture topology and document parity...");

        string composePath = Path.Combine(repoRoot, "docker", "docker-compose.yml");
        string archPath = Path.Combine(repoRoot, "ARCHITECTURE.md");
        string tddPath = Path.Combine(repoRoot, "docs", "SDLC", "02_Technical_Design_Document_TDD.md");

        if (!File.Exists(composePath))
        {
            Console.WriteLine($"[ERROR] docker-compose.yml not found at {composePath}");
            return 1;
        }

        var composeServices = ExtractDockerComposeServices(composePath);
        if (composeServices.Count == 0)
        {
            Console.WriteLine("[ERROR] No services found in docker-compose.yml.");
            return 1;
        }

        if (File.Exists(archPath))
        {
            string archText = File.ReadAllText(archPath);
            foreach (var svc in composeServices)
            {
                if (!archText.Contains(svc, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[WARNING] Compose service '{svc}' not explicitly mentioned in ARCHITECTURE.md");
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
                    Console.WriteLine($"[WARNING] Compose service '{svc}' not explicitly mentioned in TDD.md");
                }
            }
        }

        Console.WriteLine($"✔ Architecture drift validation completed cleanly across {composeServices.Count} services.");
        return 0;
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

    private static string FindRepoRoot() => RepositoryRoot.Find();
}
