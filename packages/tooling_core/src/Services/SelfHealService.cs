using System;
using System.Diagnostics;

namespace Cadence.ToolingCore.Services;

public static class SelfHealService
{
    public static int ExecuteSelfHealing(string? repoRoot = null)
    {
        Console.WriteLine("Executing Self-Healing Pipeline...\n");

        Console.WriteLine("  1. Auto-formatting C# Codebase...");
        var psi = new ProcessStartInfo("dotnet", "format Cadence.sln") { UseShellExecute = false };
        using var p = Process.Start(psi);
        p?.WaitForExit();

        Console.WriteLine("\n  2. Validating ADRs...");
        AdrValidatorService.ValidateAdrs(checkGitDiff: false);

        Console.WriteLine("\n  3. Synchronizing OpenAPI Specifications & Documentation...");
        OpenApiSyncService.SyncOpenApiSpec(validate: false, repoRoot);
        ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot);

        Console.WriteLine("\nSelf-Healing Pipeline Complete.");
        return 0;
    }
}
