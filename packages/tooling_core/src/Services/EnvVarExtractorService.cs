using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cadence.ToolingCore.Services;

public static partial class EnvVarExtractorService
{
    [GeneratedRegex(@"GetEnvironmentVariable\s*\(\s*""([^""]+)""")]
    private static partial Regex DotnetEnvRegex();

    [GeneratedRegex(@"os\.environ\.get\s*\(\s*""([^""]+)""|os\.environ\[""([^""]+)""\]")]
    private static partial Regex PythonEnvRegex();

    public static int ExtractAndValidateEnvVars(bool checkOnly = true, string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        Console.WriteLine("Extracting and verifying environment variable declarations...");

        var envVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var searchDirs = new[] { "apps", "packages" };
        foreach (var dir in searchDirs)
        {
            string fullPath = Path.Combine(repoRoot, dir);
            if (!Directory.Exists(fullPath)) continue;

            foreach (var file in Directory.EnumerateFiles(fullPath, "*.*", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}"))
                    continue;

                string ext = Path.GetExtension(file).ToLowerInvariant();
                string content;
                try { content = File.ReadAllText(file); } catch { continue; }

                if (ext == ".cs")
                {
                    var csMatches = DotnetEnvRegex().Matches(content);
                    for (int i = 0; i < csMatches.Count; i++)
                    {
                        if (csMatches[i].Groups[1].Success)
                        {
                            envVars.Add(csMatches[i].Groups[1].Value);
                        }
                    }
                }
                else if (ext == ".py")
                {
                    var pyMatches = PythonEnvRegex().Matches(content);
                    for (int i = 0; i < pyMatches.Count; i++)
                    {
                        var m = pyMatches[i];
                        if (m.Groups[1].Success) envVars.Add(m.Groups[1].Value);
                        if (m.Groups[2].Success) envVars.Add(m.Groups[2].Value);
                    }
                }
            }
        }

        Console.WriteLine($"Discovered {envVars.Count} distinct environment variable accesses in code.");

        string envExamplePath = Path.Combine(repoRoot, ".env.example");
        if (checkOnly && File.Exists(envExamplePath))
        {
            string exampleContent = File.ReadAllText(envExamplePath);
            var missingInExample = envVars.Where(v => !exampleContent.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();

            if (missingInExample.Count > 0)
            {
                Console.WriteLine($"[WARNING] {missingInExample.Count} environment variables found in code are missing from .env.example:");
                foreach (var missing in missingInExample)
                {
                    Console.WriteLine($"  - {missing}");
                }
            }
        }

        Console.WriteLine("✔ Environment variable extraction and verification complete.");
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
