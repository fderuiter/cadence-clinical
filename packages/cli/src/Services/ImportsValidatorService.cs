namespace Cadence.Cli.Services;

public static class ImportsValidatorService
{
    public static bool ValidateImports()
    {
        return Cadence.ToolingCore.Services.ImportValidatorService.ValidateImports() == 0;
    }
}
