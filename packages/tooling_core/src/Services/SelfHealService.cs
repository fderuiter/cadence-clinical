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
        bool ok = p is { ExitCode: 0 };

        Console.WriteLine("\n  2. Validating ADRs...");
        ok &= AdrValidatorService.ValidateAdrs(checkGitDiff: false, fix: true);

        Console.WriteLine("\n  3. Synchronizing OpenAPI Specifications & Documentation...");
        ok &= OpenApiSyncService.SyncOpenApiSpec(validate: false, repoRoot) == 0;
        ok &= ApiDocsGeneratorService.GenerateApiDocs(validate: false, repoRoot) == 0;

        Console.WriteLine(ok ? "\nSelf-Healing Pipeline Complete." : "\n[ERROR] Self-Healing Pipeline finished with failures (see above).");
        return ok ? 0 : 1;
    }
}
