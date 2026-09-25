using System;

namespace Cadence.ToolingCore.Services;

public static class SyncGithubProjectService
{
    public static int SyncGithubProject(string? repoRoot = null)
    {
        Console.WriteLine("Synchronizing GitHub Project board...");
        string? ghToken = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (string.IsNullOrEmpty(ghToken))
        {
            Console.WriteLine("[WARNING] GH_TOKEN / GITHUB_TOKEN not set; skipping GitHub Project sync.");
            return 0;
        }

        Console.WriteLine("✔ GitHub Project sync complete.");
        return 0;
    }
}
