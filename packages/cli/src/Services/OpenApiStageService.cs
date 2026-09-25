using System;
using System.IO;

using Cadence.ToolingCore.OpenApi;

using Spectre.Console;

namespace Cadence.Cli.Services;

public static class OpenApiStageService
{
    public static bool StageAndValidateOpenApi()
    {
        string repoRoot = QualityGateOrchestrator.FindRepoRoot();

        AnsiConsole.MarkupLine("[cyan]Auto-Staging & Validating OpenAPI Schemas...[/]");

        int exitCode = OpenApiToolingService.ValidateOpenApiDrift(repoRoot);
        return exitCode == 0;
    }
}
