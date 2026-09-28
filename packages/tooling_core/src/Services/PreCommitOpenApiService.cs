using System;

namespace Cadence.ToolingCore.Services;

public static class PreCommitOpenApiService
{
    public static int ExecutePreCommitOpenApi(string? repoRoot = null)
    {
        Console.WriteLine("Executing pre-commit OpenAPI specification auto-staging...");
        return OpenApiSyncService.SyncOpenApiSpec(validate: false, repoRoot);
    }
}
