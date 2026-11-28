namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The eTMF module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddEtmfGrants()
    {
        // Sponsor data management and system administrators manage full eTMF lifecycle transitions, EDLs, and redaction.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(
                role,
                "etmf_document",
                "tag",
                "create",
                "read",
                "read_raw",
                "redact",
                "sign",
                "transition_technical_qc",
                "transition_clinical_qc",
                "transition_approved",
                "transition_archived",
                "transition_rejected",
                "transition_draft",
                "transition_signed",
                "manage_expiration");
            Grant(role, "etmf_edl", "read", "create");
            Grant(role, "etmf_taxonomy", "read");
        }

        // CRAs and monitors stage, redact, sign, and perform clinical QC on eTMF documents and maintain EDLs.
        foreach (string role in new[] { RoleNormalization.ROLE_CRA_CANONICAL, "monitor" })
        {
            Grant(role, "etmf_document", "tag", "create", "read", "redact", "sign", "transition_clinical_qc");
            Grant(role, "etmf_edl", "read", "create");
            Grant(role, "etmf_taxonomy", "read");
        }

        // Site roles and external/audit roles read eTMF documents, EDLs, and taxonomy.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_INVESTIGATOR,
            RoleNormalization.ROLE_CRC,
            RoleNormalization.ROLE_TRIAL_COORDINATOR,
            RoleNormalization.ROLE_AUDITOR_CANONICAL,
            RoleNormalization.ROLE_EXTERNAL_MONITOR,
        })
        {
            Grant(role, "etmf_document", "read");
            Grant(role, "etmf_edl", "read");
            Grant(role, "etmf_taxonomy", "read");
        }

        // System administrators, regulatory auditors, and external monitors read eTMF audit logs.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_AUDITOR_CANONICAL, RoleNormalization.ROLE_EXTERNAL_MONITOR,
        })
        {
            Grant(role, "etmf_audit_logs", "read");
        }

        // Site coordinators file and stage site documents for CRA QC; CRAs and monitors already tag, create and run
        // clinical QC, and data managers run the later QC transitions.
        Grant(RoleNormalization.ROLE_CRC, "etmf_document", "tag", "create");

        // Exporting a study binder copies every document in it, so it is its own permission: sponsor data management,
        // monitors and inspection staff, never site roles.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_SPONSOR_DM, RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_AUDITOR_CANONICAL,
        })
        {
            Grant(role, "etmf_document", "export");
        }
    }
}
