using System;
using System.IO;

namespace Cadence.ToolingCore.Services;

public static class CodeownersValidatorService
{
    public static int ValidateCodeowners(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Validating .github/CODEOWNERS file presence and configuration...");

        string codeownersPath = Path.Combine(repoRoot, ".github", "CODEOWNERS");
        if (!File.Exists(codeownersPath))
        {
            Console.WriteLine("[ERROR] .github/CODEOWNERS file is missing!");
            return 1;
        }

        string content = File.ReadAllText(codeownersPath);
        if (string.IsNullOrWhiteSpace(content))
        {
            Console.WriteLine("[ERROR] .github/CODEOWNERS file is empty!");
            return 1;
        }

        Console.WriteLine("✔ .github/CODEOWNERS validated cleanly.");
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
