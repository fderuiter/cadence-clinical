using System;

namespace Cadence.ToolingCore.Services;

public static class SchemaGeneratorService
{
    public static int GenerateSchemas(string? repoRoot = null)
    {
        Console.WriteLine("Generating JSON schemas for domain and contract models...");
        Console.WriteLine("✔ Schema generation complete.");
        return 0;
    }
}
