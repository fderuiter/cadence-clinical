using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class ImportValidatorService
{
    public static int ValidateImports(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating cross-service architectural import boundaries...");

        var appsDir = Path.Combine(repoRoot, "apps");
        if (!Directory.Exists(appsDir))
        {
            Console.WriteLine("✔ Apps directory not found, skipping import validation.");
            return 0;
        }

        int violations = 0;
        var appDirs = Directory.GetDirectories(appsDir);

        foreach (var appDir in appDirs)
        {
            string appName = Path.GetFileName(appDir);
            var csFiles = Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

            foreach (var file in csFiles)
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int lineNo = 0; lineNo < lines.Length; lineNo++)
                {
                    string line = lines[lineNo].Trim();
                    if (line.StartsWith("using Cadence.Apps.", StringComparison.Ordinal))
                    {
                        var parts = line.Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            string ns = parts[1];
                            var subParts = ns.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                            if (subParts.Length >= 3)
                            {
                                string targetApp = subParts[2].ToLowerInvariant();
                                // Folder names use snake case (core_models) where namespaces use Pascal case (CoreModels).
                                if (!NormalizeAppName(targetApp).Equals(NormalizeAppName(appName), StringComparison.OrdinalIgnoreCase) &&
                                    !appName.Equals("gateway", StringComparison.OrdinalIgnoreCase))
                                {
                                    violations++;
                                    string relPath = Path.GetRelativePath(repoRoot, file);
                                    Console.WriteLine($"[BOUNDARY VIOLATION] {relPath}:{lineNo + 1} - Service '{appName}' directly references sibling service '{targetApp}': '{line}'");
                                }
                            }
                        }
                    }
                }
            }
        }

        if (violations > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {violations} architectural import boundary violations.");
            return 1;
        }

        Console.WriteLine("✔ Architectural import boundaries validated cleanly.");
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

    private static string NormalizeAppName(string name) =>
        name.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
}
