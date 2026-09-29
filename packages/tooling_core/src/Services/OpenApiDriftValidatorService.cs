using System;

namespace Cadence.ToolingCore.Services;

public static class OpenApiDriftValidatorService
{
    public static int ValidateDrift(string? repoRoot = null)
    {
        Console.WriteLine("--- Running Native OpenAPI Specification & Documentation Drift Sentinel ---");

        Console.WriteLine("\n[GATE 1/2] Checking docs/openapi against the .NET services...");
        int resSpec = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot);

        Console.WriteLine("\n[GATE 2/2] Checking docs/api against docs/openapi...");
        int resDocs = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);

        if (resSpec != 0 || resDocs != 0)
        {
            Console.WriteLine("\n[ERROR] OpenAPI Specification Drift Detected!");
            return 1;
        }

        Console.WriteLine("\n[SUCCESS] OpenAPI Specification Drift Sentinel passed cleanly.");
        return 0;
    }
}
