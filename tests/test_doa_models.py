"""Unit test suite for Delegation of Authority (DOA) log Pydantic models.

Requirements: PRD-SYS-001
"""

from execution.doa_models import (
    DOAAssignmentRecord,
    DOATaskDelegationEnum,
    DOATaskRoleEnum,
)

import packages  # noqa: F401


def test_doa_assignment_record_creation() -> None:
    """Validate DOAAssignmentRecord model instantiation and field constraints.

    Requirements: PRD-SYS-001
    """
    record = DOAAssignmentRecord(
        record_id="doa_rec_001",
        study_id="study_doa_01",
        site_id="site_doa_101",
        personnel_name="Dr. Sarah Connor",
        personnel_email="sconnor@site.org",
        role=DOATaskRoleEnum.SUB_INVESTIGATOR,
        delegated_tasks=[
            DOATaskDelegationEnum.SUBJECT_INFORMED_CONSENT,
            DOATaskDelegationEnum.PHYSICAL_EXAMINATION,
            DOATaskDelegationEnum.AE_SAE_REPORTING,
        ],
        start_date="2026-07-01",
        is_active=True,
        signed_off=False,
    )

    assert record.record_id == "doa_rec_001"
    assert record.role == DOATaskRoleEnum.SUB_INVESTIGATOR
    assert len(record.delegated_tasks) == 3
    assert DOATaskDelegationEnum.SUBJECT_INFORMED_CONSENT in record.delegated_tasks
    assert record.signed_off is False


def test_doa_delegation_record_defaults() -> None:
    """Test instantiating DOADelegationRecord sets default version_index = 1 and created_at.

    Requirements: PRD-SYS-001
    """
    from datetime import date, datetime

    from apps.ctms.models import DOADelegationRecord

    record = DOADelegationRecord(
        id="doa_rec_111",
        site_id="site_101",
        staff_user_id="user_202",
        task_code="CRF_DATA_ENTRY",
        start_date=date(2026, 7, 1),
        created_by="pi_user",
        reason_for_change="New delegation",
    )

    assert record.id == "doa_rec_111"
    assert record.version_index == 1
    assert isinstance(record.created_at, datetime)
    assert record.status == "PENDING_PI_APPROVAL"
    assert record.is_active is True
    assert record.is_deleted is False


def test_doa_delegation_record_validation() -> None:
    """Test missing required field raises validation error.

    Requirements: PRD-SYS-001
    """
    import pytest
    from pydantic import ValidationError

    from apps.ctms.models import DOADelegationRecord

    with pytest.raises(ValidationError):
        # missing start_date in dict validation
        DOADelegationRecord.model_validate(
            {
                "id": "doa_rec_111",
                "site_id": "site_101",
                "staff_user_id": "user_202",
                "task_code": "CRF_DATA_ENTRY",
                "created_by": "pi_user",
                "reason_for_change": "New delegation",
            }
        )
