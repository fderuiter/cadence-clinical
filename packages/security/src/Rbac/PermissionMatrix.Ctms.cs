namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The role the CTMS service's signed caller carries when filing GxP artifacts to eTMF or checking readiness.</summary>
    public const string CtmsServiceRole = "ctms_service";

    /// <summary>The role the Knowledge service's signed caller carries when forwarding verified PI bulk DoA endorsements to CTMS.</summary>
    public const string KnowledgeServiceRole = "knowledge_service";

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
            RoleNormalization.ROLE_PHARMACIST, RoleNormalization.ROLE_SPONSOR_MM, RoleNormalization.ROLE_AUDITOR_CANONICAL,
            RoleNormalization.ROLE_EXTERNAL_MONITOR,
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
            RoleNormalization.ROLE_EXTERNAL_MONITOR,
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

        Grant(RoleNormalization.ROLE_PHARMACIST, "ctms_doa", "read");
        Grant(RoleNormalization.ROLE_EXTERNAL_MONITOR, "ctms_doa", "read", "export");

        // Only sysadmin and Principal Investigator roles (cloned via piBase in PermissionMatrix.cs) may sign DOA logs;
        // sub-investigators normalize to ROLE_INVESTIGATOR and must not inherit PI sign-off authority (ADR-2648).
        Grant(RoleNormalization.ROLE_SYSADMIN, "ctms_doa", "sign_off", "sign");

        // Signed service-to-service forwarding from Knowledge after Knowledge consumes the PI's single-use X-Sig-Token.
        Grant(KnowledgeServiceRole, "ctms_doa", "read", "service_endorse");

        foreach (string role in new[] { RoleNormalization.ROLE_AUDITOR_CANONICAL, RoleNormalization.ROLE_SPONSOR_MM })
        {
            Grant(role, "ctms_doa", "read", "export");
        }
    }
}
