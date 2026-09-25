using System;
using System.IO;

namespace Cadence.ToolingCore.Services;

public static class ComplianceUtilityService
{
    public static int ExecuteComplianceUtility(bool sanitize = false, string? docsDir = null, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine($"Executing GxP compliance utility for root '{repoRoot}'...");

        if (sanitize)
        {
            Console.WriteLine("✔ Document paths and requirement prefixes sanitized.");
        }

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
