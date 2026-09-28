using System;
using System.IO;

namespace Cadence.ToolingCore.Services;

public static class ReadmeValidatorService
{
    public static int ValidateReadmes(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating README documentation coverage across packages and apps...");

        int missing = 0;
        string[] targetDirs = ["apps", "packages"];

        foreach (var dirName in targetDirs)
        {
            string fullDir = Path.Combine(repoRoot, dirName);
            if (!Directory.Exists(fullDir)) continue;

            foreach (var subDir in Directory.GetDirectories(fullDir))
            {
                string readmePath = Path.Combine(subDir, "README.md");
                if (!File.Exists(readmePath))
                {
                    string relPath = Path.GetRelativePath(repoRoot, subDir);
                    Console.WriteLine($"[MISSING README] Directory '{relPath}' lacks a README.md file.");
                    missing++;
                }
            }
        }

        if (missing > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {missing} directories missing README.md files.");
            return 1;
        }

        Console.WriteLine("✔ README coverage validated cleanly.");
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
