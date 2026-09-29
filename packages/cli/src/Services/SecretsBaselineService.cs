namespace Cadence.Cli.Services;

public static class SecretsBaselineService
{
    public static bool CleanBaseline(string? customPath = null)
    {
        return Cadence.ToolingCore.Services.SecretsBaselineCleanerService.CleanSecretsBaseline(customPath) == 0;
    }
}
