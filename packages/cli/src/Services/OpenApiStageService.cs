namespace Cadence.Cli.Services;

public static class OpenApiStageService
{
    public static bool StageAndValidateOpenApi()
    {
        return Cadence.ToolingCore.Services.PreCommitOpenApiService.ExecutePreCommitOpenApi() == 0;
    }
}
