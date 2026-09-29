namespace Cadence.Cli.Services;

public static class SecurityAuditService
{
    public static bool AuditSecurity(string[]? targetFiles = null)
    {
        return Cadence.ToolingCore.Services.SecurityAuditorService.AuditSecurity(targetFiles) == 0;
    }
}
