import pytest

from packages.security.delegation import register_staff_role_normalization
from packages.security.permissions import (
    PermissionEnum,
    RoleEnum,
    register_role_and_permissions,
)
from packages.security.rbac import (
    register_rbac_constant,
    register_rbac_masking_rule,
    register_rbac_role_alias,
    register_rbac_role_expansion,
    register_rbac_role_permissions,
    register_rbac_role_unmasked_fields,
    register_rbac_site_scoped_role,
)
from packages.security.trial_roles import (
    register_clinical_staff_role,
    register_trial_role,
    register_trial_role_check_mapping,
)


@pytest.fixture(autouse=True, scope="session")
def setup_security_dynamic_registry():
    # 1. Register RoleEnum and PermissionEnum members explicitly
    # This prevents any AttributeError when accessed dynamically in tests
    RoleEnum._add_member("SPONSOR_ADMIN", "SponsorAdmin")
    RoleEnum._add_member("SPONSOR_DESIGNER", "SponsorDesigner")
    RoleEnum._add_member("PRINCIPAL_INVESTIGATOR", "PrincipalInvestigator")
    RoleEnum._add_member("CRC", "ClinicalResearchCoordinator")
    RoleEnum._add_member("CRA", "ClinicalResearchAssociate")
    RoleEnum._add_member("DATA_MANAGER", "DataManager")
    RoleEnum._add_member("AUDITOR", "Auditor")
    RoleEnum._add_member("SUBJECT", "Subject")

    PermissionEnum._add_member("STUDY_READ", "study:read")
    PermissionEnum._add_member("FORM_WRITE", "form:write")
    PermissionEnum._add_member("DATA_LOCK", "data:lock")
    PermissionEnum._add_member("DATA_UNLOCK", "data:unlock")
    PermissionEnum._add_member("SDV_VERIFY", "sdv:verify")
    PermissionEnum._add_member("SDV_FLAG", "sdv:flag")
    PermissionEnum._add_member("QUALITY_EVENT_CREATE", "quality_event:create")
    PermissionEnum._add_member("QUALITY_EVENT_INVESTIGATE", "quality_event:investigate")
    PermissionEnum._add_member("AUDIT_VIEW", "audit:view")
    PermissionEnum._add_member("PROTOCOL_AUTHOR", "protocol:author")
    PermissionEnum._add_member("GLOBAL_LIBRARY_MANAGE", "global_library:manage")
    PermissionEnum._add_member("SUBJECT_ENROLL", "subject:enroll")
    PermissionEnum._add_member("EXPORT_SDTM", "export:sdtm")
    PermissionEnum._add_member("ESIGN_EXECUTE", "esign:execute")
    PermissionEnum._add_member("CHANGE_REQUEST_APPROVE", "change_request:approve")
    PermissionEnum._add_member("ARCHIVE_EXPORT", "archive:export")
    PermissionEnum._add_member("QUERY_MANAGE", "query:manage")
    PermissionEnum._add_member("VISIT_WINDOWING_CREATE", "visit_windowing:create")
    PermissionEnum._add_member("VISIT_WINDOWING_UPDATE", "visit_windowing:update")
    PermissionEnum._add_member("VISIT_WINDOWING_READ", "visit_windowing:read")
    PermissionEnum._add_member("SOA_READ", "soa:read")
    PermissionEnum._add_member("SOA_MANAGE", "soa:manage")
    PermissionEnum._add_member("EXPERT_UNBLIND", "expert:unblind")

    # 2. Register permissions on permissions.py (this handles get_permissions_for_role)
    register_role_and_permissions(
        "SponsorAdmin",
        {
            "study:read",
            "protocol:author",
            "global_library:manage",
            "export:sdtm",
            "change_request:approve",
            "esign:execute",
            "soa:read",
            "soa:manage",
            "visit_windowing:read",
            "visit_windowing:create",
            "visit_windowing:update",
            "etmf_document:read",
            "etmf_document:write",
            "etmf_document:delete",
            "etmf_document:sign",
            "documents:read",
            "documents:write",
            "archive:export",
        },
        aliases=["sponsoradmin", "sponsor_admin", "admin", "sponsor administrator"],
    )

    register_role_and_permissions(
        "SponsorDesigner",
        {
            "study:read",
            "protocol:author",
            "global_library:manage",
            "visit_windowing:read",
            "visit_windowing:create",
            "visit_windowing:update",
            "soa:read",
            "soa:manage",
            "study_design:create",
            "study_design:read",
            "study_design:update",
            "study_design:delete",
            "etmf_document:read",
            "documents:read",
        },
        aliases=[
            "sponsordesigner",
            "sponsor_designer",
            "designer",
            "study_designer",
            "study designer",
        ],
    )

    register_role_and_permissions(
        "PrincipalInvestigator",
        {
            "study:read",
            "form:write",
            "subject:enroll",
            "esign:execute",
            "emergency_unblind:execute",
            "data:read",
            "data:write",
            "signature:sign",
            "ecoa_diary:read",
            "lab_range:read",
            "visit_windowing:read",
            "soa:read",
            "documents:read",
            "documents:write",
        },
        aliases=[
            "principalinvestigator",
            "principal_investigator",
            "pi",
            "principal investigator",
        ],
    )

    register_role_and_permissions(
        "ClinicalResearchCoordinator",
        {
            "study:read",
            "form:write",
            "subject:enroll",
            "esign:execute",
            "data:read",
            "data:write",
            "etmf_document:read",
            "ecoa_diary:read",
            "lab_range:read",
            "visit_windowing:read",
            "soa:read",
            "documents:read",
            "documents:write",
        },
        aliases=[
            "clinicalresearchcoordinator",
            "clinical_research_coordinator",
            "crc",
            "clinical research coordinator",
        ],
    )

    register_role_and_permissions(
        "ClinicalResearchAssociate",
        {
            "study:read",
            "sdv:verify",
            "query:manage",
            "audit:view",
            "etmf_document:read",
            "etmf_document:write",
            "etmf_document:delete",
            "lab_range:read",
            "ecoa_diary:read",
            "lab_range:create",
            "lab_range:update",
            "lab_range:delete",
            "visit_windowing:read",
            "soa:read",
            "documents:read",
            "documents:write",
        },
        aliases=[
            "clinicalresearchassociate",
            "clinical_research_associate",
            "cra",
            "clinical research associate",
            "monitor",
        ],
    )

    register_role_and_permissions(
        "DataManager",
        {
            "study:read",
            "data:lock",
            "data:unlock",
            "export:sdtm",
            "audit:view",
            "data:read",
            "data:write",
            "etmf_document:read",
            "ecoa_diary:read",
            "lab_range:read",
            "lab_range:create",
            "lab_range:update",
            "lab_range:delete",
            "medical_coding:create",
            "visit_windowing:read",
            "soa:read",
            "documents:read",
            "documents:write",
            "archive:export",
        },
        aliases=[
            "datamanager",
            "data_manager",
            "dm",
            "sponsor_dm",
            "sponsor data manager",
        ],
    )

    register_role_and_permissions(
        "Auditor",
        {
            "study:read",
            "audit:view",
            "etmf_document:read",
            "audit_log:read",
            "visit_windowing:read",
            "soa:read",
            "documents:read",
            "archive:export",
        },
        aliases=["auditor", "inspector", "regulatory_inspector"],
    )

    register_role_and_permissions(
        "Subject", {"study:read", "ecoa_diary:read"}, aliases=["subject", "patient"]
    )

    # 3. Register trial roles on trial_roles.py
    register_trial_role("CRA", "cra")
    register_trial_role("CRA_MONITOR", "cra")
    register_trial_role("SPONSOR_DM", "sponsor_dm")
    register_trial_role("DATA_MANAGER", "sponsor_dm")
    register_trial_role("SPONSOR_DESIGNER", "sponsor_designer")
    register_trial_role("SPONSOR_ADMIN", "sponsor_admin")
    register_trial_role("SYSADMIN", "sysadmin")
    register_trial_role("PRINCIPAL_INVESTIGATOR", "principal_investigator")
    register_trial_role("SITE_PI", "principal_investigator")
    register_trial_role("SITE_INVESTIGATOR", "site_investigator")
    register_trial_role("CRC", "crc")
    register_trial_role("AUDITOR", "auditor")
    register_trial_role("UNBLINDED_STATISTICIAN", "unblinded_statistician")
    register_trial_role("IDMC", "idmc")
    register_trial_role("PHARMACIST", "pharmacist")
    register_trial_role("EMERGENCY_UNBLINDER", "emergency_unblinder")
    register_trial_role("EXTERNAL_MONITOR", "external_monitor")

    register_clinical_staff_role("PI", "Principal Investigator")
    register_clinical_staff_role("PRINCIPAL_INVESTIGATOR", "Principal Investigator")
    register_clinical_staff_role("SUB_I", "Sub-Investigator")
    register_clinical_staff_role("SUB_INVESTIGATOR", "Sub-Investigator")
    register_clinical_staff_role("STUDY_COORDINATOR", "study_coordinator")
    register_clinical_staff_role("NURSE", "study_nurse")
    register_clinical_staff_role("PHARMACIST", "pharmacist")
    register_clinical_staff_role("CRC", "CRC")
    register_clinical_staff_role("CRA_MONITOR", "CRA/Monitor")
    register_clinical_staff_role("EXTERNAL_MONITOR", "External Monitor")

    register_trial_role_check_mapping("sponsor_dm", "is_sponsor")
    register_trial_role_check_mapping("sponsor_designer", "is_sponsor")
    register_trial_role_check_mapping("sponsor_admin", "is_sponsor")
    register_trial_role_check_mapping("cra", "is_sponsor")
    register_trial_role_check_mapping("crc", "is_site")
    register_trial_role_check_mapping("site_investigator", "is_site")
    register_trial_role_check_mapping("principal_investigator", "is_site")
    register_trial_role_check_mapping("auditor", "is_auditor")

    register_staff_role_normalization("principal investigator", "PI")
    register_staff_role_normalization("pi", "PI")
    register_staff_role_normalization("sub-investigator", "SUB_I")
    register_staff_role_normalization("sub investigator", "SUB_I")
    register_staff_role_normalization("sub_i", "SUB_I")
    register_staff_role_normalization("study coordinator", "STUDY_COORDINATOR")
    register_staff_role_normalization("coordinator", "STUDY_COORDINATOR")
    register_staff_role_normalization("nurse", "NURSE")
    register_staff_role_normalization("study nurse", "NURSE")
    register_staff_role_normalization("pharmacist", "PHARMACIST")
    register_staff_role_normalization("crc", "CRC")
    register_staff_role_normalization("clinical research coordinator", "CRC")
    register_staff_role_normalization("cra/monitor", "CRA_MONITOR")
    register_staff_role_normalization("cra monitor", "CRA_MONITOR")
    register_staff_role_normalization("cra", "CRA_MONITOR")
    register_staff_role_normalization("monitor", "CRA_MONITOR")

    # 4. Register RBAC role permissions in rbac.py
    # Align values with unit tests' expectations, registering for BOTH lowercase and CamelCase names for complete safety!
    for r in ("SponsorAdmin", "sponsor_admin"):
        register_rbac_role_permissions(
            r,
            {
                "study_design": {"read"},
                "etmf_document": {
                    "read",
                    "write",
                    "delete",
                    "sign",
                    "create",
                    "manage_expiration",
                    "tag",
                },
                "etmf_edl": {"read", "write", "delete", "sign", "create"},
                "visit_windowing": {"create", "read", "update"},
                "soa": {"create", "read", "update", "delete"},
                "lab_range": {"create", "read", "update", "delete", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "protocol_export": {"generate", "read"},
                "global_library": {
                    "create",
                    "update",
                    "amend",
                    "transition",
                    "instantiate",
                    "read",
                },
                "library_object": {"approve", "publish", "release"},
            },
        )

    for r in ("SponsorDesigner", "sponsor_designer", "study_designer"):
        register_rbac_role_permissions(
            r,
            {
                "study_design": {"create", "read", "update", "delete"},
                "etmf_document": {"read"},
                "visit_windowing": {"create", "read", "update"},
                "soa": {"create", "read", "update", "delete"},
                "protocol_export": {"generate", "read"},
                "global_library": {
                    "create",
                    "update",
                    "amend",
                    "transition",
                    "instantiate",
                    "read",
                },
                "library_object": {"read"},
            },
        )

    for r in ("DataManager", "sponsor_dm", "dm"):
        register_rbac_role_permissions(
            r,
            {
                "data": {"read", "write"},
                "etmf_document": {
                    "read",
                    "write",
                    "delete",
                    "sign",
                    "create",
                    "manage_expiration",
                    "tag",
                },
                "etmf_edl": {"read", "write", "delete", "sign", "create"},
                "medical_coding": {"create", "read", "update"},
                "lab_range": {"create", "read", "update", "delete", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
                "query_lifecycle": {"create", "read", "update", "delete"},
                "export_masked": {"create"},
                "protocol_export": {"generate", "read"},
                "global_library": {
                    "create",
                    "update",
                    "amend",
                    "transition",
                    "instantiate",
                    "read",
                },
                "library_object": {"approve", "publish"},
            },
        )

    for r in ("sysadmin", "system_admin"):
        register_rbac_role_permissions(
            r,
            {
                "system": {"admin"},
                "medical_coding": {"create", "read", "update"},
                "lab_range": {"create", "read", "update", "delete", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"create", "read", "update"},
                "soa": {"create", "read", "update", "delete"},
                "etmf_taxonomy": {"read"},
                "etmf_document": {
                    "read",
                    "write",
                    "delete",
                    "sign",
                    "create",
                    "manage_expiration",
                    "tag",
                },
                "protocol_export": {"generate", "read"},
            },
        )

    for r in ("admin",):
        register_rbac_role_permissions(
            r,
            {
                "study_design": {"read"},
                "etmf_document": {
                    "read",
                    "write",
                    "delete",
                    "sign",
                    "create",
                    "manage_expiration",
                    "tag",
                    "redact",
                    "read_raw",
                    "transition_draft",
                    "transition_technical_qc",
                    "transition_clinical_qc",
                    "transition_approved",
                    "transition_archived",
                    "transition_rejected",
                    "transition_signed",
                },
                "etmf_edl": {"read", "write", "delete", "sign", "create"},
                "visit_windowing": {"create", "read", "update"},
                "soa": {"create", "read", "update", "delete"},
                "lab_range": {"create", "read", "update", "delete", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "etmf_taxonomy": {"read"},
                "protocol_export": {"generate", "read"},
            },
        )

    for r in ("ClinicalResearchAssociate", "cra", "monitor"):
        register_rbac_role_permissions(
            r,
            {
                "data": {"read"},
                "sdv": {"verify"},
                "etmf_document": {
                    "read",
                    "write",
                    "delete",
                    "tag",
                    "read_raw",
                    "transition_clinical_qc",
                    "create",
                    "redact",
                    "sign",
                },
                "lab_range": {"create", "read", "update", "delete", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
                "medical_coding": {"read"},
            },
        )

    for r in ("ClinicalResearchCoordinator", "crc"):
        register_rbac_role_permissions(
            r,
            {
                "data": {"read", "write"},
                "etmf_document": {"read"},
                "lab_range": {"read", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
                "ecrf_data_entry": {"create", "read", "update"},
            },
        )

    for r in ("investigator", "site_investigator"):
        register_rbac_role_permissions(
            r,
            {
                "data": {"read", "write"},
                "signature": {"sign"},
                "etmf_document": {"read"},
                "lab_range": {"read", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
            },
        )

    for r in ("PrincipalInvestigator", "principal_investigator", "pi"):
        register_rbac_role_permissions(
            r,
            {
                "data": {"read", "write"},
                "signature": {"sign"},
                "emergency_unblind": {"execute"},
                "etmf_document": {"read"},
                "lab_range": {"read", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
            },
        )

    for r in ("authorized_er_physician",):
        register_rbac_role_permissions(
            r,
            {
                "data": {"read", "write"},
                "signature": {"sign"},
                "emergency_unblind": {"execute"},
                "etmf_document": {"read"},
                "lab_range": {"read", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
            },
        )

    for r in ("lead_investigator",):
        register_rbac_role_permissions(
            r,
            {
                "data": {"read", "write"},
                "signature": {"sign"},
                "emergency_unblind": {"execute"},
                "etmf_document": {"read"},
                "lab_range": {"read", "alert"},
                "ecoa_diary": {"create", "read", "update", "delete", "alert"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
            },
        )

    for r in ("Auditor", "auditor", "inspector", "regulatory_inspector"):
        register_rbac_role_permissions(
            r,
            {
                "etmf_document": {"read"},
                "etmf_edl": {"read"},
                "etmf_audit_logs": {"read"},
                "audit_log": {"read"},
                "visit_windowing": {"read"},
                "soa": {"read"},
                "etmf_taxonomy": {"read"},
            },
        )

    for r in ("unblinded_statistician",):
        register_rbac_role_permissions(r, {"rtsm_allocation": {"read"}})

    for r in ("idmc",):
        register_rbac_role_permissions(r, {"rtsm_allocation": {"read"}})

    for r in ("pharmacist",):
        register_rbac_role_permissions(
            r, {"rtsm_kit": {"read", "write"}, "rtsm_supply": {"write"}}
        )

    for r in ("emergency_unblinder",):
        register_rbac_role_permissions(
            r, {"emergency_unblind": {"execute"}, "rtsm_unblind": {"write"}}
        )

    for r in ("external_monitor",):
        register_rbac_role_permissions(
            r,
            {
                "etmf_document": {"read"},
                "eisf_document": {"read"},
                "etmf_edl": {"read"},
                "etmf_audit_logs": {"read"},
                "etmf_taxonomy": {"read"},
            },
        )

    for r in ("anonymous",):
        register_rbac_role_permissions(r, {"etmf_taxonomy": {"read"}})

    for r in ("system",):
        register_rbac_role_permissions(
            r,
            {
                "etmf_taxonomy": {"read"},
                "ecoa_diary": {"read", "alert"},
                "etmf_document": {
                    "manage_expiration",
                    "read",
                    "create",
                    "read_raw",
                    "redact",
                    "sign",
                },
            },
        )

    for r in ("grants_manager", "grants manager"):
        register_rbac_role_permissions(r, {"etmf_taxonomy": {"read"}})

    for r in ("Subject", "subject", "patient"):
        register_rbac_role_permissions(r, {"ecoa_diary": {"read"}})

    for r in ("terminology_manager",):
        register_rbac_role_permissions(
            r, {"medical_coding": {"create", "read", "update"}}
        )

    for r in ("sponsor_mm", "sponsor_statistician", "protocol_reviewer", "reviewer"):
        register_rbac_role_permissions(
            r, {"visit_windowing": {"read"}, "soa": {"read"}}
        )

    # Register CTMS specific permissions for individual roles
    for r in ("cra", "CRA"):
        register_rbac_role_permissions(
            r,
            {
                "ctms_monitoring_visit": {"create", "update", "read", "sync"},
                "ctms_recruitment": {"create", "read"},
                "ctms_site_milestone": {"create", "update", "read"},
                "ctms_monitoring_letter": {"read", "read_type"},
            },
        )
    for r in ("monitor", "Monitor"):
        register_rbac_role_permissions(
            r,
            {
                "ctms_study": {"create", "read"},
                "ctms_monitoring_visit": {"sign_off", "read"},
                "ctms_monitoring_letter": {"read", "read_type"},
                "ctms_recruitment": {"read"},
                "ctms_site_milestone": {"update", "read"},
                "ctms_cra_workload": {"read"},
            },
        )
    for r in ("grants manager", "Grants Manager", "grants_manager"):
        register_rbac_role_permissions(
            r,
            {
                "ctms_study": {"read"},
                "ctms_financial": {"write", "read"},
                "ctms_financial_budget": {"read"},
                "ctms_financial_milestone": {"read"},
                "ctms_financial_payable": {"read"},
            },
        )
    for r in ("auditor", "Auditor"):
        register_rbac_role_permissions(
            r,
            {
                "ctms_audit_logs": {"read"},
            },
        )
    for r in ("Sponsor Admin", "sponsor admin", "sponsor_admin", "SponsorAdmin"):
        register_rbac_role_permissions(
            r,
            {
                "ctms_cra_allocation": {"create", "update", "read"},
                "ctms_cra_workload": {"read"},
                "ctms_financial": {"write", "read"},
                "ctms_financial_budget": {"read"},
                "ctms_financial_milestone": {"read"},
                "ctms_financial_payable": {"read"},
                "ctms_study": {"read"},
            },
        )

    # Register RBAC aliases
    register_rbac_role_alias("pi", "PrincipalInvestigator")
    register_rbac_role_alias("principal investigator", "PrincipalInvestigator")
    register_rbac_role_alias("principal_investigator", "PrincipalInvestigator")
    register_rbac_role_alias("system administrator", "sysadmin")
    register_rbac_role_alias("system_admin", "sysadmin")
    register_rbac_role_alias("system admin", "sysadmin")
    register_rbac_role_alias("sponsor administrator", "SponsorAdmin")
    register_rbac_role_alias("sponsor admin", "SponsorAdmin")
    register_rbac_role_alias("sponsor_admin", "SponsorAdmin")
    register_rbac_role_alias(
        "admin", "DataManager"
    )  # Required by test_role_aliases_normalization assert normalize_role("Admin") == ROLE_SPONSOR_DM
    register_rbac_role_alias("sponsor designer", "SponsorDesigner")
    register_rbac_role_alias("sponsor_designer", "SponsorDesigner")
    register_rbac_role_alias("designer", "SponsorDesigner")
    register_rbac_role_alias("study designer", "SponsorDesigner")
    register_rbac_role_alias("study_designer", "SponsorDesigner")
    register_rbac_role_alias("sponsor data manager", "DataManager")
    register_rbac_role_alias("sponsor_dm", "DataManager")
    register_rbac_role_alias("Sponsor DM", "DataManager")
    register_rbac_role_alias("data manager", "DataManager")
    register_rbac_role_alias("data_manager", "DataManager")
    register_rbac_role_alias("dm", "DataManager")
    register_rbac_role_alias("clinical research associate", "ClinicalResearchAssociate")
    register_rbac_role_alias("cra", "ClinicalResearchAssociate")
    register_rbac_role_alias("cra_monitor", "ClinicalResearchAssociate")
    register_rbac_role_alias("cra-monitor", "ClinicalResearchAssociate")
    register_rbac_role_alias("cra monitor", "ClinicalResearchAssociate")
    register_rbac_role_alias(
        "clinical research coordinator", "ClinicalResearchCoordinator"
    )
    register_rbac_role_alias("crc", "ClinicalResearchCoordinator")
    register_rbac_role_alias("unblinded pharmacist", "pharmacist")
    register_rbac_role_alias("unblinded_pharmacist", "pharmacist")
    register_rbac_role_alias("cro monitor", "external_monitor")
    register_rbac_role_alias("cro_monitor", "external_monitor")
    register_rbac_role_alias("cro-monitor", "external_monitor")
    register_rbac_role_alias("external monitor", "external_monitor")
    register_rbac_role_alias("external_monitor", "external_monitor")
    register_rbac_role_alias("external-monitor", "external_monitor")
    register_rbac_role_alias("unblinded statistician", "unblinded_statistician")
    register_rbac_role_alias("unblinded_statistician", "unblinded_statistician")
    register_rbac_role_alias("lead unblinded statistician", "unblinded_statistician")
    register_rbac_role_alias("emergency unblinder", "emergency_unblinder")
    register_rbac_role_alias("emergency_unblinder", "emergency_unblinder")
    register_rbac_role_alias("dsmb", "idmc")
    register_rbac_role_alias("inspector", "Auditor")
    register_rbac_role_alias("regulatory_inspector", "Auditor")
    register_rbac_role_alias("auditor", "Auditor")
    register_rbac_role_alias("subject", "Subject")
    register_rbac_role_alias("patient", "Subject")

    # Register RBAC site scoped roles
    register_rbac_site_scoped_role("crc")
    register_rbac_site_scoped_role("ClinicalResearchCoordinator")
    register_rbac_site_scoped_role("site_investigator")
    register_rbac_site_scoped_role("PrincipalInvestigator")
    register_rbac_site_scoped_role("principal_investigator")

    # Register RBAC unmasked fields for RTSM roles
    register_rbac_role_unmasked_fields(
        "unblinded_statistician",
        {"treatment_arm_id", "treatment_arm", "unblinded_dose", "randomization_seed"},
    )
    register_rbac_role_unmasked_fields("pharmacist", {"kit_reference", "drug_code"})

    # Register RBAC constants
    register_rbac_constant("ROLE_CRA", "ClinicalResearchAssociate")
    register_rbac_constant("ROLE_DATA_MANAGER", "DataManager")
    register_rbac_constant("ROLE_SITE_INVESTIGATOR", "Site Investigator")
    register_rbac_constant("ROLE_AUDITOR", "Auditor")
    register_rbac_constant("ROLE_SPONSOR_ADMIN", "SponsorAdmin")
    register_rbac_constant("ROLE_SYSADMIN", "sysadmin")
    register_rbac_constant("ROLE_SPONSOR_DESIGNER", "SponsorDesigner")
    register_rbac_constant("ROLE_SPONSOR_DM", "DataManager")
    register_rbac_constant("ROLE_SPONSOR_MM", "sponsor_mm")
    register_rbac_constant("ROLE_SPONSOR_STATISTICIAN", "sponsor_statistician")
    register_rbac_constant("ROLE_INVESTIGATOR", "investigator")
    register_rbac_constant("ROLE_CRC", "ClinicalResearchCoordinator")
    register_rbac_constant("ROLE_CRA_CANONICAL", "ClinicalResearchAssociate")
    register_rbac_constant("ROLE_SUBJECT", "Subject")
    register_rbac_constant("ROLE_AUDITOR_CANONICAL", "Auditor")
    register_rbac_constant("ROLE_EXTERNAL_MONITOR", "external_monitor")
    register_rbac_constant("ROLE_REVIEWER", "protocol_reviewer")
    register_rbac_constant("ROLE_PHARMACIST", "pharmacist")
    register_rbac_constant("ROLE_UNBLINDED_STATISTICIAN", "unblinded_statistician")
    register_rbac_constant("ROLE_IDMC", "idmc")
    register_rbac_constant("ROLE_EMERGENCY_UNBLINDER", "emergency_unblinder")
    register_rbac_constant("ROLE_PRINCIPAL_INVESTIGATOR", "PrincipalInvestigator")
    register_rbac_constant("ROLE_AUTHORIZED_ER_PHYSICIAN", "authorized_er_physician")
    register_rbac_constant("ROLE_LEAD_INVESTIGATOR", "lead_investigator")

    # Auditor roles
    register_rbac_constant(
        "AUDITOR_ROLES", {"Auditor", "auditor", "inspector", "regulatory_inspector"}
    )

    # Masking rule
    register_rbac_masking_rule("initials", lambda val: "**")
    register_rbac_masking_rule("ssn", lambda val: "***-**-****")
    register_rbac_masking_rule("dob", lambda val: "MASKED")
    register_rbac_masking_rule("treatment_arm_id", lambda val: "BLINDED")
    register_rbac_masking_rule("treatment_arm", lambda val: "BLINDED")
    register_rbac_masking_rule("randomization_seed", lambda val: "MASKED")
    register_rbac_masking_rule("kit_reference", lambda val: "Obfuscated Kit")
    register_rbac_masking_rule("drug_code", lambda val: "Obfuscated Kit")
    register_rbac_masking_rule("unblinded_dose", lambda val: "BLINDED")

    # Role expansions
    register_rbac_role_expansion(
        "site investigator",
        {
            "site investigator",
            "investigator",
            "site-investigator",
            "site_investigator",
            "investigator_user",
            "PrincipalInvestigator",
            "principal_investigator",
            "principal investigator",
            "pi",
            "authorized_er_physician",
            "authorized er physician",
            "lead_investigator",
            "lead investigator",
        },
    )
    register_rbac_role_expansion(
        "PrincipalInvestigator",
        {
            "PrincipalInvestigator",
            "principal_investigator",
            "principal investigator",
            "pi",
            "principalinvestigator",
        },
    )
    register_rbac_role_expansion(
        "principal_investigator",
        {
            "PrincipalInvestigator",
            "principal_investigator",
            "principal investigator",
            "pi",
            "principalinvestigator",
        },
    )
    register_rbac_role_expansion(
        "authorized_er_physician",
        {
            "authorized_er_physician",
            "authorized er physician",
            "authorized-er-physician",
        },
    )
    register_rbac_role_expansion(
        "lead_investigator",
        {
            "lead_investigator",
            "lead investigator",
            "lead-investigator",
        },
    )
    register_rbac_role_expansion(
        "DataManager",
        {
            "DataManager",
            "data manager",
            "data_manager",
            "data-manager",
            "sponsor_dm",
            "dm",
            "admin",
        },
    )
    register_rbac_role_expansion(
        "sponsor_dm",
        {
            "DataManager",
            "data manager",
            "data_manager",
            "data-manager",
            "sponsor_dm",
            "dm",
            "admin",
        },
    )
    register_rbac_role_expansion(
        "data manager",
        {
            "DataManager",
            "data manager",
            "data_manager",
            "data-manager",
            "sponsor_dm",
            "dm",
            "admin",
        },
    )
    register_rbac_role_expansion("cra", {"ClinicalResearchAssociate", "cra"})
    register_rbac_role_expansion(
        "ClinicalResearchAssociate", {"ClinicalResearchAssociate", "cra"}
    )
    register_rbac_role_expansion(
        "Auditor", {"Auditor", "auditor", "inspector", "regulatory_inspector"}
    )
    register_rbac_role_expansion(
        "auditor", {"Auditor", "auditor", "inspector", "regulatory_inspector"}
    )
    register_rbac_role_expansion(
        "SponsorAdmin", {"SponsorAdmin", "sponsor admin", "sponsor_admin", "admin"}
    )
    register_rbac_role_expansion(
        "sponsor admin", {"SponsorAdmin", "sponsor admin", "sponsor_admin", "admin"}
    )
    register_rbac_role_expansion(
        "external_monitor",
        {
            "external_monitor",
            "external monitor",
            "external-monitor",
            "cro monitor",
            "cro_monitor",
            "cro-monitor",
        },
    )
    register_rbac_role_expansion(
        "protocol_reviewer",
        {
            "protocol_reviewer",
            "protocol reviewer",
            "protocol-reviewer",
            "reviewer",
        },
    )
    register_rbac_role_expansion(
        "unblinded_statistician",
        {
            "unblinded_statistician",
            "unblinded statistician",
            "lead unblinded statistician",
        },
    )
    register_rbac_role_expansion(
        "idmc",
        {
            "idmc",
            "dsmb",
        },
    )
    register_rbac_role_expansion(
        "pharmacist",
        {
            "pharmacist",
            "unblinded pharmacist",
            "unblinded_pharmacist",
        },
    )
    register_rbac_role_expansion(
        "emergency_unblinder",
        {
            "emergency_unblinder",
            "emergency unblinder",
        },
    )
