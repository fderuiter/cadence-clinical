namespace Cadence.Cli.Services;

public static class VulnerabilitiesValidatorService
{
    public static bool ValidateVulnerabilities()
    {
        return Cadence.ToolingCore.Services.VulnerabilityAuditorService.AuditVulnerabilities() == 0;
    }
}
