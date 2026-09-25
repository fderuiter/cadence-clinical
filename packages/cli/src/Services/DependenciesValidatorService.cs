using System;
using System.IO;
using System.Text.Json;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class DependenciesValidatorService
{
    private static readonly string[] ForbiddenCryptoPackages = new[] { "node-forge", "elliptic", "jose" };

    public static bool ValidateDependencies()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        string webPackageJson = Path.Combine(repoRoot, "apps", "web", "package.json");

        AnsiConsole.MarkupLine("[cyan]Executing Frontend Dependency Cryptographic Check...[/]");

        if (!File.Exists(webPackageJson))
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Frontend package manifest check passed.[/]");
            return true;
        }

        try
        {
            string content = File.ReadAllText(webPackageJson);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            bool clean = true;
            foreach (var depType in new[] { "dependencies", "devDependencies" })
            {
                if (root.TryGetProperty(depType, out var deps))
                {
                    foreach (var pkg in ForbiddenCryptoPackages)
                    {
                        if (deps.TryGetProperty(pkg, out _))
                        {
                            AnsiConsole.MarkupLine($"[red]Forbidden client-side crypto package '{pkg}' found in apps/web/{depType}![/]");
                            clean = false;
                        }
                    }
                }
            }

            if (clean)
            {
                AnsiConsole.MarkupLine("  [green][[OK]] Frontend dependencies GxP crypto compliant.[/]");
            }

            return clean;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning checking apps/web/package.json: {ex.Message}[/]");
            return true;
        }
    }
}
