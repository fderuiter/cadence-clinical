namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>The CTMS module's route permissions (ADR-2510, ADR-2516).</summary>
    private static partial void AddCtmsGrants()
    {
        // Sponsor operations and system administrators manage study-wide CTMS records, budgets, payables, and monitoring.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "ctms_study", "create", "read");
            Grant(role, "ctms_audit_logs", "read");
            Grant(role, "ctms_monitoring_visit", "create", "update", "read", "sign_off", "sync");
            Grant(role, "ctms_monitoring_letter", "read", "read_type");
            Grant(role, "ctms_recruitment", "create", "read");
            Grant(role, "ctms_site_milestone", "create", "update", "read");
            Grant(role, "ctms_cra_allocation", "create", "update", "read");
            Grant(role, "ctms_cra_workload", "read");
            Grant(role, "ctms_financial", "create", "read", "update", "write");
            Grant(role, "ctms_financial_budget", "create", "read");
            Grant(role, "ctms_financial_milestone", "create", "read", "trigger");
            Grant(role, "ctms_financial_payable", "read");
        }

        // Site clinical staff read scoped CTMS study, recruitment, milestone, and monitor allocation summaries.
        foreach (string role in new[]
        {
            RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_TRIAL_COORDINATOR,
        })
        {
            Grant(role, "ctms_study", "read");
            Grant(role, "ctms_recruitment", "read");
            Grant(role, "ctms_site_milestone", "read");
            Grant(role, "ctms_cra_allocation", "read");
            Grant(role, "ctms_cra_workload", "read");
        }

        // Field monitors and CRAs create and track studies, recruitment, milestones, and monitoring visits.
        foreach (string role in new[] { RoleNormalization.ROLE_CRA_CANONICAL, "monitor" })
        {
            Grant(role, "ctms_study", "create", "read");
            Grant(role, "ctms_monitoring_letter", "read", "read_type");
            Grant(role, "ctms_recruitment", "create", "read");
            Grant(role, "ctms_site_milestone", "create", "update", "read");
            Grant(role, "ctms_cra_allocation", "read");
            Grant(role, "ctms_cra_workload", "read");
        }

        Grant(RoleNormalization.ROLE_CRA_CANONICAL, "ctms_monitoring_visit", "create", "update", "read", "sync");
        Grant("monitor", "ctms_monitoring_visit", "read", "update", "sign_off", "sync");

        // Regulatory auditors inspect all CTMS operational, audit, and financial records read-only.
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_study", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_audit_logs", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_monitoring_visit", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_monitoring_letter", "read", "read_type");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_recruitment", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_site_milestone", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_cra_allocation", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_cra_workload", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_financial", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_financial_budget", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_financial_milestone", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "ctms_financial_payable", "read");

        // Site start-up greenlight: sponsor operations certify a site ready to screen; monitors and site staff read it.
        foreach (string role in new[] { RoleNormalization.ROLE_SYSADMIN, RoleNormalization.ROLE_SPONSOR_DM })
        {
            Grant(role, "ctms_greenlight", "read", "certify");
        }

        foreach (string role in new[]
        {
            RoleNormalization.ROLE_CRA_CANONICAL, "monitor", RoleNormalization.ROLE_INVESTIGATOR, RoleNormalization.ROLE_CRC,
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

        // Only sysadmin and Principal Investigator roles (cloned via piBase in PermissionMatrix.cs) may sign DOA logs;
        // sub-investigators normalize to ROLE_INVESTIGATOR and must not inherit PI sign-off authority (ADR-2648).
        Grant(RoleNormalization.ROLE_SYSADMIN, "ctms_doa", "sign_off", "sign");

        foreach (string role in new[] { RoleNormalization.ROLE_AUDITOR_CANONICAL, RoleNormalization.ROLE_SPONSOR_MM })
        {
            Grant(role, "ctms_doa", "read", "export");
        }
    }
}
