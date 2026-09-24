"""Integration test suite for Granular Role-Based Access Control (RBAC) Enforcement.

Validates permissions matrix, site isolation, and field-level data masking across system roles.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

from apps.execution.field_masking import (
    MASKED_REPLACEMENT_TEXT,
    mask_clinical_record,
    mask_clinical_records_list,
)
from packages.security.permissions import (
    PermissionEnum,
    RoleEnum,
    get_permissions_for_role,
)


def test_rbac_sponsor_admin_authorization():
    """Verify SponsorAdmin authorization and permission scope.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    admin_perms = get_permissions_for_role(RoleEnum.SPONSOR_ADMIN.value)
    assert PermissionEnum.STUDY_READ in admin_perms
    assert PermissionEnum.PROTOCOL_AUTHOR in admin_perms
    assert PermissionEnum.GLOBAL_LIBRARY_MANAGE in admin_perms
    assert PermissionEnum.EXPORT_SDTM in admin_perms
    assert PermissionEnum.CHANGE_REQUEST_APPROVE in admin_perms
    assert PermissionEnum.SDV_VERIFY not in admin_perms


def test_rbac_cra_monitoring_authorization():
    """Verify CRA monitoring permission scope.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    cra_perms = get_permissions_for_role(RoleEnum.CRA.value)
    assert PermissionEnum.SDV_VERIFY in cra_perms
    assert PermissionEnum.QUERY_MANAGE in cra_perms
    assert PermissionEnum.AUDIT_VIEW in cra_perms
    assert PermissionEnum.FORM_WRITE not in cra_perms
    assert PermissionEnum.DATA_LOCK not in cra_perms


def test_rbac_data_manager_lock_authorization():
    """Verify DataManager data locking permission scope.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    dm_perms = get_permissions_for_role(RoleEnum.DATA_MANAGER.value)
    assert PermissionEnum.DATA_LOCK in dm_perms
    assert PermissionEnum.DATA_UNLOCK in dm_perms
    assert PermissionEnum.EXPORT_SDTM in dm_perms
    assert PermissionEnum.FORM_WRITE not in dm_perms


def test_field_level_masking_blinded_user():
    """Verify field-level masking masks blinded treatment fields when caller lacks unblinded access.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    raw_record = {
        "subject_id": "SUBJ-1001",
        "visit_name": "Baseline",
        "treatment_arm": "Active Drug 100mg",
        "kit_number": "KIT-998822",
        "unblinded_dose": "100mg BID",
        "systolic_bp": 120,
    }

    # Blinded CRA caller (no EXPERT_UNBLIND permission)
    cra_perms = get_permissions_for_role("CRA")
    masked = mask_clinical_record(raw_record, cra_perms, unblinded_access=False)

    assert masked["subject_id"] == "SUBJ-1001"
    assert masked["systolic_bp"] == 120
    assert masked["treatment_arm"] == MASKED_REPLACEMENT_TEXT
    assert masked["kit_number"] == MASKED_REPLACEMENT_TEXT
    assert masked["unblinded_dose"] == MASKED_REPLACEMENT_TEXT


def test_field_level_masking_unblinded_user():
    """Verify field-level masking preserves unblinded treatment data when caller has unblinded access.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    raw_record = {
        "subject_id": "SUBJ-1001",
        "treatment_arm": "Active Drug 100mg",
        "kit_number": "KIT-998822",
    }

    # PI caller with explicit unblinded_access = True
    pi_perms = get_permissions_for_role("PI")
    unmasked = mask_clinical_record(raw_record, pi_perms, unblinded_access=True)

    assert unmasked["treatment_arm"] == "Active Drug 100mg"
    assert unmasked["kit_number"] == "KIT-998822"


def test_field_level_masking_pii_fields():
    """Verify PII fields are always masked regardless of role.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    raw_record = {
        "subject_id": "SUBJ-1001",
        "first_name": "John",
        "last_name": "Doe",
        "phone_number": "555-0199",
        "systolic_bp": 118,
    }

    dm_perms = get_permissions_for_role("DataManager")
    masked = mask_clinical_record(raw_record, dm_perms, unblinded_access=True)

    assert masked["first_name"] == MASKED_REPLACEMENT_TEXT
    assert masked["last_name"] == MASKED_REPLACEMENT_TEXT
    assert masked["phone_number"] == MASKED_REPLACEMENT_TEXT
    assert masked["systolic_bp"] == 118


def test_mask_clinical_records_list():
    """Verify batch masking over lists of records.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    records = [
        {"subject_id": "SUBJ-1", "treatment_arm": "Drug A"},
        {"subject_id": "SUBJ-2", "treatment_arm": "Placebo"},
    ]

    crc_perms = get_permissions_for_role("CRC")
    masked_list = mask_clinical_records_list(records, crc_perms, unblinded_access=False)

    assert len(masked_list) == 2
    assert masked_list[0]["treatment_arm"] == MASKED_REPLACEMENT_TEXT
    assert masked_list[1]["treatment_arm"] == MASKED_REPLACEMENT_TEXT
