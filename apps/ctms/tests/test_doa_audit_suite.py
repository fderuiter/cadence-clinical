"""Integration test suite verifying historical Delegation of Authority (DOA) audit trail logging.

Requirements: PRD-SYS-001
"""

from execution.doa_models import DOATaskDelegationEnum, DOATaskRoleEnum

from apps.execution.services.doa_service import DOAService


def test_doa_historical_audit_trail_logging() -> None:
    """Validate DOA service maintains immutable historical audit records of task assignments and sign-offs.

    Requirements: PRD-SYS-001
    """
    service = DOAService()
    study_id = "study_audit_doa_01"
    site_id = "site_audit_doa_101"

    # Step 1: Create 3 task assignments
    rec1 = service.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Dr. Mark Vance",
        personnel_email="mvance@site.org",
        role=DOATaskRoleEnum.SUB_INVESTIGATOR,
        delegated_tasks=[DOATaskDelegationEnum.SUBJECT_INFORMED_CONSENT],
        start_date="2026-07-01",
    )

    rec2 = service.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="CRC Chloe Bennett",
        personnel_email="cbennett@site.org",
        role=DOATaskRoleEnum.CLINICAL_RESEARCH_COORDINATOR,
        delegated_tasks=[DOATaskDelegationEnum.CRF_DATA_ENTRY],
        start_date="2026-07-05",
    )

    rec3 = service.add_assignment(
        study_id=study_id,
        site_id=site_id,
        personnel_name="Nurse David Kim",
        personnel_email="dkim@site.org",
        role=DOATaskRoleEnum.STUDY_NURSE,
        delegated_tasks=[DOATaskDelegationEnum.PHYSICAL_EXAMINATION],
        start_date="2026-07-10",
    )

    # Step 2: PI signs off rec1 and rec2, leaving rec3 pending
    service.sign_off_assignment(
        rec1.record_id, "pi_main", "Initial Delegation Approval"
    )
    service.sign_off_assignment(rec2.record_id, "pi_main", "CRC Delegation Approval")

    # Step 3: Verify audit log state
    site_log = service.get_site_doa_log(study_id, site_id)
    assert len(site_log) == 3

    signed_ids = [r.record_id for r in site_log if r.signed_off]
    pending_ids = [r.record_id for r in site_log if not r.signed_off]

    assert rec1.record_id in signed_ids
    assert rec2.record_id in signed_ids
    assert rec3.record_id in pending_ids
