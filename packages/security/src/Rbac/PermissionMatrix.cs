using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Security.Rbac;

/// <summary>
/// Platform RBAC permission matrix mapping canonical roles to allowed resource-action sets.
/// @req:PRD-SYS-053
/// </summary>
public static partial class PermissionMatrix
{
    private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> _rolePermissions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Canonical operational and clinical staff roles permitted to read SOP guidance and log/view support tickets
    /// (excludes read-only auditors and study participants).
    /// </summary>
    private static readonly string[] OperationalStaffRoles =
    [
        RoleNormalization.ROLE_SYSADMIN,
        RoleNormalization.ROLE_SPONSOR_DESIGNER,
        RoleNormalization.ROLE_REVIEWER,
        RoleNormalization.ROLE_SPONSOR_DM,
        RoleNormalization.ROLE_SPONSOR_MM,
        RoleNormalization.ROLE_SPONSOR_STATISTICIAN,
        RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
        RoleNormalization.ROLE_IDMC,
        RoleNormalization.ROLE_INVESTIGATOR,
        RoleNormalization.ROLE_CRC,
        RoleNormalization.ROLE_TRIAL_COORDINATOR,
        RoleNormalization.ROLE_CRA_CANONICAL,
        "monitor",
        RoleNormalization.ROLE_EXTERNAL_MONITOR,
        RoleNormalization.ROLE_PHARMACIST,
        RoleNormalization.ROLE_FINANCE_OFFICER,
        "terminology_manager",
    ];

    public static IReadOnlyDictionary<string, Dictionary<string, HashSet<string>>> ROLE_PERMISSIONS => _rolePermissions;

    static PermissionMatrix()
    {
        // sysadmin
        _rolePermissions[RoleNormalization.ROLE_SYSADMIN] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["safety_report"] = ["create", "read"],
            ["notification"] = ["send"],
            ["visit_windowing"] = ["create", "read", "update"],
            ["soa"] = ["create", "read", "update", "delete"],
            ["study_design"] = ["create", "read", "update", "delete", "approve", "reorder"],
            ["global_library"] = ["create", "update", "amend", "transition", "instantiate", "read"],
            ["library_object"] = ["create", "read", "update", "delete", "approve", "publish", "release"],
            ["mdr_concept"] = ["create", "update", "rename", "delete", "read"],
            ["protocol_export"] = ["generate", "read"],
            ["designer_cache"] = ["admin"],
            ["system_audit_logs"] = ["read"],
            ["export_masked"] = ["read"],
            ["protocol_ingestion"] = ["upload", "read", "review", "promote"],
            ["protocol_section"] = ["lock", "unlock", "approve", "review", "read"],
            ["protocol_version"] = ["sign", "transition_approved"],
            ["regulatory_form"] = ["create", "read", "sign"],
            ["training_log"] = ["create", "read", "sign"],
            ["ctms_study"] = ["create", "read"],
            ["ctms_audit_logs"] = ["read"],
            ["ctms_monitoring_visit"] = ["create", "update", "read", "sign_off", "sync"],
            ["ctms_monitoring_letter"] = ["read", "read_type"],
            ["ctms_recruitment"] = ["create", "read"],
            ["ctms_site_milestone"] = ["create", "update", "read"],
            ["ctms_cra_allocation"] = ["create", "update", "read"],
            ["ctms_cra_workload"] = ["read"],
            ["ctms_financial"] = ["create", "read", "update", "write"],
            ["ctms_financial_budget"] = ["create", "read"],
            ["ctms_financial_milestone"] = ["create", "read", "trigger"],
            ["ctms_financial_payable"] = ["read"],
            ["etmf_document"] = ["tag", "create", "read", "read_raw", "redact", "sign", "transition_technical_qc", "transition_clinical_qc", "transition_approved", "transition_archived", "transition_rejected", "transition_draft", "transition_signed", "manage_expiration"],
            ["etmf_edl"] = ["read", "create"],
            ["etmf_taxonomy"] = ["read"],
            ["etmf_audit_logs"] = ["read"],
            ["quality_event"] = ["create", "read", "update", "delete", "investigate"],
            ["quality_audit_logs"] = ["read"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["medical_coding"] = ["create", "read", "update"],
            ["lab_range"] = ["create", "read", "update", "delete", "alert"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // sponsor_designer
        _rolePermissions[RoleNormalization.ROLE_SPONSOR_DESIGNER] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["visit_windowing"] = ["create", "read", "update"],
            ["soa"] = ["create", "read", "update", "delete"],
            ["study_design"] = ["create", "read", "update", "delete", "approve", "reorder"],
            ["global_library"] = ["create", "update", "amend", "transition", "instantiate", "read"],
            ["library_object"] = ["create", "read", "update", "delete"],
            ["mdr_concept"] = ["create", "update", "rename", "delete", "read"],
            ["protocol_export"] = ["generate", "read"],
            ["designer_cache"] = ["admin"],
            ["system_audit_logs"] = ["read"],
            ["protocol_ingestion"] = ["upload", "read", "review", "promote"],
            ["protocol_version"] = ["sign", "transition_approved"],
            ["protocol_section"] = ["lock", "unlock", "approve", "review", "read"],
            ["regulatory_form"] = ["read"],
            ["training_log"] = ["read"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // protocol_reviewer
        _rolePermissions[RoleNormalization.ROLE_REVIEWER] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["protocol_ingestion"] = ["upload", "read", "review", "promote"],
            ["protocol_section"] = ["review", "read"]
        };

        // sponsor_dm
        _rolePermissions[RoleNormalization.ROLE_SPONSOR_DM] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["notification"] = ["send"],
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read", "approve"],
            ["global_library"] = ["transition", "read"],
            ["library_object"] = ["read", "approve", "publish"],
            ["mdr_concept"] = ["read"],
            ["protocol_export"] = ["generate", "read"],
            ["subject_enrollment"] = ["read"],
            ["ecrf_data_entry"] = ["read"],
            ["system_audit_logs"] = ["read"],
            ["export_masked"] = ["create", "read", "update"],
            ["protocol_version"] = ["transition_approved"],
            ["protocol_section"] = ["read"],
            ["regulatory_form"] = ["create", "read", "sign"],
            ["training_log"] = ["create", "read", "sign"],
            ["ctms_study"] = ["create", "read"],
            ["ctms_audit_logs"] = ["read"],
            ["ctms_monitoring_visit"] = ["create", "update", "read", "sign_off", "sync"],
            ["ctms_monitoring_letter"] = ["read", "read_type"],
            ["ctms_recruitment"] = ["create", "read"],
            ["ctms_site_milestone"] = ["create", "update", "read"],
            ["ctms_cra_allocation"] = ["create", "update", "read"],
            ["ctms_cra_workload"] = ["read"],
            ["ctms_financial"] = ["create", "read", "update", "write"],
            ["ctms_financial_budget"] = ["create", "read"],
            ["ctms_financial_milestone"] = ["create", "read", "trigger"],
            ["ctms_financial_payable"] = ["read"],
            ["etmf_document"] = ["tag", "create", "read", "read_raw", "redact", "sign", "transition_technical_qc", "transition_clinical_qc", "transition_approved", "transition_archived", "transition_rejected", "transition_draft", "transition_signed", "manage_expiration"],
            ["etmf_edl"] = ["read", "create"],
            ["etmf_taxonomy"] = ["read"],
            ["quality_event"] = ["create", "read", "update", "delete", "investigate"],
            ["quality_audit_logs"] = ["read"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["medical_coding"] = ["create", "read", "update"],
            ["lab_range"] = ["create", "read", "update", "delete", "alert"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"],
            // SCE: issues and revokes database-lock tokens; cannot run or read unblinded analyses (#5382)
            ["sce_lock_token"] = ["read", "issue", "revoke"]
        };

        // sponsor_mm
        _rolePermissions[RoleNormalization.ROLE_SPONSOR_MM] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["safety_report"] = ["create", "read"],
            ["notification"] = ["send"],
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["subject_enrollment"] = ["read"],
            ["ecrf_data_entry"] = ["read"],
            ["observation"] = ["review"],
            ["system_audit_logs"] = ["read"],
            ["export_masked"] = ["read"],
            ["eisf_document"] = ["read"],
            ["regulatory_form"] = ["read"],
            ["training_log"] = ["read"]
        };

        // sponsor_statistician
        _rolePermissions[RoleNormalization.ROLE_SPONSOR_STATISTICIAN] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["system_audit_logs"] = ["read"],
            ["export_masked"] = ["create", "read", "update"],
            ["eisf_document"] = ["read"],
            ["regulatory_form"] = ["read"],
            ["training_log"] = ["read"],
            // SCE: blinded programs and runs only (#5382)
            ["sce_program"] = ["create", "read"],
            ["sce_run"] = ["execute", "read"]
        };

        // investigator
        _rolePermissions[RoleNormalization.ROLE_INVESTIGATOR] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["subject_enrollment"] = ["create", "read", "update"],
            ["rtsm_unblind"] = ["write"],
            ["ecrf_data_entry"] = ["create", "read", "update"],
            ["sdv"] = ["read"],
            ["lab_range"] = ["read", "alert"],
            ["system_audit_logs"] = ["read"],
            ["regulatory_form"] = ["create", "read", "sign"],
            ["training_log"] = ["create", "read", "sign"],
            ["ctms_study"] = ["read"],
            ["ctms_recruitment"] = ["read"],
            ["ctms_site_milestone"] = ["read"],
            ["ctms_cra_allocation"] = ["read"],
            ["ctms_cra_workload"] = ["read"],
            ["etmf_document"] = ["read"],
            ["etmf_edl"] = ["read"],
            ["etmf_taxonomy"] = ["read"],
            ["quality_event"] = ["read"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // crc
        _rolePermissions[RoleNormalization.ROLE_CRC] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["subject_enrollment"] = ["create", "read", "update"],
            ["ecrf_data_entry"] = ["create", "read", "update"],
            ["lab_range"] = ["read", "alert"],
            ["system_audit_logs"] = ["read"],
            ["regulatory_form"] = ["create", "read", "sign"],
            ["training_log"] = ["create", "read", "sign"],
            ["ctms_study"] = ["read"],
            ["ctms_recruitment"] = ["read"],
            ["ctms_site_milestone"] = ["read"],
            ["ctms_cra_allocation"] = ["read"],
            ["ctms_cra_workload"] = ["read"],
            ["etmf_document"] = ["read"],
            ["etmf_edl"] = ["read"],
            ["etmf_taxonomy"] = ["read"],
            ["quality_event"] = ["read"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // trial_coordinator (self-service trial workspace coordinator; write authority restricted to trial-* tenants)
        _rolePermissions[RoleNormalization.ROLE_TRIAL_COORDINATOR] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["subject_enrollment"] = ["create", "read", "update"],
            ["ecrf_data_entry"] = ["create", "read", "update"],
            ["lab_range"] = ["read", "alert"],
            ["system_audit_logs"] = ["read"],
            ["regulatory_form"] = ["create", "read", "sign"],
            ["training_log"] = ["create", "read", "sign"],
            ["ctms_study"] = ["read"],
            ["ctms_recruitment"] = ["read"],
            ["ctms_site_milestone"] = ["read"],
            ["ctms_cra_allocation"] = ["read"],
            ["ctms_cra_workload"] = ["read"],
            ["etmf_document"] = ["read"],
            ["etmf_edl"] = ["read"],
            ["etmf_taxonomy"] = ["read"],
            ["quality_event"] = ["read"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // cra
        _rolePermissions[RoleNormalization.ROLE_CRA_CANONICAL] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["notification"] = ["send"],
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["subject_enrollment"] = ["read"],
            ["ecrf_data_entry"] = ["read"],
            ["sdv"] = ["create", "read", "update", "delete", "flag"],
            ["system_audit_logs"] = ["read"],
            ["export_masked"] = ["read"],
            ["regulatory_form"] = ["create", "read", "sign"],
            ["training_log"] = ["create", "read", "sign"],
            ["ctms_study"] = ["create", "read"],
            ["ctms_monitoring_visit"] = ["create", "update", "read", "sync"],
            ["ctms_monitoring_letter"] = ["read", "read_type"],
            ["ctms_recruitment"] = ["create", "read"],
            ["ctms_site_milestone"] = ["create", "update", "read"],
            ["ctms_cra_allocation"] = ["read"],
            ["ctms_cra_workload"] = ["read"],
            ["etmf_document"] = ["tag", "create", "read", "redact", "sign", "transition_clinical_qc"],
            ["etmf_edl"] = ["read", "create"],
            ["etmf_taxonomy"] = ["read"],
            ["quality_event"] = ["create", "read", "update"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["medical_coding"] = ["read"],
            ["lab_range"] = ["create", "read", "update", "delete", "alert"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // monitor
        _rolePermissions["monitor"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["notification"] = ["send"],
            ["visit_windowing"] = ["read"],
            ["soa"] = ["read"],
            ["study_design"] = ["read"],
            ["sdv"] = ["create", "read", "update", "delete", "flag"],
            ["system_audit_logs"] = ["read"],
            ["regulatory_form"] = ["create", "read", "sign"],
            ["training_log"] = ["create", "read", "sign"],
            ["ctms_study"] = ["create", "read"],
            ["ctms_monitoring_visit"] = ["read", "sign_off", "sync"],
            ["ctms_monitoring_letter"] = ["read", "read_type"],
            ["ctms_recruitment"] = ["create", "read"],
            ["ctms_site_milestone"] = ["create", "update", "read"],
            ["ctms_cra_allocation"] = ["read"],
            ["ctms_cra_workload"] = ["read"],
            ["etmf_document"] = ["tag", "create", "read", "redact", "sign", "transition_clinical_qc"],
            ["etmf_edl"] = ["read", "create"],
            ["etmf_taxonomy"] = ["read"],
            ["quality_event"] = ["create", "read", "update"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["lab_range"] = ["create", "read", "update", "delete", "alert"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // subject
        // A participant reaches only their own ePRO/eCOA; site eCRF data entry is site work (ADR-2516).
        _rolePermissions[RoleNormalization.ROLE_SUBJECT] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ecoa_schedule"] = ["read"],
            ["ecoa_diary"] = ["read"],
            ["ecoa_submission"] = ["create", "read"]
        };

        // auditor
        _rolePermissions[RoleNormalization.ROLE_AUDITOR_CANONICAL] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["soa"] = ["read"],
            ["system_audit_logs"] = ["read"],
            ["regulatory_form"] = ["read"],
            ["training_log"] = ["read"],
            ["ctms_study"] = ["read"],
            ["ctms_audit_logs"] = ["read"],
            ["ctms_monitoring_visit"] = ["read"],
            ["ctms_monitoring_letter"] = ["read", "read_type"],
            ["ctms_recruitment"] = ["read"],
            ["ctms_site_milestone"] = ["read"],
            ["ctms_cra_allocation"] = ["read"],
            ["ctms_cra_workload"] = ["read"],
            ["ctms_financial"] = ["read"],
            ["ctms_financial_budget"] = ["read"],
            ["ctms_financial_milestone"] = ["read"],
            ["ctms_financial_payable"] = ["read"],
            ["etmf_document"] = ["read"],
            ["etmf_edl"] = ["read"],
            ["etmf_taxonomy"] = ["read"],
            ["etmf_audit_logs"] = ["read"],
            ["quality_event"] = ["read"],
            ["quality_audit_logs"] = ["read"],
            ["eisf_document"] = ["read"]
        };

        // external_monitor
        _rolePermissions[RoleNormalization.ROLE_EXTERNAL_MONITOR] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["etmf_document"] = ["read"],
            ["etmf_edl"] = ["read"],
            ["etmf_taxonomy"] = ["read"],
            ["etmf_audit_logs"] = ["read"],
            ["eisf_document"] = ["read"],
            ["regulatory_form"] = ["read"],
            ["training_log"] = ["read"]
        };

        // admin
        _rolePermissions["admin"] = _rolePermissions[RoleNormalization.ROLE_SPONSOR_DM];

        // RTSM specific roles
        _rolePermissions[RoleNormalization.ROLE_UNBLINDED_STATISTICIAN] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rtsm_randomization"] = ["read"],
            ["rtsm_allocation"] = ["read"],
            // SCE: runs unblinded analyses with a lock token issued to them, and reads unblinded runs (#5382)
            ["sce_program"] = ["create", "read"],
            ["sce_lock_token"] = ["read"],
            ["sce_run"] = ["execute", "read", "execute_unblinded", "read_unblinded"]
        };

        _rolePermissions[RoleNormalization.ROLE_IDMC] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rtsm_randomization"] = ["read"],
            ["rtsm_allocation"] = ["read"],
            // SCE: reviews unblinded runs; does not run them (#5382)
            ["sce_program"] = ["read"],
            ["sce_run"] = ["read", "read_unblinded"]
        };

        _rolePermissions[RoleNormalization.ROLE_PHARMACIST] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rtsm_supply"] = ["read", "write"]
        };

        _rolePermissions[RoleNormalization.ROLE_EMERGENCY_UNBLINDER] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rtsm_unblind"] = ["write"]
        };

        _rolePermissions["terminology_manager"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["medical_coding"] = ["create", "read", "update"]
        };

        // Module permission catalogs (PermissionMatrix.<Module>.cs), before the investigator is cloned into the PI roles.
        AddModuleGrants();

        // Derived PI / ER Physician / Lead Investigator
        var piBase = ClonePermissions(_rolePermissions[RoleNormalization.ROLE_INVESTIGATOR]);
        piBase["rtsm_unblind"] = ["write"];
        piBase["eisf_document"] = ["create", "read", "update", "delete", "sync"];
        // Only the PI (and those acting as PI) signs the site delegation log; a sub-investigator does not (#5311, ADR-2648).
        piBase["delegation_log"] = ["read", "sign"];
        piBase["ctms_doa"].UnionWith(["sign_off", "sign"]);

        _rolePermissions[RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR] = ClonePermissions(piBase);
        _rolePermissions[RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN] = ClonePermissions(piBase);
        _rolePermissions[RoleNormalization.ROLE_LEAD_INVESTIGATOR] = ClonePermissions(piBase);
    }

    /// <summary>
    /// Grants <paramref name="role"/> the <paramref name="actions"/> on <paramref name="resource"/>, adding to what it
    /// already holds. Module catalogs call this from their part of <see cref="AddModuleGrants"/>.
    /// </summary>
    private static void Grant(string role, string resource, params string[] actions)
    {
        if (!_rolePermissions.TryGetValue(role, out var permissions))
        {
            permissions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            _rolePermissions[role] = permissions;
        }

        if (!permissions.TryGetValue(resource, out var granted))
        {
            granted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            permissions[resource] = granted;
        }

        granted.UnionWith(actions);
    }

    private static void AddModuleGrants()
    {
        AddExecutionGrants();
        AddPaymentsGrants();
        AddInteropGrants();
        AddEtmfGrants();
        AddImagingGrants();
        AddEconsentGrants();
        AddKnowledgeGrants();
        AddTicketsGrants();
        AddCtmsGrants();
        AddSafetyGrants();
    }

    private static partial void AddExecutionGrants();

    private static partial void AddPaymentsGrants();

    private static partial void AddInteropGrants();

    private static partial void AddEtmfGrants();

    private static partial void AddImagingGrants();

    private static partial void AddEconsentGrants();

    private static partial void AddKnowledgeGrants();

    private static partial void AddTicketsGrants();

    private static partial void AddCtmsGrants();

    private static partial void AddSafetyGrants();

    /// <summary>
    /// True when at least one role holds <paramref name="permission"/> (<c>resource:action</c>). A route permission no
    /// role holds is a typo or a missing grant, so <see cref="PermissionPolicyProvider"/> refuses it (ADR-2516).
    /// </summary>
    public static bool IsDefined(string permission)
    {
        if (!TrySplit(permission, out string resource, out string action))
        {
            return false;
        }

        return _rolePermissions.Values.Any(perms => perms.TryGetValue(resource, out var actions) && actions.Contains(action));
    }

    private static bool TrySplit(string? permission, out string resource, out string action)
    {
        resource = action = string.Empty;
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        int colonIndex = permission.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= 0 || colonIndex >= permission.Length - 1)
        {
            return false;
        }

        resource = permission[..colonIndex].Trim();
        action = permission[(colonIndex + 1)..].Trim();
        return true;
    }

    private static Dictionary<string, HashSet<string>> ClonePermissions(Dictionary<string, HashSet<string>> source)
    {
        var clone = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            clone[kvp.Key] = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
        }
        return clone;
    }

    /// <summary>
    /// Checks if a principal possesses the specified permission string ("resource:action").
    /// </summary>
    public static bool HasPermission(Principal? principal, string permission)
    {
        if (principal == null || !TrySplit(permission, out string resource, out string action)) return false;

        var rolesToCheck = new List<string>(principal.Roles);
        foreach (string r in principal.RawRoles)
        {
            string norm = RoleNormalization.NormalizeRole(r);
            if (!rolesToCheck.Contains(norm, StringComparer.OrdinalIgnoreCase))
            {
                rolesToCheck.Add(norm);
            }
            if ((string.Equals(norm, "admin", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(norm, "sponsor_admin", StringComparison.OrdinalIgnoreCase)) &&
                !rolesToCheck.Contains("admin", StringComparer.OrdinalIgnoreCase))
            {
                rolesToCheck.Add("admin");
            }
        }

        foreach (string role in rolesToCheck)
        {
            if (_rolePermissions.TryGetValue(role, out var perms) && perms.TryGetValue(resource, out var actions) && actions.Contains(action))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Dictionary<string, string> RoleDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR] = "Principal Investigator",
        [RoleNormalization.ROLE_LEAD_INVESTIGATOR] = "Lead Investigator",
        [RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN] = "Authorized ER Physician",
        [RoleNormalization.ROLE_INVESTIGATOR] = "Site Investigator",
        [RoleNormalization.ROLE_CRC] = "Clinical Research Coordinator",
        [RoleNormalization.ROLE_TRIAL_COORDINATOR] = "Trial Coordinator",
        [RoleNormalization.ROLE_CRA_CANONICAL] = "Clinical Research Associate",
        ["monitor"] = "Clinical Monitor",
        [RoleNormalization.ROLE_EXTERNAL_MONITOR] = "External Monitor",
        [RoleNormalization.ROLE_SPONSOR_DM] = "Sponsor Data Manager",
        [RoleNormalization.ROLE_SPONSOR_DESIGNER] = "Sponsor Study Designer",
        [RoleNormalization.ROLE_REVIEWER] = "Protocol Reviewer",
        [RoleNormalization.ROLE_SPONSOR_MM] = "Sponsor Medical Monitor",
        [RoleNormalization.ROLE_SPONSOR_STATISTICIAN] = "Sponsor Statistician",
        [RoleNormalization.ROLE_UNBLINDED_STATISTICIAN] = "Unblinded Statistician",
        [RoleNormalization.ROLE_IDMC] = "IDMC Member",
        [RoleNormalization.ROLE_PHARMACIST] = "Study Pharmacist",
        [RoleNormalization.ROLE_EMERGENCY_UNBLINDER] = "Emergency Unblinder",
        [RoleNormalization.ROLE_FINANCE_OFFICER] = "Finance Officer",
        ["terminology_manager"] = "Terminology Manager",
        [RoleNormalization.ROLE_AUDITOR_CANONICAL] = "Regulatory Auditor",
        [RoleNormalization.ROLE_SUBJECT] = "Study Participant",
        [RoleNormalization.ROLE_SYSADMIN] = "System Administrator",
        ["admin"] = "Sponsor Data Manager",
    };

    private static readonly string[] PreferredRoleOrder =
    [
        RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR,
        RoleNormalization.ROLE_INVESTIGATOR,
        RoleNormalization.ROLE_CRC,
        RoleNormalization.ROLE_TRIAL_COORDINATOR,
        RoleNormalization.ROLE_CRA_CANONICAL,
        "monitor",
        RoleNormalization.ROLE_SPONSOR_DM,
        RoleNormalization.ROLE_SPONSOR_DESIGNER,
        RoleNormalization.ROLE_REVIEWER,
        RoleNormalization.ROLE_SPONSOR_MM,
        RoleNormalization.ROLE_SPONSOR_STATISTICIAN,
        RoleNormalization.ROLE_UNBLINDED_STATISTICIAN,
        RoleNormalization.ROLE_IDMC,
        RoleNormalization.ROLE_PHARMACIST,
        RoleNormalization.ROLE_EMERGENCY_UNBLINDER,
        RoleNormalization.ROLE_FINANCE_OFFICER,
        "terminology_manager",
        RoleNormalization.ROLE_EXTERNAL_MONITOR,
        RoleNormalization.ROLE_AUDITOR_CANONICAL,
        RoleNormalization.ROLE_SUBJECT,
        RoleNormalization.ROLE_LEAD_INVESTIGATOR,
        RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN,
        RoleNormalization.ROLE_SYSADMIN,
    ];

    private static readonly Dictionary<string, string> ExactPermissionPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["site_compliance:activate"] = "activate clinical sites for enrollment",
        ["site_compliance:read"] = "view clinical site activation readiness",
        ["site_compliance:write"] = "update clinical site compliance gates",
        ["delegation_log:sign"] = "co-sign the site delegation of authority log",
        ["delegation_log:read"] = "view the site delegation of authority log",
        ["delegation_log:write"] = "manage site delegation of authority assignments",
        ["training_curriculum:assign"] = "assign protocol training curricula",
        ["training_curriculum:write"] = "configure protocol training curricula",
        ["training_curriculum:read"] = "view protocol training curricula",
        ["etmf_document:create"] = "upload documents to the electronic Trial Master File",
        ["etmf_document:tag"] = "classify and tag electronic Trial Master File documents",
        ["etmf_document:redact"] = "redact protected health information in Trial Master File documents",
        ["etmf_document:sign"] = "apply an electronic signature to Trial Master File documents",
        ["etmf_document:transition_technical_qc"] = "complete technical quality review on Trial Master File documents",
        ["etmf_document:transition_clinical_qc"] = "complete clinical quality review on Trial Master File documents",
        ["etmf_document:transition_approved"] = "approve Trial Master File documents for filing",
        ["etmf_document:transition_rejected"] = "return Trial Master File documents from quality review",
        ["etmf_document:transition_archived"] = "archive Trial Master File documents",
        ["etmf_document:transition_draft"] = "return Trial Master File documents to draft status",
        ["etmf_document:transition_signed"] = "record signed status on Trial Master File documents",
        ["etmf_document:manage_expiration"] = "update Trial Master File document expiration dates",
        ["etmf_document:export"] = "export certified Trial Master File archives",
    };

    private static readonly Dictionary<string, string> ResourceNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["soa"] = "Schedule of Activities definitions",
        ["mdr_concept"] = "metadata repository concepts",
        ["sdv"] = "source data verification records",
        ["ecrf_data_entry"] = "electronic case report form entries",
        ["subject_enrollment"] = "participant enrollment records",
        ["ctms_study"] = "clinical trial management study records",
        ["ctms_audit_logs"] = "clinical trial management audit logs",
        ["ctms_monitoring_visit"] = "site monitoring visits",
        ["ctms_monitoring_letter"] = "site monitoring letters",
        ["ctms_recruitment"] = "site recruitment milestones",
        ["ctms_site_milestone"] = "clinical site milestones",
        ["ctms_cra_allocation"] = "monitor site allocations",
        ["ctms_cra_workload"] = "monitor workload summaries",
        ["ctms_financial"] = "site financial records",
        ["ctms_financial_budget"] = "study financial budgets",
        ["ctms_financial_milestone"] = "financial payment milestones",
        ["ctms_financial_payable"] = "site financial payables",
        ["etmf_document"] = "electronic Trial Master File documents",
        ["etmf_edl"] = "expected document lists",
        ["etmf_taxonomy"] = "Trial Master File taxonomy entries",
        ["etmf_audit_logs"] = "Trial Master File audit logs",
        ["eisf_document"] = "electronic Investigator Site File documents",
        ["ecoa_schedule"] = "participant assessment schedules",
        ["ecoa_diary"] = "participant diary entries",
        ["ecoa_submission"] = "participant questionnaire submissions",
        ["rtsm_supply"] = "investigational product supply records",
        ["rtsm_randomization"] = "randomization schedules",
        ["rtsm_allocation"] = "treatment arm allocations",
        ["rtsm_unblind"] = "emergency treatment unblinding",
        ["sce_program"] = "statistical computing programs",
        ["sce_run"] = "statistical analysis runs",
        ["sce_lock_token"] = "database lock analysis tokens",
    };

    private static readonly Dictionary<string, string> ActionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read"] = "view",
        ["read_raw"] = "view unredacted",
        ["read_type"] = "view",
        ["read_unblinded"] = "view unblinded",
        ["create"] = "create",
        ["write"] = "update",
        ["update"] = "update",
        ["delete"] = "remove",
        ["sign"] = "electronically sign",
        ["sign_off"] = "sign off on",
        ["approve"] = "approve",
        ["review"] = "review",
        ["publish"] = "publish",
        ["release"] = "release",
        ["promote"] = "promote",
        ["upload"] = "upload",
        ["export"] = "export",
        ["generate"] = "generate",
        ["execute"] = "execute",
        ["execute_unblinded"] = "execute unblinded",
        ["issue"] = "issue",
        ["revoke"] = "revoke",
        ["assign"] = "assign",
        ["activate"] = "activate",
        ["disburse"] = "disburse",
        ["reconcile"] = "reconcile",
        ["compile"] = "compile",
        ["transition"] = "transition",
        ["transition_approved"] = "approve",
        ["lock"] = "lock",
        ["unlock"] = "unlock",
        ["sync"] = "synchronize",
        ["investigate"] = "investigate",
        ["redact"] = "redact",
        ["tag"] = "classify",
        ["send"] = "send",
        ["trigger"] = "trigger",
        ["admin"] = "administer",
    };

    /// <summary>
    /// Canonical roles that grant <paramref name="permission"/> (<c>resource:action</c>), ordered for clinical readability.
    /// </summary>
    public static IReadOnlyList<string> RolesHolding(string permission)
    {
        if (!TrySplit(permission, out string resource, out string action))
        {
            return [];
        }

        var holders = _rolePermissions
            .Where(kvp => !string.Equals(kvp.Key, "admin", StringComparison.OrdinalIgnoreCase)
                && kvp.Value.TryGetValue(resource, out var actions)
                && actions.Contains(action))
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var ordered = PreferredRoleOrder.Where(holders.Remove).ToList();
        ordered.AddRange(holders.OrderBy(r => r, StringComparer.OrdinalIgnoreCase));
        return ordered;
    }

    /// <summary>Human-readable clinical title for a canonical or raw role name.</summary>
    public static string DescribeRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return "Authorized Study Staff";
        }

        string normalized = RoleNormalization.NormalizeRole(role);
        if (RoleDisplayNames.TryGetValue(normalized, out string? label))
        {
            return label;
        }

        return HumanizeWords(normalized);
    }

    /// <summary>Formats a list of roles as a concise plain-English phrase without raw role codes.</summary>
    public static string DescribeRoles(IEnumerable<string> roles, int maxListed = 3)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var distinct = roles
            .Select(DescribeRole)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinct.Count == 0)
        {
            return "authorized study role";
        }

        if (distinct.Count == 1)
        {
            return distinct[0];
        }

        if (distinct.Count == 2)
        {
            return $"{distinct[0]} or {distinct[1]}";
        }

        if (distinct.Count <= maxListed)
        {
            return $"{string.Join(", ", distinct.Take(distinct.Count - 1))}, or {distinct[^1]}";
        }

        int remaining = distinct.Count - (maxListed - 1);
        string head = string.Join(", ", distinct.Take(maxListed - 1));
        return $"{head}, or {remaining} other clinical roles";
    }

    /// <summary>
    /// Describes a <c>resource:action</c> permission in plain clinical English without exposing raw permission tokens.
    /// </summary>
    public static string DescribePermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return "perform this clinical action";
        }

        string trimmed = permission.Trim();
        if (ExactPermissionPhrases.TryGetValue(trimmed, out string? exact))
        {
            return exact;
        }

        if (!TrySplit(trimmed, out string resource, out string action))
        {
            return $"perform {HumanizeLowerWords(trimmed)}";
        }

        string noun = ResourceNouns.TryGetValue(resource, out string? mappedNoun)
            ? mappedNoun
            : HumanizeResourceNoun(resource);
        string verb = ActionVerbs.TryGetValue(action, out string? mappedVerb)
            ? mappedVerb
            : HumanizeLowerWords(action);

        return $"{verb} {noun}";
    }

    private static string HumanizeResourceNoun(string resource)
    {
        string words = HumanizeLowerWords(resource);
        return words.EndsWith('s') ? words : $"{words} records";
    }

    private static string HumanizeLowerWords(string token) =>
        string.Join(
            ' ',
            token.Split(['_', ':', '-', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ExpandClinicalAbbreviation));

    private static string ExpandClinicalAbbreviation(string part) => part.ToLowerInvariant() switch
    {
        "ctms" => "clinical trial management",
        "etmf" => "Trial Master File",
        "eisf" => "Investigator Site File",
        "ecrf" => "electronic case report form",
        "ecoa" => "electronic clinical outcome",
        "econsent" => "electronic consent",
        "rtsm" => "randomization and supply",
        "sdv" => "source data verification",
        "soa" => "Schedule of Activities",
        "qc" => "quality review",
        "edl" => "expected document list",
        "doa" => "delegation of authority",
        "sce" => "statistical computing",
        "cgt" => "cell and gene therapy",
        "cec" => "clinical endpoint adjudication",
        "ip" => "investigational product",
        "erp" => "financial ledger",
        _ => part.ToLowerInvariant(),
    };

    private static string HumanizeWords(string token) =>
        string.Join(
            ' ',
            token.Split(['_', ':', '-', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
}
