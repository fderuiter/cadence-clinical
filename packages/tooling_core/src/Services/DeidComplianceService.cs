using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static class DeidComplianceService
{
    private static readonly Regex SsnPattern = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);

    public static int ScanDeidCompliance(string[]? files = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Executing DEID compliance scan...");

        var candidateFiles = files != null && files.Length > 0
            ? files.Select(f => Path.IsPathRooted(f) ? f : Path.Combine(repoRoot, f)).Where(File.Exists).ToList()
            : Directory.GetFiles(repoRoot, "*.cs", SearchOption.AllDirectories).ToList();

        int violations = 0;

        foreach (var file in candidateFiles)
        {
            string rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/node_modules/") || rel.Contains("tests/")) continue;
            if (!File.Exists(file)) continue;

            try
            {
                string text = File.ReadAllText(file);
                if (SsnPattern.IsMatch(text))
                {
                    Console.WriteLine($"[DEID VIOLATION] Raw SSN pattern detected in '{rel}'");
                    violations++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] Error reading '{file}': {ex.Message}");
            }
        }

        if (violations > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {violations} DEID compliance violations.");
            return 1;
        }

        Console.WriteLine("✔ DEID compliance scan passed cleanly.");
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
