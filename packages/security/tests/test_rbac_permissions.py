"""Unit tests for Granular RBAC Permission Matrix and Role-Based Authorization.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

from packages.security.permissions import (
    PermissionEnum,
    RoleEnum,
    get_permissions_for_role,
    get_permissions_for_roles,
    has_permission,
    normalize_role_name,
)


def test_permission_matrix_enum_values():
    """Validate core PermissionEnum values and string representations.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    assert PermissionEnum.STUDY_READ == "study:read"
    assert PermissionEnum.FORM_WRITE == "form:write"
    assert PermissionEnum.DATA_LOCK == "data:lock"
    assert PermissionEnum.SDV_VERIFY == "sdv:verify"
    assert PermissionEnum.AUDIT_VIEW == "audit:view"
    assert PermissionEnum.PROTOCOL_AUTHOR == "protocol:author"
    assert PermissionEnum.GLOBAL_LIBRARY_MANAGE == "global_library:manage"
    assert PermissionEnum.SUBJECT_ENROLL == "subject:enroll"
    assert PermissionEnum.EXPORT_SDTM == "export:sdtm"
    assert PermissionEnum.ESIGN_EXECUTE == "esign:execute"


def test_role_enum_canonical_names():
    """Validate RoleEnum canonical values.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    assert RoleEnum.SPONSOR_ADMIN == "SponsorAdmin"
    assert RoleEnum.SPONSOR_DESIGNER == "SponsorDesigner"
    assert RoleEnum.PRINCIPAL_INVESTIGATOR == "PrincipalInvestigator"
    assert RoleEnum.CRC == "ClinicalResearchCoordinator"
    assert RoleEnum.CRA == "ClinicalResearchAssociate"
    assert RoleEnum.DATA_MANAGER == "DataManager"
    assert RoleEnum.AUDITOR == "Auditor"
    assert RoleEnum.SUBJECT == "Subject"


def test_normalize_role_name():
    """Validate role string normalization and alias resolution.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    assert normalize_role_name("SponsorAdmin") == "SponsorAdmin"
    assert normalize_role_name("pi") == "PrincipalInvestigator"
    assert normalize_role_name("crc") == "ClinicalResearchCoordinator"
    assert normalize_role_name("cra") == "ClinicalResearchAssociate"
    assert normalize_role_name("dm") == "DataManager"
    assert normalize_role_name("auditor") == "Auditor"
    assert normalize_role_name("patient") == "Subject"


def test_get_permissions_for_role_sponsor_admin():
    """Verify SponsorAdmin role permissions matrix.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    perms = get_permissions_for_role("SponsorAdmin")
    assert PermissionEnum.STUDY_READ in perms
    assert PermissionEnum.PROTOCOL_AUTHOR in perms
    assert PermissionEnum.GLOBAL_LIBRARY_MANAGE in perms
    assert PermissionEnum.EXPORT_SDTM in perms
    assert PermissionEnum.CHANGE_REQUEST_APPROVE in perms
    assert PermissionEnum.ESIGN_EXECUTE in perms
    # SponsorAdmin does NOT have raw CRA SDV or form data entry permissions
    assert PermissionEnum.SDV_VERIFY not in perms


def test_get_permissions_for_role_cra():
    """Verify ClinicalResearchAssociate (CRA) role permissions.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    perms = get_permissions_for_role("CRA")
    assert PermissionEnum.STUDY_READ in perms
    assert PermissionEnum.SDV_VERIFY in perms
    assert PermissionEnum.QUERY_MANAGE in perms
    assert PermissionEnum.AUDIT_VIEW in perms
    assert PermissionEnum.FORM_WRITE not in perms
    assert PermissionEnum.DATA_LOCK not in perms


def test_get_permissions_for_role_data_manager():
    """Verify DataManager role permissions matrix.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    perms = get_permissions_for_role("DataManager")
    assert PermissionEnum.STUDY_READ in perms
    assert PermissionEnum.DATA_LOCK in perms
    assert PermissionEnum.DATA_UNLOCK in perms
    assert PermissionEnum.EXPORT_SDTM in perms
    assert PermissionEnum.AUDIT_VIEW in perms


def test_get_permissions_for_roles_aggregated():
    """Verify aggregated permissions when a user possesses multiple roles.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    roles = ["CRC", "CRA"]
    aggregated = get_permissions_for_roles(roles)
    assert PermissionEnum.FORM_WRITE in aggregated  # from CRC
    assert PermissionEnum.SDV_VERIFY in aggregated  # from CRA
    assert PermissionEnum.STUDY_READ in aggregated


def test_has_permission_checks():
    """Verify has_permission helper evaluation across role inputs.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    assert has_permission("pi", PermissionEnum.FORM_WRITE) is True
    assert has_permission("pi", PermissionEnum.ESIGN_EXECUTE) is True
    assert has_permission("pi", PermissionEnum.DATA_LOCK) is False

    assert has_permission(["auditor"], PermissionEnum.AUDIT_VIEW) is True
    assert has_permission(["auditor"], PermissionEnum.FORM_WRITE) is False

    assert has_permission(["dm", "crc"], PermissionEnum.DATA_LOCK) is True


def test_unknown_role_returns_empty_permissions():
    """Verify unrecognized roles receive an empty permission set.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    assert get_permissions_for_role("NonExistentRole") == set()
    assert has_permission("NonExistentRole", PermissionEnum.STUDY_READ) is False


def test_soa_permissions_definitions():
    """Validate that the new SoA permissions are correctly defined.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    assert PermissionEnum.SOA_READ == "soa:read"
    assert PermissionEnum.SOA_MANAGE == "soa:manage"


def test_soa_permissions_matrix_mapping():
    """Verify the mapping of SOA_READ and SOA_MANAGE across canonical roles.

    Requirements: PRD-SYS-001, 21 CFR Part 11
    """
    # SponsorAdmin holds both SOA_READ and SOA_MANAGE
    admin_perms = get_permissions_for_role("SponsorAdmin")
    assert PermissionEnum.SOA_READ in admin_perms
    assert PermissionEnum.SOA_MANAGE in admin_perms

    # SponsorDesigner holds both SOA_READ and SOA_MANAGE
    designer_perms = get_permissions_for_role("SponsorDesigner")
    assert PermissionEnum.SOA_READ in designer_perms
    assert PermissionEnum.SOA_MANAGE in designer_perms

    # PrincipalInvestigator, CRC, CRA, DataManager, and Auditor hold only SOA_READ
    read_only_roles = [
        "PrincipalInvestigator",
        "ClinicalResearchCoordinator",
        "ClinicalResearchAssociate",
        "DataManager",
        "Auditor",
    ]
    for role in read_only_roles:
        perms = get_permissions_for_role(role)
        assert PermissionEnum.SOA_READ in perms
        assert PermissionEnum.SOA_MANAGE not in perms

    # Subject role does NOT have SOA_READ or SOA_MANAGE
    subject_perms = get_permissions_for_role("Subject")
    assert PermissionEnum.SOA_READ not in subject_perms
    assert PermissionEnum.SOA_MANAGE not in subject_perms
