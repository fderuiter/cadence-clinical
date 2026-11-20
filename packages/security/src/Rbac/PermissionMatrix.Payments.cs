namespace Cadence.Security.Rbac;

public static partial class PermissionMatrix
{
    /// <summary>
    /// The Payments module's route permissions (ADR-2510, ADR-2516, #5361). Finance staff move and settle money, site
    /// staff act for their participants, the sponsor's data manager and auditors read, the system administrator reads
    /// what operations needs, and authenticated trial participants (<c>subject</c>) hold narrow self-service permissions
    /// strictly for their own signed-caller-bound wallet, receipt OCR, expense claims, and server-rule diary stipends.
    /// </summary>
    private static partial void AddPaymentsGrants()
    {
        string finance = RoleNormalization.ROLE_FINANCE_OFFICER;

        // Money out, money owed and its configuration are finance actions: stipend and wallet payouts, stipend rules,
        // invoice compilation, approval, remittance signing, reconciliation and the ERP export. The ERP export route is
        // also how an ERP integration account pushes an invoice; that account signs in with the finance role.
        // Approval and remittance signing share the role, so the handler refuses a signature from the invoice's approver.
        Grant(finance, "payments_stipend", "disburse");
        Grant(finance, "payments_wallet", "read", "issue_card", "disburse");
        Grant(finance, "payments_stipend_rule", "read", "configure");
        Grant(finance, "payments_expense", "read");
        Grant(finance, "payments_invoice", "read", "compile", "approve", "sign_remittance", "reconcile", "export_erp");
        Grant(finance, "payments_audit_log", "read");

        // Site staff act for the participants at their site: they record verified visits (which credit stipends under
        // the rules finance configured), issue cards, take and verify W-9s, and submit and review expense claims.
        foreach (string role in new[] { RoleNormalization.ROLE_CRC, RoleNormalization.ROLE_INVESTIGATOR })
        {
            Grant(role, "payments_visit_event", "record");
            Grant(role, "payments_wallet", "read", "issue_card");
            Grant(role, "payments_w9", "submit", "verify");
            Grant(role, "payments_stipend_rule", "read");
            Grant(role, "payments_expense", "read", "submit", "review");
        }

        // The sponsor's data manager reads participant payments and site invoices; auditors read everything, including
        // the payments audit log.
        Grant(RoleNormalization.ROLE_SPONSOR_DM, "payments_wallet", "read");
        Grant(RoleNormalization.ROLE_SPONSOR_DM, "payments_stipend_rule", "read");
        Grant(RoleNormalization.ROLE_SPONSOR_DM, "payments_expense", "read");
        Grant(RoleNormalization.ROLE_SPONSOR_DM, "payments_invoice", "read");

        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "payments_wallet", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "payments_stipend_rule", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "payments_expense", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "payments_invoice", "read");
        Grant(RoleNormalization.ROLE_AUDITOR_CANONICAL, "payments_audit_log", "read");

        // Operations: invoice and rule configuration status and the audit log, for troubleshooting; no participant
        // wallets or claims, and no money movement.
        Grant(RoleNormalization.ROLE_SYSADMIN, "payments_invoice", "read");
        Grant(RoleNormalization.ROLE_SYSADMIN, "payments_stipend_rule", "read");
        Grant(RoleNormalization.ROLE_SYSADMIN, "payments_audit_log", "read");

        // Participant self-service: strictly bound to the signed caller's own SubjectId on /participant/me/* routes.
        // Participants hold zero staff-scoped permissions (payments_wallet:*, payments_expense:*, payments_w9:*, etc.).
        Grant(RoleNormalization.ROLE_SUBJECT, "payments_self_wallet", "read", "disburse");
        Grant(RoleNormalization.ROLE_SUBJECT, "payments_self_expense", "ocr_scan", "submit");
        Grant(RoleNormalization.ROLE_SUBJECT, "payments_self_stipend", "credit");
    }
}
