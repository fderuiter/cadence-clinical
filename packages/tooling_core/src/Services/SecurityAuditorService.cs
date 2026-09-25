using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class SecurityAuditorService
{
    private static readonly string[] BinaryExtensions = [".dll", ".exe", ".pdb", ".so", ".dylib", ".o", ".a"];

    public static int AuditSecurity(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Executing hardcoded secret scanner and security audit...");

        var candidateFiles = files != null && files.Length > 0
            ? files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists)
            : Directory.EnumerateFiles(repoRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}.venv{Path.DirectorySeparatorChar}"));

        int findings = 0;
        string[] secretPatterns = ["AKIA[0-9A-Z]{16}", "-----BEGIN PRIVATE KEY-----", "-----BEGIN RSA PRIVATE KEY-----"];

        foreach (var file in candidateFiles)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (BinaryExtensions.Contains(ext)) continue;

            string content;
            try { content = File.ReadAllText(file); } catch { continue; }

            foreach (var pattern in secretPatterns.Where(p => System.Text.RegularExpressions.Regex.IsMatch(content, p)))
            {
                string relPath = Path.GetRelativePath(repoRoot, file);
                Console.WriteLine($"[SECURITY FINDING] {relPath} - Potential hardcoded secret detected matching '{pattern}'");
                findings++;
            }
        }

        if (findings > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {findings} security findings.");
            return 1;
        }

        Console.WriteLine("✔ Hardcoded secret scanner audit passed cleanly.");
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
