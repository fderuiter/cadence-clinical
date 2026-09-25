using System;
using System.IO;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class ImportsValidatorService
{
    private static readonly Regex CSharpImportRegex = new(@"^\s*using\s+Cadence\.Apps\.([a-zA-Z0-9]+)", RegexOptions.Compiled);

    public static bool ValidateImports()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        AnsiConsole.MarkupLine("[cyan]Validating Cross-Service Import Boundaries...[/]");

        string[] searchDirs = new[] { Path.Combine(repoRoot, "apps"), Path.Combine(repoRoot, "packages") };
        bool allPassed = true;

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains("/obj/") || file.Contains("/bin/")) continue;

                string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                string currentApp = ExtractAppName(relPath);

                if (string.IsNullOrEmpty(currentApp)) continue;

                foreach (var line in File.ReadLines(file))
                {
                    var match = CSharpImportRegex.Match(line);
                    if (match.Success)
                    {
                        string targetApp = match.Groups[1].Value;
                        if (!currentApp.Equals(targetApp, StringComparison.OrdinalIgnoreCase) && !IsAllowedCrossImport(currentApp))
                        {
                            AnsiConsole.MarkupLine($"[red]Forbidden cross-service import in '{relPath}': reference to '{targetApp}'[/]");
                            allPassed = false;
                        }
                    }
                }
            }
        }

        if (allPassed)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Cross-service import boundaries verified.[/]");
        }

        return allPassed;
    }

    private static string ExtractAppName(string relPath)
    {
        if (relPath.StartsWith("apps/", StringComparison.Ordinal))
        {
            var parts = relPath.Split('/');
            if (parts.Length > 1) return parts[1];
        }
        return string.Empty;
    }

    private static bool IsAllowedCrossImport(string source)
    {
        return source.Equals("gateway", StringComparison.OrdinalIgnoreCase);
    }
}
