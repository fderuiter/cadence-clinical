namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The Tickets module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddTicketsGrants()
    {
        // Operational and clinical staff may log and list support tickets within their authorized study/site scope.
        // Auditors remain strictly read-only (AuditorWriteGuardMiddleware), and participants (subject) hold neither.
        foreach (string role in OperationalStaffRoles)
        {
            Grant(role, "tickets", "read", "create", "comment", "transition", "sign");
        }

        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "tickets", "read");
    }
}
