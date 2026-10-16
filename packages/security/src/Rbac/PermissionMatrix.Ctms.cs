namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The CTMS module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddCtmsGrants()
    {
        // Site start-up greenlight: sponsor operations certify a site ready to screen; monitors and site staff read it.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "ctms_greenlight", "read", "certify");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC,
            RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "ctms_greenlight", "read");
        }
    }
}
