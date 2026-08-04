"""
Unit tests for hexagonal decoupled services running 100% in-memory.
These tests verify that services run natively with zero reliance on web servers,
live databases, or SQLAlchemy/FastAPI framework imports.
"""

from datetime import UTC, datetime
from typing import Any

import pytest

from apps.execution.application.ports import IExecutionDOARepository
from apps.execution.application.services import ExecutionDOAUseCase
from apps.execution.coding.service import process_coding_action
from apps.execution.domain.models import (
    ExecutionAuditLogEntity,
    ExecutionDelegationEntity,
    ExecutionStaffEntity,
)
from apps.execution.eligibility_service import verify_subject_eligible_for_randomization
from apps.execution.exceptions import (
    CodingAssignmentNotFoundError,
    InvalidCodingActionError,
    SubjectEligibilityError,
)
from apps.execution.tsdv import evaluate_bulk_tsdv, evaluate_tsdv_requirement


class DummySubject:
    def __init__(self, subject_id: str, status: str):
        self.subject_id = subject_id
        self.status = status


def test_in_memory_eligibility_rejection():
    """Verify verify_subject_eligible_for_randomization raises domain error for non-ENROLLED subjects in-memory."""
    subj_screening = DummySubject("SUBJ-123", "SCREENING")

    with pytest.raises(SubjectEligibilityError) as exc:
        verify_subject_eligible_for_randomization(subj_screening)
    assert "Only ENROLLED subjects can proceed" in str(exc.value)

    # Dictionary representation works as a plain data structure
    subj_dict = {"subject_id": "SUBJ-456", "status": "SCREEN_FAILED"}
    with pytest.raises(SubjectEligibilityError) as exc:
        verify_subject_eligible_for_randomization(subj_dict)
    assert "Only ENROLLED subjects can proceed" in str(exc.value)


def test_in_memory_eligibility_success():
    """Verify verify_subject_eligible_for_randomization passes for ENROLLED subjects in-memory."""
    subj_enrolled = DummySubject("SUBJ-999", "ENROLLED")
    # Should not raise any error
    verify_subject_eligible_for_randomization(subj_enrolled)

    subj_dict_enrolled = {"subject_id": "SUBJ-999", "status": "ENROLLED"}
    verify_subject_eligible_for_randomization(subj_dict_enrolled)


# --- Coding Service In-Memory Mocks ---


class DummyAssignment:
    def __init__(self):
        self.id = "assign_1"
        self.verbatim_text = "Migraine"
        self.observation_id = "obs_1"
        self.dictionary_type = "MEDDRA"
        self.dictionary_version = "26.0"
        self.coded_code = None
        self.coded_term = None
        self.status = "UNCODED"
        self.recoding_status = "NONE"
        self.suggestions = [{"code": "10029300", "term_name": "Migraine", "score": 1.0}]
        self.hierarchy = None
        self.assigned_by = None
        self.assigned_at = None
        self.score = None


class DummyQuery:
    def __init__(self, observation_id: str):
        self.observation_id = observation_id
        self.status = "OPEN"
        self.resolver = None
        self.resolved_at = None
        self.response = None


class InMemoryCodingRepository:
    def __init__(self):
        self.assignments = {"assign_1": DummyAssignment()}
        self.queries = {"obs_1": [DummyQuery("obs_1")]}
        self.ledgers = []

    async def get_assignment(self, assignment_id: str) -> Any:
        if assignment_id in self.assignments:
            return self.assignments[assignment_id]
        raise CodingAssignmentNotFoundError(f"Assignment {assignment_id} not found.")

    async def list_assignments(self, **kwargs) -> list[Any]:
        return list(self.assignments.values())

    async def save_assignment(self, assignment: Any) -> None:
        self.assignments[assignment.id] = assignment

    async def add_ledger(self, ledger_data: dict) -> None:
        self.ledgers.append(ledger_data)

    async def get_active_queries(self, observation_id: str) -> list[Any]:
        return self.queries.get(observation_id, [])

    async def save_query(self, query: Any) -> None:
        pass

    async def validate_meddra_term(self, version: str, code: str) -> Any:
        return "mock_meddra_record" if code == "10029300" else None

    async def validate_whodrug_record(self, version: str, code: str) -> Any:
        return None

    async def get_meddra_hierarchy(self, term_record: Any, version: str) -> list[Any]:
        return [{"llt_code": "10029300"}]

    async def get_whodrug_context(
        self, rec_record: Any, version: str
    ) -> tuple[list[Any], list[Any]]:
        return ([], [])


@pytest.mark.asyncio
async def test_process_coding_action_accept_in_memory():
    """Verify that process_coding_action processes an ACCEPT action successfully in-memory with a mock repository."""
    repo = InMemoryCodingRepository()

    # Process ACCEPT on suggestion index 0
    updated_assignment = await process_coding_action(
        session=repo,
        assignment_id="assign_1",
        action="ACCEPT",
        suggestion_index=0,
        actor="test_user",
    )

    assert updated_assignment.status == "CODED"
    assert updated_assignment.coded_code == "10029300"
    assert updated_assignment.coded_term == "Migraine"
    assert len(repo.ledgers) == 1
    assert repo.ledgers[0]["new_coded_code"] == "10029300"
    assert repo.ledgers[0]["decision_by"] == "test_user"


@pytest.mark.asyncio
async def test_process_coding_action_invalid_code_in_memory():
    """Verify that process_coding_action rejects an invalid code action in-memory with a mock repository."""
    repo = InMemoryCodingRepository()

    with pytest.raises(InvalidCodingActionError) as exc:
        await process_coding_action(
            session=repo,
            assignment_id="assign_1",
            action="ACCEPT",
            code="INVALID_CODE",
            actor="test_user",
        )
    assert "does not match any available suggestions" in str(exc.value)


class InMemoryExecutionDOARepository(IExecutionDOARepository):
    """InMemory execution repository implementation for testing Delegation of Authority logic."""

    def __init__(self) -> None:
        self.staff_store: dict[str, ExecutionStaffEntity] = {}
        self.delegation_store: dict[str, ExecutionDelegationEntity] = {}
        self.audit_logs: list[ExecutionAuditLogEntity] = []

    async def get_staff(
        self, site_id: str, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        staff = self.staff_store.get(staff_user_id)
        if staff and staff.site_id == site_id:
            return staff
        return None

    async def get_staff_by_user_id(
        self, staff_user_id: str
    ) -> ExecutionStaffEntity | None:
        return self.staff_store.get(staff_user_id)

    async def save_staff(self, staff: ExecutionStaffEntity) -> ExecutionStaffEntity:
        self.staff_store[staff.staff_user_id] = staff
        return staff

    async def get_delegation_by_id(
        self, delegation_id: str
    ) -> ExecutionDelegationEntity | None:
        return self.delegation_store.get(delegation_id)

    async def save_delegation(
        self, delegation: ExecutionDelegationEntity
    ) -> ExecutionDelegationEntity:
        if not delegation.id:
            import uuid

            delegation.id = f"delegation_{uuid.uuid4().hex[:8]}"
        self.delegation_store[delegation.id] = delegation
        return delegation

    async def save_audit_log(self, audit: ExecutionAuditLogEntity) -> None:
        self.audit_logs.append(audit)

    async def get_all_audit_logs(self) -> list[ExecutionAuditLogEntity]:
        return self.audit_logs

    async def get_all_delegations(self) -> list[ExecutionDelegationEntity]:
        return list(self.delegation_store.values())


@pytest.mark.asyncio
async def test_in_memory_execution_doa():
    """Verify Delegation of Authority logic executes 100% in-memory without database connection."""
    repo = InMemoryExecutionDOARepository()
    use_case = ExecutionDOAUseCase(repo)

    # 1. Create a staff member
    staff = await use_case.create_or_update_staff(
        site_id="site_abc",
        staff_user_id="staff_1",
        name="John Doe",
        email="john@doe.com",
        has_gcp_training=True,
    )
    assert staff.staff_user_id == "staff_1"
    assert staff.has_gcp_training is True

    # 2. Delegate a task
    delegation = await use_case.delegate_task(
        site_id="site_abc",
        staff_user_id="staff_1",
        task_code="SUBJECT_INFORMED_CONSENT",
        pi_user_id="pi_1",
        reason_for_change="Assignment of nursing task",
    )
    assert delegation.status == "PENDING_PI_APPROVAL"
    assert delegation.task_code == "SUBJECT_INFORMED_CONSENT"

    # 3. Approve delegation (by PI)
    approved = await use_case.approve_delegation(
        delegation_id=delegation.id,
        pi_user_id="pi_1",
    )
    assert approved.status == "ACTIVE"
    assert approved.is_active is True

    # 4. Revoke delegation
    revoked = await use_case.revoke_delegation(
        delegation_id=delegation.id,
        end_date=datetime.now(UTC),
        reason_for_change="Task completed",
    )
    assert revoked.status == "REVOKED"
    assert revoked.is_active is False


def test_in_memory_tsdv_verification():
    """Verify that clinical sampling rules evaluate compliance targets without active DB connection."""

    class DummyConfig:
        def __init__(self):
            self.sampling_model = "SUBJECT_BASED"
            self.initial_full_sdv_subject_count = 2
            self.random_sample_percentage = 50.0
            self.trial_random_seed = 42
            self.full_sdv_domains = ["VS", "DM"]
            self.safety_endpoints = ["AE"]
            self.zero_sdv_domains = ["LB"]

    config = DummyConfig()

    # Precedence check: Domain-level safety endpoint vs subject-level
    required, _, field_dec, exp = evaluate_tsdv_requirement(
        config=config, subject_uuid="subj_003", enrollment_index=3, domain="AE"
    )
    assert required is True
    assert field_dec is True
    assert "safety/full-SDV" in exp

    # Precedence check: Zero-SDV domain
    required, _, field_dec, exp = evaluate_tsdv_requirement(
        config=config, subject_uuid="subj_003", enrollment_index=3, domain="LB"
    )
    assert required is False
    assert field_dec is False
    assert "zero-SDV" in exp

    # Initial subjects always full SDV
    required, subj_sel, _, exp = evaluate_tsdv_requirement(
        config=config, subject_uuid="subj_001", enrollment_index=1, domain="MH"
    )
    assert required is True
    assert subj_sel is True
    assert "first 2 enrolled subjects" in exp

    # Bulk evaluation
    targets = [
        ("subj_001", 1, "AE"),
        ("subj_003", 3, "LB"),
    ]
    results = evaluate_bulk_tsdv(config, targets)
    assert len(results) == 2
    assert results[0].required is True
    assert results[1].required is False
