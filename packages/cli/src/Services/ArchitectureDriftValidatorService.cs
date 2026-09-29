namespace Cadence.Cli.Services;

public static class ArchitectureDriftValidatorService
{
    public static bool ValidateArchitectureDrift()
    {
        return Cadence.ToolingCore.Services.ArchitectureDriftValidatorService.ValidateArchitectureDrift() == 0;
    }
}
