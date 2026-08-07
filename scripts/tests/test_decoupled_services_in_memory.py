"""
Unit tests for hexagonal decoupled services running 100% in-memory.
These tests verify that services run natively with zero reliance on web servers,
live databases, or SQLAlchemy/FastAPI framework imports.
"""

from typing import Any

import pytest

from apps.execution.coding.service import process_coding_action
from apps.execution.eligibility_service import verify_subject_eligible_for_randomization
from apps.execution.exceptions import (
    CodingAssignmentNotFoundError,
    InvalidCodingActionError,
    SubjectEligibilityError,
)


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
