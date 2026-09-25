using System;
using System.IO;
using System.Linq;

namespace Cadence.ToolingCore.Services;

public static class CleanTestDbsService
{
    public static int CleanTestDatabases(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Purging local test SQLite database files...");

        var dbFiles = Directory.GetFiles(repoRoot, "*.db", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(repoRoot, "*.sqlite", SearchOption.AllDirectories))
            .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"))
            .ToList();

        int deleted = 0;
        foreach (var file in dbFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch
            {
                // Ignore locked files
            }
        }

        Console.WriteLine($"✔ Purged {deleted} local test database files.");
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
