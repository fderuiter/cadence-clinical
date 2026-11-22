using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.ToolingCore.Security;

namespace Cadence.ToolingCore.Services;

public static class SecurityAuditorService
{
    public static int AuditSecurity(
        string[]? files = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Executing hardcoded secret scanner and security audit...");

        if (files is { Length: 0 })
        {
            Console.WriteLine("✔ Security audit passed cleanly (no target files).");
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            IEnumerable<string> filesToScan;
            if (files is not null)
            {
                filesToScan = files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f));
            }
            else
            {
                filesToScan = Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories);
            }

            var findings = SecretScanner.ScanFiles(repoRoot, filesToScan, reader);

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
    }

    private static string FindRepoRoot() => PreCommitOpenApiService.FindRepoRoot();
}
