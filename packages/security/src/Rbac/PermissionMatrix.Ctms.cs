namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The CTMS module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddCtmsGrants()
    {
        // Site start-up greenlight: sponsor operations certify a site ready to screen; operations and monitors initialize/update gates; site staff read it.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "ctms_greenlight", "read", "write", "certify");
        }

        foreach (string role in new[] { RoleNormalization.ROLE_CRA_CANONICAL, "monitor" })
        {
            Grant(role, "ctms_greenlight", "read", "write");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC,
            RoleNormalization.ROLE_SPONSOR_MM, RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "ctms_greenlight", "read");
        }

        // Site milestones and monitoring visits/letters: site staff and medical monitors read scoped records within their
        // assigned studies and sites; monitors complete and sign off monitoring visits; CRAs and sponsor DMs schedule visits.
        Grant(RoleNormalization.ROLE_SPONSOR_MM, "ctms_site_milestone", "read");
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_SPONSOR_MM,
        })
        {
            Grant(role, "ctms_monitoring_visit", "read");
            Grant(role, "ctms_monitoring_letter", "read", "read_type");
        }

        Grant("monitor", "ctms_monitoring_visit", "update");

        // Delegation of Authority (DOA)
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SYSADMIN,
            RoleNormalization.ROLE_INVESTIGATOR,
            RoleNormalization.ROLE_CRC,
            RoleNormalization.ROLE_CRA_CANONICAL,
            "monitor",
            RoleNormalization.ROLE_SPONSOR_DM,
        })
        {
            Grant(role, "ctms_doa", "read", "assign", "create", "revoke", "update", "export");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SYSADMIN,
            RoleNormalization.ROLE_INVESTIGATOR,
        })
        {
            Grant(role, "ctms_doa", "sign_off", "sign");
        }

        foreach (string role in new[] { RoleNormalization.ROLE_AUDITOR_CANONICAL, RoleNormalization.ROLE_SPONSOR_MM })
        {
            Grant(role, "ctms_doa", "read", "export");
        }
    }
}
