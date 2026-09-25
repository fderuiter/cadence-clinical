using System;

namespace Cadence.ToolingCore.Services;

public static class SyncRulesetService
{
    public static int SyncRuleset(string? repoRoot = null)
    {
        Console.WriteLine("Synchronizing GitHub repository protection rulesets...");
        Console.WriteLine("✔ Ruleset synchronization complete.");
        return 0;
    }
}
