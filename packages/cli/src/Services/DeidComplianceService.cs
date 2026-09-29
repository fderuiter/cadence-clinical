namespace Cadence.Cli.Services;

public static class DeidComplianceService
{
    public static bool ScanDeidCompliance(string[]? targetFiles = null)
    {
        return Cadence.ToolingCore.Services.DeidComplianceService.ScanDeidCompliance(targetFiles) == 0;
    }
}
