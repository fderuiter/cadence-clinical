"""Granular Permission Matrix and Role-Based Authorization Definitions.

Provides fine-grained permission enums (STUDY_READ, FORM_WRITE, DATA_LOCK, SDV_VERIFY,
AUDIT_VIEW, etc.) and role-to-permission mapping for Cadence Clinical eClinical platform.

Requirements: PRD-SYS-001, 21 CFR Part 11
"""

import enum
from typing import Dict, List, Set, Union


class PermissionEnum(str, enum.Enum):
    """Granular permission definitions across Cadence Clinical platform."""

    # Core Read & Audit Permissions
    STUDY_READ = "study:read"
    AUDIT_VIEW = "audit:view"

    # Form & Data Capture Permissions
    FORM_WRITE = "form:write"
    FORM_LOCK = "form:lock"
    DATA_LOCK = "data:lock"
    DATA_UNLOCK = "data:unlock"

    # Monitoring & Verification Permissions
    SDV_VERIFY = "sdv:verify"
    QUERY_MANAGE = "query:manage"

    # Protocol Authoring & Global Library Permissions
    PROTOCOL_AUTHOR = "protocol:author"
    GLOBAL_LIBRARY_MANAGE = "global_library:manage"

    # Clinical Execution & RTSM Permissions
    SUBJECT_ENROLL = "subject:enroll"
    RTSM_RANDOMIZE = "rtsm:randomize"
    EXPERT_UNBLIND = "expert:unblind"
    SAE_REPORT = "sae:report"

    # Export & eSignature Permissions
    EXPORT_SDTM = "export:sdtm"
    ESIGN_EXECUTE = "esign:execute"
    CHANGE_REQUEST_APPROVE = "change_request:approve"

    # Document & Archival Permissions
    DOCUMENTS_READ = "documents:read"
    DOCUMENTS_WRITE = "documents:write"
    ARCHIVE_EXPORT = "archive:export"


class RoleEnum(str, enum.Enum):
    """Canonical system roles within Cadence Clinical eClinical platform."""

    SPONSOR_ADMIN = "SponsorAdmin"
    SPONSOR_DESIGNER = "SponsorDesigner"
    PRINCIPAL_INVESTIGATOR = "PrincipalInvestigator"
    CRC = "ClinicalResearchCoordinator"
    CRA = "ClinicalResearchAssociate"
    DATA_MANAGER = "DataManager"
    AUDITOR = "Auditor"
    SUBJECT = "Subject"


# Canonical Role to Permission Matrix Mapping
ROLE_PERMISSIONS_MAP: Dict[str, Set[PermissionEnum]] = {
    RoleEnum.SPONSOR_ADMIN.value: {
        PermissionEnum.STUDY_READ,
        PermissionEnum.AUDIT_VIEW,
        PermissionEnum.PROTOCOL_AUTHOR,
        PermissionEnum.GLOBAL_LIBRARY_MANAGE,
        PermissionEnum.EXPORT_SDTM,
        PermissionEnum.CHANGE_REQUEST_APPROVE,
        PermissionEnum.ESIGN_EXECUTE,
        PermissionEnum.DOCUMENTS_READ,
        PermissionEnum.DOCUMENTS_WRITE,
        PermissionEnum.ARCHIVE_EXPORT,
    },
    RoleEnum.SPONSOR_DESIGNER.value: {
        PermissionEnum.STUDY_READ,
        PermissionEnum.PROTOCOL_AUTHOR,
        PermissionEnum.GLOBAL_LIBRARY_MANAGE,
    },
    RoleEnum.PRINCIPAL_INVESTIGATOR.value: {
        PermissionEnum.STUDY_READ,
        PermissionEnum.FORM_WRITE,
        PermissionEnum.SUBJECT_ENROLL,
        PermissionEnum.RTSM_RANDOMIZE,
        PermissionEnum.EXPERT_UNBLIND,
        PermissionEnum.SAE_REPORT,
        PermissionEnum.ESIGN_EXECUTE,
        PermissionEnum.QUERY_MANAGE,
        PermissionEnum.DOCUMENTS_READ,
        PermissionEnum.DOCUMENTS_WRITE,
    },
    RoleEnum.CRC.value: {
        PermissionEnum.STUDY_READ,
        PermissionEnum.FORM_WRITE,
        PermissionEnum.SUBJECT_ENROLL,
        PermissionEnum.RTSM_RANDOMIZE,
        PermissionEnum.SAE_REPORT,
        PermissionEnum.QUERY_MANAGE,
        PermissionEnum.DOCUMENTS_READ,
        PermissionEnum.DOCUMENTS_WRITE,
    },
    RoleEnum.CRA.value: {
        PermissionEnum.STUDY_READ,
        PermissionEnum.SDV_VERIFY,
        PermissionEnum.QUERY_MANAGE,
        PermissionEnum.AUDIT_VIEW,
        PermissionEnum.DOCUMENTS_READ,
        PermissionEnum.DOCUMENTS_WRITE,
    },
    RoleEnum.DATA_MANAGER.value: {
        PermissionEnum.STUDY_READ,
        PermissionEnum.FORM_LOCK,
        PermissionEnum.DATA_LOCK,
        PermissionEnum.DATA_UNLOCK,
        PermissionEnum.QUERY_MANAGE,
        PermissionEnum.EXPORT_SDTM,
        PermissionEnum.AUDIT_VIEW,
        PermissionEnum.DOCUMENTS_READ,
        PermissionEnum.DOCUMENTS_WRITE,
        PermissionEnum.ARCHIVE_EXPORT,
    },
    RoleEnum.AUDITOR.value: {
        PermissionEnum.STUDY_READ,
        PermissionEnum.AUDIT_VIEW,
        PermissionEnum.DOCUMENTS_READ,
        PermissionEnum.ARCHIVE_EXPORT,
    },
    RoleEnum.SUBJECT.value: {
        PermissionEnum.FORM_WRITE,
    },
}

# Role aliases normalization mapping
_ROLE_ALIASES_MAP: Dict[str, str] = {
    "sponsor_admin": RoleEnum.SPONSOR_ADMIN.value,
    "sponsor": RoleEnum.SPONSOR_ADMIN.value,
    "designer": RoleEnum.SPONSOR_DESIGNER.value,
    "sponsor_designer": RoleEnum.SPONSOR_DESIGNER.value,
    "pi": RoleEnum.PRINCIPAL_INVESTIGATOR.value,
    "principal_investigator": RoleEnum.PRINCIPAL_INVESTIGATOR.value,
    "investigator": RoleEnum.PRINCIPAL_INVESTIGATOR.value,
    "crc": RoleEnum.CRC.value,
    "clinical_research_coordinator": RoleEnum.CRC.value,
    "cra": RoleEnum.CRA.value,
    "clinical_research_associate": RoleEnum.CRA.value,
    "monitor": RoleEnum.CRA.value,
    "dm": RoleEnum.DATA_MANAGER.value,
    "data_manager": RoleEnum.DATA_MANAGER.value,
    "auditor": RoleEnum.AUDITOR.value,
    "inspector": RoleEnum.AUDITOR.value,
    "subject": RoleEnum.SUBJECT.value,
    "patient": RoleEnum.SUBJECT.value,
}


def normalize_role_name(role: str) -> str:
    """Normalize arbitrary role string or alias to canonical RoleEnum string value.

    Args:
        role: Raw role string or alias (e.g. 'pi', 'crc', 'auditor').

    Returns:
        Canonical role string value (e.g. 'PrincipalInvestigator').
    """
    cleaned = role.strip()
    if cleaned in ROLE_PERMISSIONS_MAP:
        return cleaned

    lowered = cleaned.lower()
    if lowered in _ROLE_ALIASES_MAP:
        return _ROLE_ALIASES_MAP[lowered]

    return cleaned


def get_permissions_for_role(role: str) -> Set[PermissionEnum]:
    """Retrieve the set of granular permissions assigned to a given role.

    Args:
        role: Canonical role name or alias string.

    Returns:
        Set of PermissionEnum members granted to the role.
    """
    canonical_role = normalize_role_name(role)
    return ROLE_PERMISSIONS_MAP.get(canonical_role, set())


def get_permissions_for_roles(roles: List[str]) -> Set[PermissionEnum]:
    """Retrieve the aggregated set of permissions across multiple assigned roles.

    Args:
        roles: List of role strings or aliases.

    Returns:
        Aggregated set of PermissionEnum members granted across all input roles.
    """
    aggregated: Set[PermissionEnum] = set()
    for r in roles:
        aggregated.update(get_permissions_for_role(r))
    return aggregated


def has_permission(
    roles: Union[str, List[str]], required_permission: PermissionEnum
) -> bool:
    """Check if any of the provided roles possess the required permission.

    Args:
        roles: A single role string/alias or list of role strings/aliases.
        required_permission: The target PermissionEnum to verify.

    Returns:
        True if the required permission is granted, False otherwise.
    """
    if isinstance(roles, str):
        role_list = [roles]
    else:
        role_list = roles

    user_permissions = get_permissions_for_roles(role_list)
    return required_permission in user_permissions
