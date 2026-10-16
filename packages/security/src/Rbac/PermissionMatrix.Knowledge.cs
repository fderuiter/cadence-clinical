namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The Knowledge module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddKnowledgeGrants()
    {
        // Rolling a training curriculum out to every site of a study is a sponsor action; site staff record their own
        // training through training_log.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "training_curriculum", "assign");
        }

        // The delegation-of-authority log is read by the site team and by the people who monitor or inspect it; only
        // principal investigators sign it (PermissionMatrix.cs).
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_CRA_CANONICAL, "monitor",
            RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "delegation_log", "read");
        }
    }
}
