using System;
using System.IO;
using System.Linq;

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
                string name = Path.GetFileName(subDir);
                if (name.StartsWith('.') || name == "__pycache__" || name == "bin" || name == "obj" || name == "node_modules" || name == ".venv")
                {
                    continue;
                }

                // A package deleted upstream leaves its build output behind in existing checkouts. Such a
                // directory is not a project, so it needs no README.
                if (ContainsOnlyBuildOutput(subDir))
                {
                    continue;
                }

                string readmePath = Path.Combine(subDir, "README.md");
                string relPath = Path.GetRelativePath(repoRoot, subDir);

                if (!File.Exists(readmePath))
                {
                    Console.WriteLine($"[MISSING README] Directory '{relPath}' lacks a README.md file.");
                    missing++;
                }
                else
                {
                    string content = File.ReadAllText(readmePath);
                    if (string.IsNullOrWhiteSpace(content) || !content.Contains("# "))
                    {
                        Console.WriteLine($"[INVALID README] README.md in '{relPath}' is empty or lacks title header ('# ')");
                        missing++;
                    }
                }
            }
        }

        if (missing > 0)
        {
            Console.WriteLine($"\n[ERROR] Found {missing} directories missing or with invalid README.md files.");
            return 1;
        }

        Console.WriteLine("✔ README coverage validated cleanly.");
        return 0;
    }

    private static bool ContainsOnlyBuildOutput(string directory) =>
        !Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/'))
            .Any(f => !f.Split('/').Any(part => part is "bin" or "obj" or "node_modules"));

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
