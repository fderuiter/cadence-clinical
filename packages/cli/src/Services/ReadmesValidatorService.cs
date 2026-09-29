namespace Cadence.Cli.Services;

public static class ReadmesValidatorService
{
    public static bool ValidateReadmes()
    {
        return Cadence.ToolingCore.Services.ReadmeValidatorService.ValidateReadmes() == 0;
    }
}
