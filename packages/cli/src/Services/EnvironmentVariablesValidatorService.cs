namespace Cadence.Cli.Services;

public static class EnvironmentVariablesValidatorService
{
    public static bool ValidateEnvironmentVariables()
    {
        return Cadence.ToolingCore.Services.EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true) == 0;
    }
}
