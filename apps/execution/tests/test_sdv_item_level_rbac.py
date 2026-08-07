"""Unit and integration tests for item-level SDV flag & resolution transport contracts and RBAC permissions.

Requirements: PRD-SYS-001, GxP, 21 CFR Part 11
"""

import pytest
from pydantic import ValidationError

from apps.execution.src.domain.sdv_transport_models import (
    FlagTargetDescriptor,
    SdvFlagRequest,
    SdvFlagResponse,
    SdvFlagSeverity,
    SdvResolveRequest,
    SdvResolveResponse,
)
from packages.security.permissions import PermissionEnum, RoleEnum
from packages.security.permissions import has_permission as has_perm_granular
from packages.security.rbac import (
    ROLE_AUDITOR_CANONICAL,
    ROLE_CRA_CANONICAL,
    ROLE_INVESTIGATOR,
    Principal,
)
from packages.security.rbac import (
    has_permission as has_perm_rbac,
)


def test_sdv_flag_severity_enum():
    """Verify SdvFlagSeverity has expected values."""
    assert SdvFlagSeverity.MINOR == "MINOR"
    assert SdvFlagSeverity.MAJOR == "MAJOR"
    assert SdvFlagSeverity.CRITICAL == "CRITICAL"


def test_flag_target_descriptor_validation():
    """Verify FlagTargetDescriptor handles required fields and defaults."""
    # Valid payload
    desc = FlagTargetDescriptor(
        target_id="obs_001",
        observation_id="obs_001",
        form_id="form_abc",
        field_id="field_xyz",
        flag_reason="Discrepant vital signs value",
        flag_severity=SdvFlagSeverity.MAJOR,
    )
    assert desc.target_id == "obs_001"
    assert desc.flag_reason == "Discrepant vital signs value"
    assert desc.flag_severity == SdvFlagSeverity.MAJOR

    # Missing required target_id
    with pytest.raises(ValidationError):
        FlagTargetDescriptor(
            flag_reason="Incomplete form",
            flag_severity=SdvFlagSeverity.MINOR,
        )


def test_sdv_flag_request_validation():
    """Verify SdvFlagRequest validation and GxP compliance rules."""
    target = FlagTargetDescriptor(
        target_id="obs_002",
        flag_reason="Values outside range",
        flag_severity=SdvFlagSeverity.CRITICAL,
    )
    req = SdvFlagRequest(
        study_id="study_111",
        subject_id="subj_222",
        scope="FIELD",
        targets=[target],
        reason_for_change="Initial flagging of discrepant blood pressure data",
        site_id="site_boston",
    )
    assert req.study_id == "study_111"
    assert req.signing_reason == "CRA/monitor-gated bulk SDV sign-off"
    assert len(req.targets) == 1
    assert req.targets[0].flag_severity == SdvFlagSeverity.CRITICAL


def test_sdv_resolve_request_validation():
    """Verify SdvResolveRequest validation with both targets list and flat target_ids."""
    target = FlagTargetDescriptor(
        target_id="obs_002",
        flag_reason="Verified with source source documents",
        flag_severity=SdvFlagSeverity.MINOR,
    )
    # Using targets list
    req_targets = SdvResolveRequest(
        study_id="study_111",
        subject_id="subj_222",
        scope="FIELD",
        targets=[target],
        reason_for_change="Source data verified",
    )
    assert req_targets.targets is not None
    assert req_targets.targets[0].target_id == "obs_002"

    # Using flat target_ids
    req_flat = SdvResolveRequest(
        study_id="study_111",
        subject_id="subj_222",
        scope="FIELD",
        target_ids=["obs_002", "obs_003"],
        reason_for_change="Source data verified",
    )
    assert req_flat.target_ids == ["obs_002", "obs_003"]


def test_sdv_response_structures():
    """Verify response payloads mirror BulkSdvSignOffResponse fields."""
    flag_resp = SdvFlagResponse(
        flag_id="flag_999",
        content_digest="sha256hash",
        timestamp_utc="2026-09-15T12:00:00Z",
        audit_tx="tx_abcdef12",
        flagged_count=1,
        flagged_target_ids=["obs_001"],
        skipped_target_ids=["obs_002"],
    )
    assert flag_resp.flagged_count == 1
    assert flag_resp.flagged_target_ids == ["obs_001"]

    resolve_resp = SdvResolveResponse(
        resolution_id="res_999",
        content_digest="sha256hash",
        timestamp_utc="2026-09-15T12:01:00Z",
        audit_tx="tx_abcdef13",
        resolved_count=2,
        resolved_target_ids=["obs_001", "obs_003"],
        skipped_target_ids=[],
    )
    assert resolve_resp.resolved_count == 2
    assert resolve_resp.resolved_target_ids == ["obs_001", "obs_003"]


def test_sdv_flag_rbac_permissions():
    """Verify sdv:flag action set and Role-Based Access Control logic in packages/security/rbac.py."""
    cra_p = Principal(user_id="cra_01", roles=[ROLE_CRA_CANONICAL])
    monitor_p = Principal(user_id="monitor_01", roles=["monitor"])
    investigator_p = Principal(user_id="inv_01", roles=[ROLE_INVESTIGATOR])
    auditor_p = Principal(user_id="auditor_01", roles=[ROLE_AUDITOR_CANONICAL])

    # CRA and Monitor roles are allowed to flag
    assert has_perm_rbac(cra_p, "sdv:flag") is True
    assert has_perm_rbac(monitor_p, "sdv:flag") is True

    # Investigator is kept at read only for sdv and cannot write or flag
    assert has_perm_rbac(investigator_p, "sdv:read") is True
    assert has_perm_rbac(investigator_p, "sdv:flag") is False

    # Auditor is restricted and cannot flag
    assert has_perm_rbac(auditor_p, "sdv:flag") is False


def test_sdv_flag_granular_permissions():
    """Verify SDV_FLAG granular permission in packages/security/permissions.py."""
    # Check that PermissionEnum has the SDV_FLAG definition
    assert PermissionEnum.SDV_FLAG == "sdv:flag"

    # Check that CRA has PermissionEnum.SDV_FLAG next to SDV_VERIFY
    assert has_perm_granular(RoleEnum.CRA.value, PermissionEnum.SDV_FLAG) is True
    assert has_perm_granular(RoleEnum.CRA.value, PermissionEnum.SDV_VERIFY) is True

    # Check that Investigator/CRC do not have SDV_FLAG or SDV_VERIFY permissions
    assert (
        has_perm_granular(
            RoleEnum.PRINCIPAL_INVESTIGATOR.value, PermissionEnum.SDV_FLAG
        )
        is False
    )
    assert has_perm_granular(RoleEnum.CRC.value, PermissionEnum.SDV_FLAG) is False
