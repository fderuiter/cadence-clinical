using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Security.Rbac;

/// <summary>
/// Platform RBAC permission matrix mapping canonical roles to allowed resource-action sets.
/// @req:PRD-SYS-053
/// </summary>
public static class PermissionMatrix
{
    private static readonly Dictionary<string, Dictionary<string, HashSet<string>>> _rolePermissions =
        new(StringComparer.OrdinalIgnoreCase);

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
            ["query_lifecycle"] = ["create", "read", "update", "delete"],
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
            ["etmf_document"] = ["tag", "create", "read", "read_raw", "redact", "sign", "transition_technical_qc", "transition_approved", "transition_archived", "transition_rejected", "transition_draft", "transition_signed", "manage_expiration"],
            ["etmf_edl"] = ["read", "create"],
            ["etmf_taxonomy"] = ["read"],
            ["quality_event"] = ["create", "read", "update", "delete", "investigate"],
            ["quality_audit_logs"] = ["read"],
            ["eisf_document"] = ["create", "read", "update", "delete", "sync"],
            ["medical_coding"] = ["create", "read", "update"],
            ["lab_range"] = ["create", "read", "update", "delete", "alert"],
            ["ecoa_schedule"] = ["create", "read"],
            ["ecoa_diary"] = ["create", "read", "alert"],
            ["ecoa_submission"] = ["create", "read"]
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
            ["query_lifecycle"] = ["create", "read", "update"],
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
            ["training_log"] = ["read"]
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
            ["query_lifecycle"] = ["read", "update"],
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
            ["query_lifecycle"] = ["read", "update"],
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
            ["query_lifecycle"] = ["create", "read", "update", "delete"],
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
        _rolePermissions[RoleNormalization.ROLE_SUBJECT] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ecrf_data_entry"] = ["create", "update"],
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
            ["rtsm_allocation"] = ["read"]
        };

        _rolePermissions[RoleNormalization.ROLE_IDMC] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rtsm_randomization"] = ["read"],
            ["rtsm_allocation"] = ["read"]
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

        // Derived PI / ER Physician / Lead Investigator
        var piBase = ClonePermissions(_rolePermissions[RoleNormalization.ROLE_INVESTIGATOR]);
        piBase["rtsm_unblind"] = ["write"];
        piBase["eisf_document"] = ["create", "read", "update", "delete", "sync"];

        _rolePermissions[RoleNormalization.ROLE_PRINCIPAL_INVESTIGATOR] = ClonePermissions(piBase);
        _rolePermissions[RoleNormalization.ROLE_AUTHORIZED_ER_PHYSICIAN] = ClonePermissions(piBase);
        _rolePermissions[RoleNormalization.ROLE_LEAD_INVESTIGATOR] = ClonePermissions(piBase);
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
        if (principal == null || string.IsNullOrWhiteSpace(permission)) return false;

        int colonIndex = permission.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= permission.Length - 1) return false;

        string resource = permission[..colonIndex].Trim();
        string action = permission[(colonIndex + 1)..].Trim();

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
}
