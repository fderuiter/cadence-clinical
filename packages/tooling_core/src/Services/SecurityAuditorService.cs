using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Security;

namespace Cadence.ToolingCore.Services;

public static class SecurityAuditorService
{
    public static int AuditSecurity(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Executing hardcoded secret scanner and security audit...");

        IEnumerable<string> filesToScan;
        if (files != null && files.Length > 0)
        {
            filesToScan = files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists);
        }
        else
        {
            filesToScan = Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories);
        }

        var findings = SecretScanner.ScanFiles(repoRoot, filesToScan);

        foreach (var finding in findings)
        {
            Console.WriteLine($"[SECURITY FINDING] Security Risk ({finding.PatternName}) in '{finding.FilePath}:{finding.LineNumber}'");
        }

        if (findings.Count > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {findings.Count} security findings.");
            return 1;
        }

        Console.WriteLine("✔ Security audit passed cleanly. Zero high-entropy secrets detected.");
        return 0;
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
