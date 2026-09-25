using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Security;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class SecurityAuditService
{
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

        var findings = SecretScanner.ScanFiles(repoRoot, filesToScan);

        foreach (var finding in findings)
        {
            AnsiConsole.MarkupLine($"[red]Security Risk ({finding.PatternName}) in '{finding.FilePath}:{finding.LineNumber}'[/]");
        }

        bool clean = findings.Count == 0;

        if (clean)
        {
            AnsiConsole.MarkupLine("  [green][[OK]] Security audit passed. Zero high-entropy secrets detected.[/]");
        }

        return clean;
    }
}

