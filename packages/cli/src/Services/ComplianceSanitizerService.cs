namespace Cadence.Cli.Services;

public static class ComplianceSanitizerService
{
    public static bool SanitizeComplianceTags(string[]? targetFiles = null)
    {
        return Cadence.ToolingCore.Services.ComplianceUtilityService.ExecuteComplianceUtility(sanitize: true, targetFiles: targetFiles) == 0;
    }
}
