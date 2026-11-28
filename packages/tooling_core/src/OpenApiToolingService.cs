using Cadence.ToolingCore.Primitives;

namespace Cadence.ToolingCore.OpenApi;

/// <summary>
/// Locates the repository root for tooling and tests. The Python-era OpenAPI sync and API-docs code
/// that used to live here is replaced by <c>Services.OpenApiSyncService</c> and <c>Services.ApiDocsGeneratorService</c>.
/// </summary>
public static class OpenApiToolingService
{
    public static string FindRepoRoot() => WorkspacePrimitives.FindRepoRoot();
}
