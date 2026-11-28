using System;
using System.IO;

using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.Services;

public static class ScaffoldReadmesService
{
    public static int ScaffoldReadmes(string? repoRoot = null)
    {
        repoRoot ??= WorkspacePrimitives.FindRepoRoot();
        Console.WriteLine("Scaffolding missing README documentation files...");

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
                    string name = Path.GetFileName(subDir);
                    string content = $"# {name}\n\nDocumentation for {name}.\n";
                    File.WriteAllText(readmePath, content);
                    Console.WriteLine($"✔ Created missing README for {name}");
                }
            }
        }

        return 0;
    }
}
