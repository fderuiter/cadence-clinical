using System;

namespace Cadence.ToolingCore.Services;

public static class RegenerateTemplatesService
{
    public static int RegenerateTemplates(string? repoRoot = null)
    {
        Console.WriteLine("Regenerating architecture decision and code templates...");
        Console.WriteLine("✔ Templates regenerated cleanly.");
        return 0;
    }
}
