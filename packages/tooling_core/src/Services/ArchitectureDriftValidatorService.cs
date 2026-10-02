using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

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
        string ignorePath = Path.Combine(repoRoot, ".arch-drift-ignore");

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

        var ignoredServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(ignorePath))
        {
            foreach (var line in File.ReadLines(ignorePath))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                {
                    ignoredServices.Add(trimmed);
                }
            }
        }

        var targetServices = new List<string>();
        foreach (var svc in composeServices)
        {
            if (ignoredServices.Contains(svc))
            {
                Console.WriteLine($"[INFO] Ignoring container service '{svc}' per exclusion ledger (.arch-drift-ignore).");
            }
            else
            {
                targetServices.Add(svc);
            }
        }

        bool hasDrift = false;

        if (!File.Exists(archPath))
        {
            Console.WriteLine($"[ERROR] ARCHITECTURE.md not found at {archPath}");
            hasDrift = true;
        }
        else
        {
            string archText = File.ReadAllText(archPath);
            foreach (var svc in targetServices)
            {
                if (!archText.Contains(svc, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[ERROR] Compose service '{svc}' not explicitly mentioned in ARCHITECTURE.md");
                    hasDrift = true;
                }
            }
        }

        if (!File.Exists(tddPath))
        {
            Console.WriteLine($"[ERROR] Technical Design Document not found at {tddPath}");
            hasDrift = true;
        }
        else
        {
            string tddText = File.ReadAllText(tddPath);
            foreach (var svc in targetServices)
            {
                if (!tddText.Contains(svc, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[ERROR] Compose service '{svc}' not explicitly mentioned in TDD.md");
                    hasDrift = true;
                }
            }
        }

        if (hasDrift)
        {
            Console.WriteLine("[ERROR] Architecture drift validation failed due to undocumented container service(s).");
            return 1;
        }

        Console.WriteLine($"✔ Architecture drift validation completed cleanly across {targetServices.Count} services.");
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

    private static string FindRepoRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Cadence.sln"))) return current;
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break;
            current = parent ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
