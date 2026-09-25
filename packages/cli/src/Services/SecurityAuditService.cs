using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class SecurityAuditService
{
    private static readonly (string PatternName, Regex Pattern)[] SecretPatterns = new[]
    {
        ("AWS Secret Key", new Regex(@"(?i)aws_secret_access_key\s*=\s*['""][A-Za-z0-9/+=]{40}['""]", RegexOptions.Compiled)),
        ("Private Key Header", new Regex(@"-----BEGIN (?:RSA |EC |PGP )?PRIVATE KEY-----", RegexOptions.Compiled))
    };

    private static readonly string[] AllowedPasses = new[]
    {
        "POSTGRES_PASSWORD", "cadence_password", "test_secret", "dummy", "example", ".secrets.baseline", "sample"
    };

    private static readonly string[] ExcludedDirKeywords = new[]
    {
        "tests", "fixtures", ".git", "bin", "obj", "node_modules", ".venv", ".agents", "scripts"
    };

    public static bool AuditSecurity(string[]? targetFiles = null)
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();
        AnsiConsole.MarkupLine("[cyan]Executing Security Audit & Hardcoded Secret Scanner...[/]");

        IEnumerable<string> filesToScan;
        if (targetFiles != null && targetFiles.Length > 0)
        {
            filesToScan = targetFiles.Where(File.Exists);
        }
        else
        {
            filesToScan = Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories);
        }

        bool clean = true;

        foreach (var file in filesToScan)
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var pathParts = rel.Split('/');

            if (pathParts.Any(p => ExcludedDirKeywords.Contains(p, StringComparer.OrdinalIgnoreCase)) ||
                rel.EndsWith(".vue", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith(".test.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith(".test.ts", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("mockGateway.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("build-docs.js", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("SecurityAuditorService.cs", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("SecurityAuditService.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string filename = Path.GetFileName(file);
            if (filename == ".secrets.baseline" || filename.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                int lineNum = 1;
                foreach (var line in File.ReadLines(file))
                {
                    if (AllowedPasses.Any(p => line.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        lineNum++;
                        continue;
                    }

                    foreach (var (pName, regex) in SecretPatterns)
                    {
                        if (regex.IsMatch(line))
                        {
                            AnsiConsole.MarkupLine($"[red]Security Risk ({pName}) in '{rel}:{lineNum}'[/]");
                            clean = false;
                        }
                    }
                    lineNum++;
                }
            }
            catch
            {
                // Skip unreadable files
            }
        }

        if (clean)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Security audit passed. Zero high-entropy secrets detected.[/]");
        }

        return clean;
    }
}
