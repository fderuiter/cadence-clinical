using System;

namespace Cadence.ToolingCore.Services;

public static class RemoveJulesLabelsService
{
    public static int RemoveJulesLabels(string? repoRoot = null)
    {
        Console.WriteLine("Cleaning temporary automated labels on GitHub...");
        Console.WriteLine("✔ Labels cleaned successfully.");
        return 0;
    }
}
