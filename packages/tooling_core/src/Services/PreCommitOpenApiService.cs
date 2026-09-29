using System;

namespace Cadence.ToolingCore.Services;

public static class PreCommitOpenApiService
{
    /// <summary>
    /// The <c>openapi</c> gate. The full check boots every .NET service to export its spec, which takes
    /// minutes, so the pre-commit hook runs the quick check instead: <c>docs/api</c> against the committed
    /// <c>docs/openapi</c> specs. <c>cadence check</c>, <c>make verify</c> and CI run the full check.
    /// </summary>
    public static int ExecutePreCommitOpenApi(string? repoRoot = null, bool quick = false)
    {
        if (quick)
        {
            Console.WriteLine("Checking docs/api against docs/openapi (quick check; run 'pnpm cadence check openapi' to compare with the services)...");
            return ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
        }

        Console.WriteLine("Checking docs/openapi and docs/api against the .NET services...");
        int specs = OpenApiSyncService.SyncOpenApiSpec(validate: true, repoRoot);
        int docs = ApiDocsGeneratorService.GenerateApiDocs(validate: true, repoRoot);
        return specs != 0 || docs != 0 ? 1 : 0;
    }
}
