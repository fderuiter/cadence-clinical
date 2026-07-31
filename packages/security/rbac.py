from typing import Any, Callable, Dict, List, Optional, Set

import pydantic
from fastapi import Depends, HTTPException, Request, status
from pydantic import BaseModel, Field

# Legacy, allow-list-based role constants
ROLE_CRA = "CRA"
ROLE_DATA_MANAGER = "Data Manager"
ROLE_SITE_INVESTIGATOR = "Site Investigator"
ROLE_AUDITOR = "Auditor"
ROLE_SPONSOR_ADMIN = "Sponsor Admin"

AUDITOR_ROLES = {"auditor", "inspector", "regulatory_inspector"}


# Canonical lower-case roles from docs/SDLC/05_Security_Compliance_Audit_Spec.md §2.1
ROLE_SYSADMIN = "sysadmin"
ROLE_SPONSOR_DESIGNER = "sponsor_designer"
ROLE_SPONSOR_DM = "sponsor_dm"
ROLE_SPONSOR_MM = "sponsor_mm"
ROLE_SPONSOR_STATISTICIAN = "sponsor_statistician"
ROLE_INVESTIGATOR = "investigator"
ROLE_CRC = "crc"
ROLE_CRA_CANONICAL = "cra"
ROLE_SUBJECT = "subject"
ROLE_AUDITOR_CANONICAL = "auditor"
ROLE_EXTERNAL_MONITOR = "external_monitor"
ROLE_REVIEWER = "protocol_reviewer"

# RTSM roles
ROLE_PHARMACIST = "pharmacist"
ROLE_UNBLINDED_STATISTICIAN = "unblinded_statistician"
ROLE_IDMC = "idmc"
ROLE_EMERGENCY_UNBLINDER = "emergency_unblinder"
ROLE_PRINCIPAL_INVESTIGATOR = "principal_investigator"
ROLE_AUTHORIZED_ER_PHYSICIAN = "authorized_er_physician"
ROLE_LEAD_INVESTIGATOR = "lead_investigator"


ROLE_ALIASES = {
    "unblinded statistician": ROLE_UNBLINDED_STATISTICIAN,
    "lead unblinded statistician": ROLE_UNBLINDED_STATISTICIAN,
    "unblinded_statistician": ROLE_UNBLINDED_STATISTICIAN,
    "idmc": ROLE_IDMC,
    "dsmb": ROLE_IDMC,
    "pharmacist": ROLE_PHARMACIST,
    "unblinded pharmacist": ROLE_PHARMACIST,
    "unblinded_pharmacist": ROLE_PHARMACIST,
    "emergency unblinder": ROLE_EMERGENCY_UNBLINDER,
    "emergency_unblinder": ROLE_EMERGENCY_UNBLINDER,
    "protocol_reviewer": ROLE_REVIEWER,
    "protocol reviewer": ROLE_REVIEWER,
    "protocol-reviewer": ROLE_REVIEWER,
    "reviewer": ROLE_REVIEWER,
    "external monitor": ROLE_EXTERNAL_MONITOR,
    "external_monitor": ROLE_EXTERNAL_MONITOR,
    "external-monitor": ROLE_EXTERNAL_MONITOR,
    "cro monitor": ROLE_EXTERNAL_MONITOR,
    "cro_monitor": ROLE_EXTERNAL_MONITOR,
    "cro-monitor": ROLE_EXTERNAL_MONITOR,
    "sysadmin": ROLE_SYSADMIN,
    "system administrator": ROLE_SYSADMIN,
    "system_admin": ROLE_SYSADMIN,
    "system-admin": ROLE_SYSADMIN,
    "sponsor study designer": ROLE_SPONSOR_DESIGNER,
    "sponsor_designer": ROLE_SPONSOR_DESIGNER,
    "sponsor-designer": ROLE_SPONSOR_DESIGNER,
    "designer": ROLE_SPONSOR_DESIGNER,
    "study_designer": ROLE_SPONSOR_DESIGNER,
    "study-designer": ROLE_SPONSOR_DESIGNER,
    "study designer": ROLE_SPONSOR_DESIGNER,
    "sponsor designer": ROLE_SPONSOR_DESIGNER,
    "sponsor data manager": ROLE_SPONSOR_DM,
    "sponsor_dm": ROLE_SPONSOR_DM,
    "sponsor-dm": ROLE_SPONSOR_DM,
    "sponsor dm": ROLE_SPONSOR_DM,
    "data manager": ROLE_SPONSOR_DM,
    "data_manager": ROLE_SPONSOR_DM,
    "data-manager": ROLE_SPONSOR_DM,
    "dm": ROLE_SPONSOR_DM,
    "admin": ROLE_SPONSOR_DM,
    "sponsor admin": ROLE_SPONSOR_DM,
    "sponsor_admin": ROLE_SPONSOR_DM,
    "sponsor medical monitor": ROLE_SPONSOR_MM,
    "sponsor_mm": ROLE_SPONSOR_MM,
    "sponsor-mm": ROLE_SPONSOR_MM,
    "sponsor mm": ROLE_SPONSOR_MM,
    "medical monitor": ROLE_SPONSOR_MM,
    "medical_monitor": ROLE_SPONSOR_MM,
    "mm": ROLE_SPONSOR_MM,
    "sponsor statistician": ROLE_SPONSOR_STATISTICIAN,
    "sponsor_statistician": ROLE_SPONSOR_STATISTICIAN,
    "sponsor-statistician": ROLE_SPONSOR_STATISTICIAN,
    "statistician": ROLE_SPONSOR_STATISTICIAN,
    "investigator": ROLE_INVESTIGATOR,
    "site investigator": ROLE_INVESTIGATOR,
    "site_investigator": ROLE_INVESTIGATOR,
    "site-investigator": ROLE_INVESTIGATOR,
    "principal investigator": ROLE_PRINCIPAL_INVESTIGATOR,
    "pi": ROLE_PRINCIPAL_INVESTIGATOR,
    "principal_investigator": ROLE_PRINCIPAL_INVESTIGATOR,
    "principalinvestigator": ROLE_PRINCIPAL_INVESTIGATOR,
    "authorized er physician": ROLE_AUTHORIZED_ER_PHYSICIAN,
    "authorized_er_physician": ROLE_AUTHORIZED_ER_PHYSICIAN,
    "authorized-er-physician": ROLE_AUTHORIZED_ER_PHYSICIAN,
    "authorederphysician": ROLE_AUTHORIZED_ER_PHYSICIAN,
    "er physician": ROLE_AUTHORIZED_ER_PHYSICIAN,
    "er_physician": ROLE_AUTHORIZED_ER_PHYSICIAN,
    "lead investigator": ROLE_LEAD_INVESTIGATOR,
    "lead_investigator": ROLE_LEAD_INVESTIGATOR,
    "lead-investigator": ROLE_LEAD_INVESTIGATOR,
    "leadinvestigator": ROLE_LEAD_INVESTIGATOR,
    "investigator_user": ROLE_INVESTIGATOR,
    "crc": ROLE_CRC,
    "clinical research coordinator": ROLE_CRC,
    "study coordinator": ROLE_CRC,
    "study_coordinator": ROLE_CRC,
    "study-coordinator": ROLE_CRC,
    "coordinator": ROLE_CRC,
    "cra": ROLE_CRA_CANONICAL,
    "clinical research associate": ROLE_CRA_CANONICAL,
    "monitor": "monitor",
    "cra/monitor": ROLE_CRA_CANONICAL,
    "cra_monitor": ROLE_CRA_CANONICAL,
    "cra-monitor": ROLE_CRA_CANONICAL,
    "subject": ROLE_SUBJECT,
    "patient": ROLE_SUBJECT,
    "epro": ROLE_SUBJECT,
    "auditor": ROLE_AUDITOR_CANONICAL,
    "inspector": ROLE_AUDITOR_CANONICAL,
    "regulatory_inspector": ROLE_AUDITOR_CANONICAL,
}


# Declarative action vocabulary and role-to-permission matrix matching §2.2
# Key format: ROLE -> RESOURCE -> SET OF ACTIONS
# Actions: "create", "read", "update", "delete"
ROLE_PERMISSIONS: Dict[str, Dict[str, Set[str]]] = {
    ROLE_SYSADMIN: {
        "study_design": {"create", "read", "update", "delete", "approve", "reorder"},
        "global_library": {
            "create",
            "update",
            "amend",
            "transition",
            "instantiate",
            "read",
        },
        "mdr_concept": {"create", "update", "rename", "delete", "read"},
        "protocol_export": {"generate", "read"},
        "designer_cache": {"admin"},
        "system_audit_logs": {"read"},
        "export_masked": {"read"},
        "protocol_ingestion": {"upload", "read", "review", "promote"},
        "protocol_section": {"lock", "unlock", "approve", "review", "read"},
        # New permissions
        "protocol_version": {"sign", "transition_approved"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # CTMS
        "ctms_study": {"create", "read"},
        "ctms_audit_logs": {"read"},
        "ctms_monitoring_visit": {"create", "update", "read", "sign_off", "sync"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"create", "read"},
        "ctms_site_milestone": {"create", "update", "read"},
        "ctms_cra_allocation": {"create", "update", "read"},
        "ctms_cra_workload": {"read"},
        "ctms_financial": {"create", "read", "update", "write"},
        "ctms_financial_budget": {"create", "read"},
        "ctms_financial_milestone": {"create", "read", "trigger"},
        "ctms_financial_payable": {"read"},
        # eTMF
        "etmf_document": {
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
            "manage_expiration",
        },
        "etmf_edl": {"read", "create"},
        "etmf_audit_logs": {"read"},
        # Quality
        "quality_event": {"create", "read", "update", "delete", "investigate"},
        "quality_audit_logs": {"read"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "tsdv_config": {"create", "read", "update", "delete"},
        "form_submission": {"create", "read", "update", "delete"},
        "pi_signoff": {"create", "read", "update", "delete"},
        "medical_coding": {"create", "read", "update", "delete"},
        "trial_lock": {"create", "read", "update", "delete"},
        "export_unmasked": {"create", "read", "update", "delete"},
    },
    ROLE_SPONSOR_DESIGNER: {
        "study_design": {"create", "read", "update", "delete", "approve", "reorder"},
        "global_library": {
            "create",
            "update",
            "amend",
            "transition",
            "instantiate",
            "read",
        },
        "mdr_concept": {"create", "update", "rename", "delete", "read"},
        "protocol_export": {"generate", "read"},
        "designer_cache": {"admin"},
        "system_audit_logs": {"read"},
        "protocol_ingestion": {"upload", "read", "review", "promote"},
        "protocol_version": {"sign", "transition_approved"},
        "protocol_section": {"lock", "unlock", "approve", "review", "read"},
        "regulatory_form": {"read"},
        "training_log": {"read"},
        "trial_lock": {"read"},
    },
    ROLE_REVIEWER: {
        "study_design": {"read"},
        "protocol_ingestion": {"upload", "read", "review", "promote"},
        "protocol_section": {"review", "read"},
    },
    ROLE_SPONSOR_DM: {
        "study_design": {"read", "approve"},
        "global_library": {"transition", "read"},
        "mdr_concept": {"read"},
        "protocol_export": {"generate", "read"},
        "subject_enrollment": {"read"},
        "ecrf_data_entry": {"read"},
        "query_lifecycle": {"create", "read", "update", "delete"},
        "system_audit_logs": {"read"},
        "export_masked": {"create", "read", "update"},
        "protocol_version": {"transition_approved"},
        "protocol_section": {"read"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # CTMS
        "ctms_study": {"create", "read"},
        "ctms_audit_logs": {"read"},
        "ctms_monitoring_visit": {"create", "update", "read", "sign_off", "sync"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"create", "read"},
        "ctms_site_milestone": {"create", "update", "read"},
        "ctms_cra_allocation": {"create", "update", "read"},
        "ctms_cra_workload": {"read"},
        "ctms_financial": {"create", "read", "update", "write"},
        "ctms_financial_budget": {"create", "read"},
        "ctms_financial_milestone": {"create", "read", "trigger"},
        "ctms_financial_payable": {"read"},
        # eTMF
        "etmf_document": {
            "create",
            "read",
            "read_raw",
            "redact",
            "sign",
            "transition_technical_qc",
            "transition_approved",
            "transition_archived",
            "transition_rejected",
            "transition_draft",
            "transition_signed",
            "manage_expiration",
        },
        "etmf_edl": {"read", "create"},
        # Quality
        "quality_event": {"create", "read", "update", "delete", "investigate"},
        "quality_audit_logs": {"read"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "tsdv_config": {"read"},
        "form_submission": {"read"},
        "pi_signoff": {"read"},
        "medical_coding": {"create", "read", "update", "delete"},
        "trial_lock": {"create", "read", "update", "delete"},
    },
    ROLE_SPONSOR_MM: {
        "study_design": {"read"},
        "subject_enrollment": {"read"},
        "ecrf_data_entry": {"read"},
        "query_lifecycle": {"create", "read", "update"},
        "system_audit_logs": {"read"},
        "export_masked": {"read"},
        "eisf_document": {"read"},
        "regulatory_form": {"read"},
        "training_log": {"read"},
        # Execution Core Resources
        "form_submission": {"read"},
        "pi_signoff": {"read"},
        "medical_coding": {"read"},
        "trial_lock": {"read"},
    },
    ROLE_SPONSOR_STATISTICIAN: {
        "study_design": {"read"},
        "system_audit_logs": {"read"},
        "export_masked": {"create", "read", "update"},
        "eisf_document": {"read"},
        "regulatory_form": {"read"},
        "training_log": {"read"},
        # Execution Core Resources
        "export_unmasked": {"create", "read", "update"},
        "trial_lock": {"read"},
    },
    ROLE_INVESTIGATOR: {
        "study_design": {"read"},
        "subject_enrollment": {"create", "read", "update"},
        "rtsm_unblind": {"write"},
        "ecrf_data_entry": {"create", "read", "update"},
        "query_lifecycle": {
            "read",
            "update",
        },  # 'Ans' (Answer query) maps to update/read
        "sdv": {"read"},
        "system_audit_logs": {"read"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # CTMS
        "ctms_study": {"read"},
        "ctms_recruitment": {"read"},
        "ctms_site_milestone": {"read"},
        "ctms_cra_allocation": {"read"},
        "ctms_cra_workload": {"read"},
        # eTMF
        "etmf_document": {"read"},
        "etmf_edl": {"read"},
        # Quality
        "quality_event": {"read"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "tsdv_config": {"read"},
        "form_submission": {"create", "read", "update"},
        "pi_signoff": {"create", "read", "update"},
        "trial_lock": {"read"},
    },
    ROLE_CRC: {
        "study_design": {"read"},
        "subject_enrollment": {"create", "read", "update"},
        "ecrf_data_entry": {
            "create",
            "read",
            "update",
        },  # 'C/R/U (Draft)' maps to create/read/update
        "query_lifecycle": {"read", "update"},  # 'Ans' maps to update/read
        "system_audit_logs": {"read"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # CTMS
        "ctms_study": {"read"},
        "ctms_recruitment": {"read"},
        "ctms_site_milestone": {"read"},
        "ctms_cra_allocation": {"read"},
        "ctms_cra_workload": {"read"},
        # eTMF
        "etmf_document": {"read"},
        "etmf_edl": {"read"},
        # Quality
        "quality_event": {"read"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "form_submission": {"create", "read", "update"},
        "pi_signoff": {"read"},
        "trial_lock": {"read"},
    },
    ROLE_CRA_CANONICAL: {
        "study_design": {"read"},
        "subject_enrollment": {"read"},
        "ecrf_data_entry": {"read"},
        "query_lifecycle": {"create", "read", "update", "delete"},
        "sdv": {"create", "read", "update", "delete"},
        "system_audit_logs": {"read"},
        "export_masked": {"read"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # CTMS
        "ctms_study": {"create", "read"},
        "ctms_monitoring_visit": {"create", "update", "read", "sync"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"create", "read"},
        "ctms_site_milestone": {"create", "update", "read"},
        "ctms_cra_allocation": {"read"},
        "ctms_cra_workload": {"read"},
        # eTMF
        "etmf_document": {"create", "read", "redact", "sign", "transition_clinical_qc"},
        "etmf_edl": {"read", "create"},
        # Quality
        "quality_event": {"create", "read", "update"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "tsdv_config": {"create", "read", "update", "delete"},
        "form_submission": {"read"},
        "pi_signoff": {"read"},
        "trial_lock": {"read"},
    },
    "monitor": {
        "study_design": {"read"},
        "system_audit_logs": {"read"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # CTMS
        "ctms_study": {"create", "read"},
        "ctms_monitoring_visit": {"read", "sign_off", "sync"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"create", "read"},
        "ctms_site_milestone": {"create", "update", "read"},
        "ctms_cra_allocation": {"read"},
        "ctms_cra_workload": {"read"},
        # eTMF
        "etmf_document": {"create", "read", "redact", "sign", "transition_clinical_qc"},
        "etmf_edl": {"read", "create"},
        # Quality
        "quality_event": {"create", "read", "update"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "tsdv_config": {"create", "read", "update", "delete"},
        "form_submission": {"read"},
        "pi_signoff": {"read"},
        "trial_lock": {"read"},
    },
    ROLE_SUBJECT: {
        "ecrf_data_entry": {"create", "update"},  # 'Diary' maps to create/update
        # Execution Core Resources
        "form_submission": {"create", "update"},
    },
    ROLE_AUDITOR_CANONICAL: {
        "system_audit_logs": {"read"},
        "regulatory_form": {"read"},
        "training_log": {"read"},
        # CTMS read-only
        "ctms_study": {"read"},
        "ctms_audit_logs": {"read"},
        "ctms_monitoring_visit": {"read"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"read"},
        "ctms_site_milestone": {"read"},
        "ctms_cra_allocation": {"read"},
        "ctms_cra_workload": {"read"},
        "ctms_financial": {"read"},
        "ctms_financial_budget": {"read"},
        "ctms_financial_milestone": {"read"},
        "ctms_financial_payable": {"read"},
        # eTMF read-only
        "etmf_document": {"read"},
        "etmf_edl": {"read"},
        "etmf_audit_logs": {"read"},
        # Quality read-only
        "quality_event": {"read"},
        "quality_audit_logs": {"read"},
        # eISF
        "eisf_document": {"read"},
        # Execution Core Resources
        "tsdv_config": {"read"},
        "form_submission": {"read"},
        "pi_signoff": {"read"},
        "medical_coding": {"read"},
        "trial_lock": {"read"},
    },
    ROLE_EXTERNAL_MONITOR: {
        "etmf_document": {"read"},
        "etmf_edl": {"read"},
        "etmf_audit_logs": {"read"},
        "eisf_document": {"read"},
        "regulatory_form": {"read"},
        "training_log": {"read"},
        # Execution Core Resources
        "tsdv_config": {"read"},
        "form_submission": {"read"},
        "pi_signoff": {"read"},
        "trial_lock": {"read"},
    },
    "grants manager": {
        "ctms_study": {"create", "read"},
        "ctms_audit_logs": {"read"},
        "ctms_financial": {"create", "read", "update", "write"},
        "ctms_financial_budget": {"create", "read"},
        "ctms_financial_milestone": {"create", "read", "trigger"},
        "ctms_financial_payable": {"read"},
        "etmf_document": {"create", "read", "redact", "sign"},
        "etmf_edl": {"read"},
        "quality_event": {"create", "read", "update"},
        "eisf_document": {"create", "read", "update", "delete", "sync"},
    },
    "grants_manager": {
        "ctms_study": {"create", "read"},
        "ctms_audit_logs": {"read"},
        "ctms_financial": {"create", "read", "update", "write"},
        "ctms_financial_budget": {"create", "read"},
        "ctms_financial_milestone": {"create", "read", "trigger"},
        "ctms_financial_payable": {"read"},
        "etmf_document": {"create", "read", "redact", "sign"},
        "etmf_edl": {"read"},
        "quality_event": {"create", "read", "update"},
        "eisf_document": {"create", "read", "update", "delete", "sync"},
    },
    "sponsor_clinical": {
        "etmf_document": {
            "transition_clinical_qc",
            "transition_approved",
            "transition_rejected",
            "transition_draft",
            "transition_signed",
        }
    },
    "admin": {
        "study_design": {"create", "read", "update", "delete", "approve", "reorder"},
        "global_library": {"transition", "read"},
        "mdr_concept": {"read"},
        "protocol_export": {"generate", "read"},
        "subject_enrollment": {"read"},
        "ecrf_data_entry": {"read"},
        "query_lifecycle": {"create", "read", "update", "delete"},
        "system_audit_logs": {"read"},
        "export_masked": {"create", "read", "update"},
        "protocol_version": {"sign", "transition_approved"},
        "protocol_section": {"lock", "unlock", "approve", "review", "read"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # CTMS
        "ctms_study": {"create", "read"},
        "ctms_audit_logs": {"read"},
        "ctms_monitoring_visit": {"create", "update", "read", "sign_off", "sync"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"create", "read"},
        "ctms_site_milestone": {"create", "update", "read"},
        "ctms_cra_allocation": {"create", "update", "read"},
        "ctms_cra_workload": {"read"},
        "ctms_financial": {"create", "read", "update", "write"},
        "ctms_financial_budget": {"create", "read"},
        "ctms_financial_milestone": {"create", "read", "trigger"},
        "ctms_financial_payable": {"read"},
        # eTMF
        "etmf_document": {
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
            "manage_expiration",
        },
        "etmf_edl": {"read", "create"},
        # Quality
        "quality_event": {"create", "read", "update", "delete", "investigate"},
        "quality_audit_logs": {"read"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "tsdv_config": {"create", "read", "update", "delete"},
        "form_submission": {"create", "read", "update", "delete"},
        "pi_signoff": {"create", "read", "update", "delete"},
        "medical_coding": {"create", "read", "update", "delete"},
        "trial_lock": {"create", "read", "update", "delete"},
        "export_unmasked": {"create", "read", "update", "delete"},
    },
    "quality_manager": {
        "quality_event": {"create", "read", "update", "delete", "investigate"},
        "quality_audit_logs": {"read"},
    },
    "qa_lead": {
        "quality_event": {"create", "read", "update", "delete", "investigate"},
        "quality_audit_logs": {"read"},
    },
    "quality_oversight": {
        "quality_event": {"create", "read", "update", "delete", "investigate"},
        "quality_audit_logs": {"read"},
    },
    "system": {
        "ctms_study": {"create", "read"},
        "ctms_audit_logs": {"read"},
        "ctms_monitoring_visit": {"create", "update", "read", "sign_off", "sync"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"create", "read"},
        "ctms_site_milestone": {"create", "update", "read"},
        "ctms_cra_allocation": {"create", "update", "read"},
        "ctms_cra_workload": {"read"},
        "ctms_financial": {"create", "read", "update", "write"},
        "ctms_financial_budget": {"create", "read"},
        "ctms_financial_milestone": {"create", "read", "trigger"},
        "ctms_financial_payable": {"read"},
        "etmf_document": {
            "create",
            "read",
            "read_raw",
            "redact",
            "sign",
            "manage_expiration",
        },
        "etmf_edl": {"read", "create"},
        "etmf_audit_logs": {"read"},
        "quality_event": {"create", "read", "update", "delete", "investigate"},
        "quality_audit_logs": {"read"},
        "protocol_version": {"sign", "transition_approved"},
        "protocol_section": {"lock", "unlock", "approve", "review", "read"},
        "regulatory_form": {"create", "read", "sign"},
        "training_log": {"create", "read", "sign"},
        # eISF
        "eisf_document": {"create", "read", "update", "delete", "sync"},
        # Execution Core Resources
        "tsdv_config": {"create", "read", "update", "delete"},
        "form_submission": {"create", "read", "update", "delete"},
        "pi_signoff": {"create", "read", "update", "delete"},
        "medical_coding": {"create", "read", "update", "delete"},
        "trial_lock": {"create", "read", "update", "delete"},
        "export_unmasked": {"create", "read", "update", "delete"},
    },
    "anonymous": {
        "ctms_study": {"read"},
        "ctms_monitoring_visit": {"read"},
        "ctms_monitoring_letter": {"read", "read_type"},
        "ctms_recruitment": {"read"},
        "ctms_site_milestone": {"read"},
        "ctms_cra_allocation": {"read"},
        "ctms_cra_workload": {"read"},
        "etmf_document": {"read"},
        "etmf_edl": {"read"},
        "quality_event": {"read"},
        # eISF
        "eisf_document": {"read"},
    },
    ROLE_UNBLINDED_STATISTICIAN: {
        "rtsm_randomization": {"read"},
        "rtsm_allocation": {"read"},
        # Execution Core Resources
        "export_unmasked": {"create", "read", "update"},
    },
    ROLE_IDMC: {
        "rtsm_randomization": {"read"},
        "rtsm_allocation": {"read"},
    },
    ROLE_PHARMACIST: {
        "rtsm_supply": {"read", "write"},
    },
    ROLE_EMERGENCY_UNBLINDER: {
        "rtsm_unblind": {"write"},
    },
}

# Derive PI / ER-Physician / Lead-Investigator permissions from the base
# ROLE_INVESTIGATOR grant set so a single edit propagates to all three personas.
# Each derivative role adds rtsm_unblind:write (controlled by the emergency-
# unblinding endpoint) and full eISF document access beyond the base.
_PI_BASE_PERMISSIONS: dict = {
    **ROLE_PERMISSIONS[ROLE_INVESTIGATOR],
    "rtsm_unblind": {"write"},
    "eisf_document": {"create", "read", "update", "delete", "sync"},
}
ROLE_PERMISSIONS[ROLE_PRINCIPAL_INVESTIGATOR] = _PI_BASE_PERMISSIONS.copy()
ROLE_PERMISSIONS[ROLE_AUTHORIZED_ER_PHYSICIAN] = _PI_BASE_PERMISSIONS.copy()
ROLE_PERMISSIONS[ROLE_LEAD_INVESTIGATOR] = _PI_BASE_PERMISSIONS.copy()


# Field-level blinding/masking rules from §2.3
# These are applied to sensitive fields for blinded users.
MASKING_RULES: Dict[str, Callable[[Any], Any]] = {
    "initials": lambda val: "**" if val else val,
    "ssn": lambda val: "***-**-****" if val else val,
    "dob": lambda val: "MASKED" if val else val,
    "treatment_arm_id": lambda val: "BLINDED" if val else val,
    "treatment_arm": lambda val: "BLINDED" if val else val,
    "administered_drug_code": lambda val: "Obfuscated Kit" if val else val,
    "drug_code": lambda val: "Obfuscated Kit" if val else val,
    "changed_reason_for_blinded_field": lambda val: "Obfuscated" if val else val,
    "randomization_seed": lambda val: "MASKED" if val is not None else val,
    "seed": lambda val: "MASKED" if val is not None else val,
    "encrypted_allocation": lambda val: "MASKED" if val is not None else val,
    "kit_reference": lambda val: "Obfuscated Kit" if val else val,
    "stratum_key": lambda val: "MASKED" if val else val,
    "randomization_id": lambda val: "MASKED" if val else val,
    "encrypted_sequence": lambda val: "MASKED" if val else val,
}

# Canonical set of site-scoped trial personas
SITE_SCOPED_ROLES: Set[str] = {
    ROLE_INVESTIGATOR,
    ROLE_CRC,
    ROLE_CRA_CANONICAL,
    "monitor",
    ROLE_EXTERNAL_MONITOR,
    ROLE_PRINCIPAL_INVESTIGATOR,
    ROLE_AUTHORIZED_ER_PHYSICIAN,
    ROLE_LEAD_INVESTIGATOR,
}

UNMASKED_ALLOCATION_FIELDS: Set[str] = {
    "treatment_arm",
    "treatment_arm_id",
    "randomization_seed",
    "seed",
    "encrypted_allocation",
    "stratum_key",
    "randomization_id",
    "encrypted_sequence",
}

UNMASKED_SUPPLY_FIELDS: Set[str] = {
    "drug_code",
    "administered_drug_code",
    "kit_reference",
}

# Role-aware masking policy mapping unblinded RTSM roles to visible fields.
# NOTE: Routine investigator personas (PI, ER Physician, Lead Investigator) do NOT
# receive blanket unmasked-allocation or unmasked-supply grants here. Allocation
# field visibility for those roles is conferred only via the controlled emergency-
# unblinding endpoint, which returns the decrypted value directly in its response
# without persisting wide-open field visibility in the session principal.
ROLE_UNMASKED_FIELDS: Dict[str, Set[str]] = {
    ROLE_UNBLINDED_STATISTICIAN: UNMASKED_ALLOCATION_FIELDS,
    ROLE_IDMC: UNMASKED_ALLOCATION_FIELDS,
    ROLE_PHARMACIST: UNMASKED_SUPPLY_FIELDS,
    ROLE_EMERGENCY_UNBLINDER: UNMASKED_ALLOCATION_FIELDS | UNMASKED_SUPPLY_FIELDS,
}


# Traceability Note: Principal now captures sponsor scope (sponsor_id) as a contract change per ADR-86.
class Principal(BaseModel):
    user_id: str
    roles: List[str]  # Normalized canonical roles
    assigned_sites: List[str] = Field(default_factory=list)
    assigned_studies: List[str] = Field(default_factory=list)
    unblinded_access: bool = False
    sponsor_id: Optional[str] = None
    change_reason: Optional[str] = None
    raw_roles: List[str] = Field(default_factory=list)


def normalize_role(role: str) -> str:
    """Normalizes a role string to its canonical form using ROLE_ALIASES."""
    norm = role.strip().lower()
    return ROLE_ALIASES.get(norm, norm)


def has_permission(principal: Principal, permission: str) -> bool:
    """
    Checks if the principal has the specified permission.
    Permission string format: "resource:action" (e.g., "study_design:read")
    """
    if ":" not in permission:
        return False
    resource, action = permission.split(":", 1)
    resource = resource.strip().lower()
    action = action.strip().lower()

    # Determine expanded list of roles to check permissions for
    roles_to_check = list(principal.roles)
    for r in principal.raw_roles:
        norm_r = r.strip().lower()
        if norm_r not in roles_to_check:
            roles_to_check.append(norm_r)
        if norm_r in ("admin", "sponsor admin", "sponsor_admin"):
            if "admin" not in roles_to_check:
                roles_to_check.append("admin")

    for role in roles_to_check:
        perms = ROLE_PERMISSIONS.get(role, {})
        if resource in perms and action in perms[resource]:
            return True
    return False


def can_access_site(principal: Principal, site_id: str) -> bool:
    """
    Determine whether a principal is permitted to access a given site.

    Site-scoped roles (e.g., investigators, CRCs, CRAs, ER physicians, lead
    investigators) are restricted to the sites listed in *principal.assigned_sites*.
    Sponsor/SysAdmin principals with an empty *assigned_sites* list are granted
    global access. The function is fail-closed: a site-scoped user with no
    assigned sites is denied access everywhere.

    Args:
        principal: The authenticated principal making the request.
        site_id: The site identifier to check access for.

    Returns:
        True if the principal may access the site; False otherwise.
    """
    user_site_roles = [r for r in principal.roles if r in SITE_SCOPED_ROLES]

    # Fail-closed handling for missing/empty site_id on legacy/study-level rows
    if site_id is None or str(site_id).strip() == "":
        if user_site_roles or principal.assigned_sites:
            return False
        return True

    if user_site_roles:
        return site_id in principal.assigned_sites

    if principal.assigned_sites:
        return site_id in principal.assigned_sites

    return True


def can_access_study(principal: Principal, study_id: str) -> bool:
    """
    Checks if the principal has access to a specific study.
    Study-scoped users are restricted to their assigned_studies.
    """
    if study_id is None or str(study_id).strip() == "":
        if "external_monitor" in principal.roles or principal.assigned_studies:
            return False
        return True

    if "external_monitor" in principal.roles:
        return study_id in principal.assigned_studies
    if principal.assigned_studies:
        return study_id in principal.assigned_studies
    return True


def get_principal_sync(request: Request) -> Principal:
    """
    Synchronous helper to extract identity and authorization attributes
    from request context, query parameters, and headers, returning a normalized Principal.
    """
    # 1. User ID
    user_id = ""
    if hasattr(request, "state"):
        user_id = getattr(request.state, "user_id", None) or ""
    if not user_id and hasattr(request, "headers"):
        user_id = (
            request.headers.get("X-User-Id") or request.headers.get("x-user-id") or ""
        )

    # 2. Roles (raw)
    roles_val = None
    if hasattr(request, "state"):
        roles_val = getattr(request.state, "roles", None)
    if roles_val is None and hasattr(request, "headers"):
        roles_val = (
            request.headers.get("X-User-Roles")
            or request.headers.get("x-user-roles")
            or ""
        )

    if isinstance(roles_val, str):
        raw_roles = [r.strip().lower() for r in roles_val.split(",") if r.strip()]
        raw_roles_list = [r.strip() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, list):
        raw_roles = [str(r).strip().lower() for r in roles_val if str(r).strip()]
        raw_roles_list = [str(r).strip() for r in roles_val if str(r).strip()]
    else:
        raw_roles = []
        raw_roles_list = []

    normalized_roles = [normalize_role(r) for r in raw_roles]

    if ROLE_EXTERNAL_MONITOR in normalized_roles:
        raise HTTPException(
            status_code=500,
            detail="External Monitor principal must be resolved via the async get_principal path to allow directory enrichment.",
        )

    # 3. Assigned Sites
    site_id_val = None
    if hasattr(request, "state"):
        site_id_val = getattr(request.state, "site_id", None)
    if site_id_val is None and hasattr(request, "headers"):
        site_id_val = (
            request.headers.get("X-Site-Id")
            or request.headers.get("x-site-id")
            or request.headers.get("X-User-Site")
            or ""
        )

    assigned_sites = []
    if site_id_val:
        assigned_sites = [s.strip() for s in site_id_val.split(",") if s.strip()]

    # 3.5. Sponsor ID
    sponsor_id_val = None
    if hasattr(request, "state"):
        sponsor_id_val = getattr(request.state, "sponsor_id", None)
    if sponsor_id_val is None and hasattr(request, "headers"):
        sponsor_id_val = (
            request.headers.get("X-Sponsor-Id")
            or request.headers.get("x-sponsor-id")
            or ""
        )

    sponsor_id = None
    if sponsor_id_val:
        if isinstance(sponsor_id_val, list):
            sponsor_id = ",".join(
                str(s).strip() for s in sponsor_id_val if str(s).strip()
            )
        else:
            sponsor_id = ",".join(
                s.strip() for s in str(sponsor_id_val).split(",") if s.strip()
            )
        if not sponsor_id:
            sponsor_id = None

    # 4. Unblinded status
    unblinded_access = False
    if hasattr(request, "headers"):
        unblinded_header = (
            request.headers.get("X-Unblinded-Access")
            or request.headers.get("x-unblinded-access")
            or ""
        )
        if unblinded_header.lower() in ("true", "1", "yes"):
            unblinded_access = True
    if (
        not unblinded_access
        and hasattr(request, "state")
        and hasattr(request.state, "unblinded_access")
    ):
        unblinded_access = bool(request.state.unblinded_access)

    # 5. Change reason (State, query parameters, headers)
    change_reason = None

    # State
    if hasattr(request, "state"):
        change_reason = getattr(request.state, "change_reason", None) or getattr(
            request.state, "reason_for_change", None
        )
        if change_reason:
            change_reason = str(change_reason).strip()

    # Query Parameters
    if not change_reason:
        try:
            if hasattr(request, "query_params") and request.query_params:
                for key in ("change_reason", "reason_for_change", "reason"):
                    val = request.query_params.get(key)
                    if val and str(val).strip():
                        change_reason = str(val).strip()
                        break
        except Exception:
            pass

    # Headers
    if not change_reason and hasattr(request, "headers") and request.headers:
        for key in (
            "X-Change-Reason",
            "x-change-reason",
            "X-Reason-For-Change",
            "x-reason-for-change",
            "Reason-For-Change",
            "reason-for-change",
        ):
            val = request.headers.get(key)
            if val and str(val).strip():
                change_reason = str(val).strip()
                break

    return Principal(
        user_id=user_id,
        roles=normalized_roles,
        assigned_sites=assigned_sites,
        unblinded_access=unblinded_access,
        sponsor_id=sponsor_id,
        change_reason=change_reason,
        raw_roles=raw_roles_list,
    )


async def get_principal(request: Request) -> Principal:
    """
    FastAPI dependency to extract identity and authorization attributes
    from request context and headers, returning a normalized Principal.
    """
    import json

    # Bypass sync exception if we are inside get_principal by extracting manually
    user_id = ""
    if hasattr(request, "state"):
        user_id = getattr(request.state, "user_id", None) or ""
    if not user_id and hasattr(request, "headers"):
        user_id = (
            request.headers.get("X-User-Id") or request.headers.get("x-user-id") or ""
        )

    roles_val = None
    if hasattr(request, "state"):
        roles_val = getattr(request.state, "roles", None)
    if roles_val is None and hasattr(request, "headers"):
        roles_val = (
            request.headers.get("X-User-Roles")
            or request.headers.get("x-user-roles")
            or ""
        )

    if isinstance(roles_val, str):
        raw_roles = [r.strip().lower() for r in roles_val.split(",") if r.strip()]
        raw_roles_list = [r.strip() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, list):
        raw_roles = [str(r).strip().lower() for r in roles_val if str(r).strip()]
        raw_roles_list = [str(r).strip() for r in roles_val if str(r).strip()]
    else:
        raw_roles = []
        raw_roles_list = []

    normalized_roles = [normalize_role(r) for r in raw_roles]

    site_id_val = None
    if hasattr(request, "state"):
        site_id_val = getattr(request.state, "site_id", None)
    if site_id_val is None and hasattr(request, "headers"):
        site_id_val = (
            request.headers.get("X-Site-Id")
            or request.headers.get("x-site-id")
            or request.headers.get("X-User-Site")
            or ""
        )

    assigned_sites = []
    if site_id_val:
        assigned_sites = [s.strip() for s in site_id_val.split(",") if s.strip()]

    # 3.5. Sponsor ID
    sponsor_id_val = None
    if hasattr(request, "state"):
        sponsor_id_val = getattr(request.state, "sponsor_id", None)
    if sponsor_id_val is None and hasattr(request, "headers"):
        sponsor_id_val = (
            request.headers.get("X-Sponsor-Id")
            or request.headers.get("x-sponsor-id")
            or ""
        )

    sponsor_id = None
    if sponsor_id_val:
        if isinstance(sponsor_id_val, list):
            sponsor_id = ",".join(
                str(s).strip() for s in sponsor_id_val if str(s).strip()
            )
        else:
            sponsor_id = ",".join(
                s.strip() for s in str(sponsor_id_val).split(",") if s.strip()
            )
        if not sponsor_id:
            sponsor_id = None

    unblinded_access = False
    if hasattr(request, "headers"):
        unblinded_header = (
            request.headers.get("X-Unblinded-Access")
            or request.headers.get("x-unblinded-access")
            or ""
        )
        if unblinded_header.lower() in ("true", "1", "yes"):
            unblinded_access = True
    if (
        not unblinded_access
        and hasattr(request, "state")
        and hasattr(request.state, "unblinded_access")
    ):
        unblinded_access = bool(request.state.unblinded_access)

    change_reason = None
    if hasattr(request, "state"):
        change_reason = getattr(request.state, "change_reason", None) or getattr(
            request.state, "reason_for_change", None
        )
        if change_reason:
            change_reason = str(change_reason).strip()

    if not change_reason:
        try:
            if hasattr(request, "query_params") and request.query_params:
                for key in ("change_reason", "reason_for_change", "reason"):
                    val = request.query_params.get(key)
                    if val and str(val).strip():
                        change_reason = str(val).strip()
                        break
        except Exception:
            pass

    if not change_reason and hasattr(request, "headers") and request.headers:
        for key in (
            "X-Change-Reason",
            "x-change-reason",
            "X-Reason-For-Change",
            "x-reason-for-change",
            "Reason-For-Change",
            "reason-for-change",
        ):
            val = request.headers.get(key)
            if val and str(val).strip():
                change_reason = str(val).strip()
                break

    principal = Principal(
        user_id=user_id,
        roles=normalized_roles,
        assigned_sites=assigned_sites,
        unblinded_access=unblinded_access,
        sponsor_id=sponsor_id,
        change_reason=change_reason,
        raw_roles=raw_roles_list,
    )

    if ROLE_EXTERNAL_MONITOR in principal.roles:
        from packages.security.org_client import resolve_personnel_assignments

        res = await resolve_personnel_assignments(principal.user_id)
        if res:
            principal.assigned_sites = res.get("assigned_sites", [])
            principal.assigned_studies = res.get("assigned_studies", [])

    # If change_reason is not found yet, and it is a write operation, check body
    if (
        not principal.change_reason
        and hasattr(request, "method")
        and request.method in ("POST", "PUT", "PATCH")
    ):
        try:
            content_type = (
                request.headers.get("content-type", "")
                if hasattr(request, "headers")
                else ""
            )
            if "application/json" in content_type:
                body = await request.body()
                if body:
                    body_json = json.loads(body)

                    def find_reason_in_dict(d: dict) -> Optional[str]:
                        for key in ("reason_for_change", "change_reason", "reason"):
                            if key in d and isinstance(d[key], str) and d[key].strip():
                                return d[key].strip()
                        for v in d.values():
                            if isinstance(v, dict):
                                res = find_reason_in_dict(v)
                                if res:
                                    return res
                        return None

                    if isinstance(body_json, dict):
                        principal.change_reason = find_reason_in_dict(body_json)

                # Reset receive stream so downstream route can read it again
                async def receive():
                    return {"type": "http.request", "body": body, "more_body": False}

                request._receive = receive
            elif (
                "application/x-www-form-urlencoded" in content_type
                or "multipart/form-data" in content_type
            ):
                form = await request.form()
                for key in ("reason_for_change", "change_reason", "reason"):
                    val = form.get(key)
                    if val and str(val).strip():
                        principal.change_reason = str(val).strip()
                        break
        except Exception:
            pass

    # Ensure change_reason is clean
    if principal.change_reason:
        principal.change_reason = principal.change_reason.strip()

    # Reject write operations with a descriptive error if the resolved change justification is missing
    if hasattr(request, "method") and request.method in (
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
    ):
        if not principal.change_reason or not principal.change_reason.strip():
            raise HTTPException(
                status_code=403,
                detail="Missing change justification reason",
            )

    return principal


def require_permission(permission: str) -> Callable[[Principal], Principal]:
    """
    FastAPI dependency factory to assert that the caller has a required permission.
    """

    def dependency(principal: Principal = Depends(get_principal)) -> Principal:
        if not has_permission(principal, permission):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Forbidden: Insufficient permissions for {permission}.",
            )
        return principal

    return dependency


def mask_payload(payload: Any, principal: Principal) -> Any:
    """Recursively mask sensitive fields in dictionaries, lists, or Pydantic models based on principal authorization.

    If principal.unblinded_access is True, no masking is performed and the original payload is returned unchanged.
    Otherwise, if the principal possesses unblinded RTSM roles configured in ROLE_UNMASKED_FIELDS, field-level
    masking rules are bypassed for fields included in the principal's unmasked set.

    Args:
        payload: The target data structure (dict, list, or Pydantic BaseModel instance) to mask.
        principal: The authenticated Principal whose roles and unblinded_access status govern field masking.

    Returns:
        The recursively masked data structure or dictionary.

    Raises:
        ValueError: If payload structure cannot be processed.
    """
    if principal.unblinded_access:
        return payload

    # Find if any RTSM role-specific policies apply
    rtsm_roles = [r for r in principal.roles if r in ROLE_UNMASKED_FIELDS]
    if rtsm_roles:
        # Union of all unmasked fields for their active RTSM roles
        unmasked_fields: Set[str] = set()
        for r in rtsm_roles:
            unmasked_fields.update(ROLE_UNMASKED_FIELDS[r])
        return _recursive_mask(payload, unmasked_fields=unmasked_fields)

    return _recursive_mask(payload)


def _recursive_mask(data: Any, unmasked_fields: Optional[Set[str]] = None) -> Any:
    if data is None:
        return None

    if isinstance(data, pydantic.BaseModel):
        dumped = data.model_dump()
        masked = _recursive_mask(dumped, unmasked_fields)
        try:
            return data.__class__.model_validate(masked)
        except Exception:
            return masked

    if isinstance(data, dict):
        new_dict = {}
        for k, v in data.items():
            k_lower = k.lower()
            if k_lower in MASKING_RULES and (
                unmasked_fields is None or k_lower not in unmasked_fields
            ):
                new_dict[k] = MASKING_RULES[k_lower](v)
            else:
                new_dict[k] = _recursive_mask(v, unmasked_fields)
        return new_dict

    if isinstance(data, list):
        return [_recursive_mask(item, unmasked_fields) for item in data]

    if isinstance(data, tuple):
        return tuple(_recursive_mask(item, unmasked_fields) for item in data)

    if isinstance(data, set):
        return {_recursive_mask(item, unmasked_fields) for item in data}

    return data


def get_normalized_roles(request: Request) -> list[str]:
    """
    Retrieves and normalizes request.state.roles or raw X-User-Roles headers.
    Updates request.state.roles to be a list of lowercase, stripped strings.
    """
    roles_val = getattr(request.state, "roles", None)
    if roles_val is None:
        roles_val = request.headers.get("X-User-Roles", "")

    if isinstance(roles_val, str):
        normalized = [r.strip().lower() for r in roles_val.split(",") if r.strip()]
    elif isinstance(roles_val, list):
        normalized = [str(r).strip().lower() for r in roles_val if str(r).strip()]
    else:
        normalized = []

    request.state.roles = normalized
    return normalized


def verify_not_auditor(request: Request) -> list[str]:
    """
    FastAPI dependency to verify that the request does not originate from an auditor persona.
    Raises HTTP 403 Forbidden if any auditor roles are detected.
    """
    roles = get_normalized_roles(request)
    if any(role in AUDITOR_ROLES for role in roles):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Auditor personas are restricted to read-only access.",
        )
    return roles


def verify_is_auditor(request: Request) -> list[str]:
    """
    FastAPI dependency to verify that the request is made by an authorized auditor persona.
    Raises HTTP 403 Forbidden if no authorized auditor/inspection roles are detected.
    """
    roles = get_normalized_roles(request)
    if not any(role in AUDITOR_ROLES for role in roles):
        raise HTTPException(
            status_code=403,
            detail="Forbidden: Access is restricted to authorized auditor/inspection roles.",
        )
    return roles


ROLE_EXPANSIONS = {
    "site investigator": {
        "site investigator",
        "investigator",
        "site-investigator",
        "site_investigator",
        "investigator_user",
        "principal_investigator",
        "principal investigator",
        "pi",
        "authorized_er_physician",
        "authorized er physician",
        "lead_investigator",
        "lead investigator",
    },
    "principal_investigator": {
        "principal_investigator",
        "principal investigator",
        "pi",
        "principalinvestigator",
    },
    "authorized_er_physician": {
        "authorized_er_physician",
        "authorized er physician",
        "authorized-er-physician",
    },
    "lead_investigator": {
        "lead_investigator",
        "lead investigator",
        "lead-investigator",
    },
    "data manager": {
        "data manager",
        "data_manager",
        "data-manager",
        "sponsor_dm",
        "dm",
        "admin",
    },
    "cra": {"cra"},
    "auditor": {"auditor", "inspector", "regulatory_inspector"},
    "sponsor admin": {"sponsor admin", "sponsor_admin", "admin"},
    "external_monitor": {
        "external_monitor",
        "external monitor",
        "external-monitor",
        "cro monitor",
        "cro_monitor",
        "cro-monitor",
    },
    "protocol_reviewer": {
        "protocol_reviewer",
        "protocol reviewer",
        "protocol-reviewer",
        "reviewer",
    },
    "unblinded_statistician": {
        "unblinded_statistician",
        "unblinded statistician",
        "lead unblinded statistician",
    },
    "idmc": {
        "idmc",
        "dsmb",
    },
    "pharmacist": {
        "pharmacist",
        "unblinded pharmacist",
        "unblinded_pharmacist",
    },
    "emergency_unblinder": {
        "emergency_unblinder",
        "emergency unblinder",
    },
}


def require_roles(*allowed_roles: str, detail: Optional[str] = None):
    """
    FastAPI dependency factory to enforce that the caller has at least one of the allowed roles.
    Allows case-insensitive, whitespace-insensitive matches and role synonym expansion.
    """

    def dependency(request: Request) -> list[str]:
        raw_roles = get_normalized_roles(request)
        roles = []
        for r in raw_roles:
            norm_r = r.strip().lower()
            if norm_r in ("sponsor admin", "sponsor_admin"):
                roles.append("sponsor_admin")
            else:
                roles.append(normalize_role(r))
        expanded_allowed = set()
        for role in allowed_roles:
            norm_role = role.strip().lower()
            # Normalize allowed roles as well so we can compare canonical forms
            norm_role_canonical = (
                "sponsor_admin"
                if norm_role in ("sponsor admin", "sponsor_admin")
                else normalize_role(norm_role)
            )
            expanded_allowed.add(norm_role_canonical)
            if norm_role_canonical in ROLE_EXPANSIONS:
                expanded_allowed.update(ROLE_EXPANSIONS[norm_role_canonical])
            if norm_role in ROLE_EXPANSIONS:
                expanded_allowed.update(ROLE_EXPANSIONS[norm_role])

        if not any(role in expanded_allowed for role in roles):
            raise HTTPException(
                status_code=403,
                detail=detail or "User role is not authorized for this action.",
            )
        return roles

    return dependency


def require_role(
    required_role: str, detail: Optional[str] = None
) -> Callable[[Request], list[str]]:
    """
    FastAPI dependency factory to enforce that the caller has the required role.
    Reads request.state.roles, normalizes the comma-separated string, and raises 403 when the required role is absent.
    """

    def dependency(request: Request) -> list[str]:
        roles = get_normalized_roles(request)
        norm_required = normalize_role(required_role.strip().lower())

        expanded_allowed = {norm_required}
        if norm_required in ROLE_EXPANSIONS:
            expanded_allowed.update(ROLE_EXPANSIONS[norm_required])

        normalized_req_roles = [normalize_role(r) for r in roles]

        if not any(r in expanded_allowed for r in normalized_req_roles):
            raise HTTPException(
                status_code=403,
                detail=detail
                or f"User role is not authorized for this action. Required: {required_role}.",
            )
        return roles

    return dependency


def require_any_role(
    *allowed_roles: str, detail: Optional[str] = None
) -> Callable[[Request], list[str]]:
    """
    FastAPI dependency factory to enforce that the caller has at least one of the allowed roles.
    Reads request.state.roles, normalizes the comma-separated string, and raises 403 when required roles are absent.
    """
    return require_roles(*allowed_roles, detail=detail)


def is_auditor(request: Request) -> bool:
    """
    Read-only helper to check if the request is associated with any read-only auditor persona.
    """
    roles = get_normalized_roles(request)
    return any(role in AUDITOR_ROLES for role in roles)
