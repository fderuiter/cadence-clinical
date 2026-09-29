using System;

namespace Cadence.ToolingCore.Services;

public static class PreCommitOpenApiService
{
    public static int ExecutePreCommitOpenApi(string? repoRoot = null)
    {
        Console.WriteLine("Checking docs/openapi and docs/api against the .NET services...");
        int specs = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot);
        int docs = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
        return specs != 0 || docs != 0 ? 1 : 0;
    }
}
