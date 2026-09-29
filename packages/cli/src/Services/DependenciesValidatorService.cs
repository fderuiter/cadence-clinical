namespace Cadence.Cli.Services;

public static class DependenciesValidatorService
{
    public static bool ValidateDependencies()
    {
        return Cadence.ToolingCore.Services.DependencyValidatorService.ValidateDependencies() == 0;
    }
}
