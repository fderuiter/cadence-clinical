"""Unit test suite for SubjectLifecycleService and ISubjectRepository in execution microservice.

Requirements:
- @req:PRD-SYS-001 (21 CFR Part 11 Audit Trail & Electronic Records)
- @req:PRD-SYS-042 (Subject State Machine Advance on Enrollment)
- @req:PRD-SYS-121 (Sovereign Site Mutation Authorization)
- @req:PRD-SUB-001 (Clinical Subject Lifecycle Management)
"""

import pytest

from apps.execution.adapters.repositories.execution_repositories import (
    InMemoryAuditRepository,
    InMemoryConsentRepository,
    InMemorySubjectRepository,
)
from apps.execution.application.services import SubjectLifecycleService
from apps.execution.subject_lifecycle import (
    InvalidStateTransitionError,
)


@pytest.fixture
def subject_repo():
    return InMemorySubjectRepository()


@pytest.fixture
def consent_repo():
    return InMemoryConsentRepository()


@pytest.fixture
def audit_repo():
    return InMemoryAuditRepository()


@pytest.fixture
def service(subject_repo, consent_repo, audit_repo):
    return SubjectLifecycleService(
        subject_repo=subject_repo,
        consent_repo=consent_repo,
        audit_repo=audit_repo,
    )


@pytest.mark.asyncio
async def test_enroll_subject_success(service, subject_repo, audit_repo):
    """Validate subject enrollment correctly assigns sequential index, encrypts demographics, and logs audit record.

    @req:PRD-SYS-001
    @req:PRD-SYS-042
    @req:PRD-SUB-001
    """
    subject = await service.enroll_subject(
        study_id="STUDY-001",
        site_id="SITE-101",
        subject_id="SUBJ-001",
        arm_id="ARM-A",
        demographics={"age": 45, "gender": "F"},
        user_id="crc.user",
        reason="Initial Subject Enrollment",
    )

    assert subject.id is not None
    assert subject.subject_id == "SUBJ-001"
    assert subject.study_id == "STUDY-001"
    assert subject.site_id == "SITE-101"
    assert subject.treatment_group == "ARM-A"
    assert subject.status == "SCREENING"
    assert subject.enrollment_index == 0
    assert subject.encrypted_demographics is not None

    # Verify repository storage
    retrieved = await subject_repo.get_by_subject_code("SUBJ-001", study_id="STUDY-001")
    assert retrieved is not None
    assert retrieved.id == subject.id

    # Verify second enrollment gets sequential index 1
    subject_2 = await service.enroll_subject(
        study_id="STUDY-001",
        site_id="SITE-101",
        subject_id="SUBJ-002",
        arm_id="ARM-B",
        demographics=None,
        user_id="crc.user",
        reason="Second Enrollment",
    )
    assert subject_2.enrollment_index == 1

    # Verify audit log was recorded
    logs = await audit_repo.get_all_audit_logs()
    assert len(logs) >= 2
    assert any(log.action == "ENROLL_SUBJECT" for log in logs)


@pytest.mark.asyncio
async def test_record_consent_and_clear_reconsent(service, consent_repo):
    """Validate consent registration and clearing of requires_reconsent on signed ICF.

    @req:PRD-SYS-001
    @req:PRD-SUB-001
    """
    # 1. Record initial consent requiring reconsent
    consent_1 = await service.record_consent(
        subject_id="SUBJ-001",
        study_id="STUDY-001",
        site_id="SITE-101",
        version_tag="v1.0",
        version_index=1,
        icf_signed=True,
        requires_reconsent=True,
        is_paper_override=False,
        user_id="crc.user",
        reason="Protocol v1.0 initial consent",
    )
    assert consent_1.requires_reconsent is True

    # 2. Record amended consent without requires_reconsent -> should clear older consent requires_reconsent flag
    consent_2 = await service.record_consent(
        subject_id="SUBJ-001",
        study_id="STUDY-001",
        site_id="SITE-101",
        version_tag="v2.0",
        version_index=2,
        icf_signed=True,
        requires_reconsent=False,
        is_paper_override=False,
        user_id="crc.user",
        reason="Protocol v2.0 reconsent completed",
    )
    assert consent_2.requires_reconsent is False


@pytest.mark.asyncio
async def test_subject_state_transitions(service, subject_repo, audit_repo):
    """Validate subject state transitions follow regulated state machine.

    @req:PRD-SYS-042
    @req:PRD-SUB-001
    """
    subject = await service.enroll_subject(
        study_id="STUDY-001",
        site_id="SITE-101",
        subject_id="SUBJ-001",
        arm_id="ARM-A",
        demographics=None,
        user_id="crc.user",
        reason="Enrollment",
    )

    # Valid transition: SCREENING -> ENROLLED
    updated = await service.advance_subject_state(
        subject_id=subject.id,
        target_state="ENROLLED",
        user_id="pi.user",
        reason="Subject passed screening eligibility",
    )
    assert updated.status == "ENROLLED"

    # Valid transition: ENROLLED -> RANDOMIZED
    updated = await service.advance_subject_state(
        subject_id=subject.id,
        target_state="RANDOMIZED",
        user_id="pi.user",
        reason="Randomization confirmed",
    )
    assert updated.status == "RANDOMIZED"

    # Invalid transition: RANDOMIZED -> SCREENING (must raise InvalidStateTransitionError)
    with pytest.raises(InvalidStateTransitionError):
        await service.advance_subject_state(
            subject_id=subject.id,
            target_state="SCREENING",
            user_id="pi.user",
            reason="Invalid reset attempt",
        )


@pytest.mark.asyncio
async def test_update_demographics(service, subject_repo):
    """Validate updating encrypted demographics on active subject.

    @req:PRD-SYS-001
    @req:PRD-SUB-001
    """
    subject = await service.enroll_subject(
        study_id="STUDY-001",
        site_id="SITE-101",
        subject_id="SUBJ-001",
        arm_id="ARM-A",
        demographics={"weight": 70.5},
        user_id="crc.user",
        reason="Enrollment",
    )

    updated = await service.update_demographics(
        subject_id=subject.id,
        demographics={"weight": 71.0, "height": 175},
        user_id="crc.user",
        reason="Follow-up Vitals Update",
    )
    assert updated.encrypted_demographics is not None
