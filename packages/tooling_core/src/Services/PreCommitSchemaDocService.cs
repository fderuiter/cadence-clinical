using System;

namespace Cadence.ToolingCore.Services;

public static class PreCommitSchemaDocService
{
    public static int ExecutePreCommitSchemaDoc(string? repoRoot = null)
    {
        Console.WriteLine("Executing pre-commit schema documentation auto-staging...");
        return SchemaDocGeneratorService.GenerateSchemaDocumentation(repoRoot);
    }
}
