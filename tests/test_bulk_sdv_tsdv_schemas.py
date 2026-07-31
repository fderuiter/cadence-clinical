import pytest
from pydantic import ValidationError

import packages  # noqa: F401
from execution.sdv_transport_models import (
    BulkSdvSignOffRequest,
    BulkSdvSignOffResponse,
    QueryTargetDescriptor,
    BulkQueryGenerationRequest,
    BulkQueryGenerationResponse,
)
from apps.execution.tsdv import evaluate_bulk_tsdv, TSDVTargetEvaluation


class MockTSDVConfig:
    """Mock configuration matching apps/execution/tsdv.py attribute expectations."""

    def __init__(
        self,
        initial_full_sdv_subject_count=0,
        random_sample_percentage=0.0,
        full_sdv_domains=None,
        safety_endpoints=None,
        zero_sdv_domains=None,
        trial_random_seed=None,
        sampling_model="SUBJECT_BASED",
    ):
        self.initial_full_sdv_subject_count = initial_full_sdv_subject_count
        self.random_sample_percentage = random_sample_percentage
        self.full_sdv_domains = full_sdv_domains or []
        self.safety_endpoints = safety_endpoints or []
        self.zero_sdv_domains = zero_sdv_domains or []
        self.trial_random_seed = trial_random_seed
        self.sampling_model = sampling_model


def test_bulk_sdv_signoff_request_validation():
    # @req:PRD-SYS-001
    # @Req:PRD-SYS-001
    """Verify validation and default fields of BulkSdvSignOffRequest."""
    payload = {
        "study_id": "STUDY-123",
        "subject_id": "SUBJ-A1",
        "scope": "VISIT",
        "target_ids": ["OBS-01", "OBS-02"],
        "reason_for_change": "In-person Source Data Verification",
    }
    req = BulkSdvSignOffRequest(**payload)
    assert req.study_id == "STUDY-123"
    assert req.subject_id == "SUBJ-A1"
    assert req.scope == "VISIT"
    assert req.target_ids == ["OBS-01", "OBS-02"]
    assert req.reason_for_change == "In-person Source Data Verification"
    assert req.site_id is None

    # Missing mandatory field: reason_for_change
    bad_payload = payload.copy()
    del bad_payload["reason_for_change"]
    with pytest.raises(ValidationError):
        BulkSdvSignOffRequest(**bad_payload)


def test_bulk_sdv_signoff_response_validation():
    # @req:PRD-SYS-001
    # @Req:PRD-SYS-001
    """Verify validation and fields of BulkSdvSignOffResponse."""
    payload = {
        "signed_count": 2,
        "signed_target_ids": ["OBS-01", "OBS-02"],
        "skipped_target_ids": ["OBS-03"],
        "content_digest": "abcdef1234567890",
        "timestamp_utc": "2026-10-31T12:00:00Z",
        "audit_tx": "tx-12345",
    }
    resp = BulkSdvSignOffResponse(**payload)
    assert resp.signed_count == 2
    assert resp.signed_target_ids == ["OBS-01", "OBS-02"]
    assert resp.skipped_target_ids == ["OBS-03"]
    assert resp.content_digest == "abcdef1234567890"
    assert resp.timestamp_utc == "2026-10-31T12:00:00Z"
    assert resp.audit_tx == "tx-12345"


def test_query_target_descriptor():
    # @req:PRD-SYS-001
    # @Req:PRD-SYS-001
    """Verify QueryTargetDescriptor can be created with various coordinates."""
    payload = {
        "subject_id": "SUB-99",
        "visit_id": "VISIT-01",
        "domain": "AE",
        "explanation": "Adverse event severity is missing.",
    }
    desc = QueryTargetDescriptor(**payload)
    assert desc.subject_id == "SUB-99"
    assert desc.visit_id == "VISIT-01"
    assert desc.domain == "AE"
    assert desc.test_code is None
    assert desc.observation_id is None
    assert desc.explanation == "Adverse event severity is missing."


def test_bulk_query_generation_request_validation():
    # @req:PRD-SYS-001
    # @Req:PRD-SYS-001
    """Verify validation and fields of BulkQueryGenerationRequest."""
    targets = [
        {
            "subject_id": "SUB-99",
            "domain": "VS",
            "test_code": "DIABP",
            "explanation": "Diastolic BP value of 150 seems anomalously high.",
        }
    ]
    payload = {
        "study_id": "STUDY-123",
        "query_targets": targets,
        "reason_for_change": "Data monitoring audit discrepancy check",
    }
    req = BulkQueryGenerationRequest(**payload)
    assert req.study_id == "STUDY-123"
    assert len(req.query_targets) == 1
    assert req.query_targets[0].subject_id == "SUB-99"
    assert req.query_targets[0].domain == "VS"
    assert req.query_targets[0].test_code == "DIABP"
    assert req.reason_for_change == "Data monitoring audit discrepancy check"


def test_bulk_query_generation_response_validation():
    # @req:PRD-SYS-001
    # @Req:PRD-SYS-001
    """Verify validation and fields of BulkQueryGenerationResponse."""
    skipped = [
        {
            "subject_id": "SUB-99",
            "domain": "VS",
            "test_code": "DIABP",
            "explanation": "Diastolic BP value of 150 seems anomalously high.",
        }
    ]
    payload = {
        "generated_count": 1,
        "generated_query_ids": ["QRY-77"],
        "skipped_targets": skipped,
        "timestamp_utc": "2026-10-31T12:00:00Z",
    }
    resp = BulkQueryGenerationResponse(**payload)
    assert resp.generated_count == 1
    assert resp.generated_query_ids == ["QRY-77"]
    assert len(resp.skipped_targets) == 1
    assert resp.skipped_targets[0].subject_id == "SUB-99"
    assert resp.timestamp_utc == "2026-10-31T12:00:00Z"


def test_evaluate_bulk_tsdv_pure():
    # @req:PRD-SYS-001
    # @Req:PRD-SYS-001
    """Verify the behavior of evaluate_bulk_tsdv with multiple subject and domain configurations."""
    config = MockTSDVConfig(
        sampling_model="SUBJECT_BASED",
        initial_full_sdv_subject_count=2,
        full_sdv_domains=["VS"],
        zero_sdv_domains=["DM"],
    )

    targets = [
        ("SUB-01", 0, "LB"),  # Within initial N (2) -> True
        ("SUB-02", 1, "DM"),  # Within initial N but domain DM is zero-SDV -> False (absolute priority)
        ("SUB-03", 2, "VS"),  # Beyond initial N but domain VS is safety/full-SDV -> True (absolute priority)
        ("SUB-04", 3, "LB"),  # Beyond initial N with random_sample_percentage = 0.0 -> False
    ]

    results = evaluate_bulk_tsdv(config, targets)

    assert len(results) == 4

    # Result 0
    assert isinstance(results[0], TSDVTargetEvaluation)
    assert results[0].subject_uuid == "SUB-01"
    assert results[0].enrollment_index == 0
    assert results[0].domain == "LB"
    assert results[0].required is True
    assert results[0].subject_selected is True
    assert results[0].field_decision is None
    assert "within the first 2" in results[0].explanation

    # Result 1
    assert results[1].subject_uuid == "SUB-02"
    assert results[1].enrollment_index == 1
    assert results[1].domain == "DM"
    assert results[1].required is False
    assert results[1].subject_selected is True
    assert results[1].field_decision is False
    assert "zero-SDV domain" in results[1].explanation

    # Result 2
    assert results[2].subject_uuid == "SUB-03"
    assert results[2].enrollment_index == 2
    assert results[2].domain == "VS"
    assert results[2].required is True
    assert results[2].subject_selected is False
    assert results[2].field_decision is True
    assert "safety/full-SDV domain" in results[2].explanation

    # Result 3
    assert results[3].subject_uuid == "SUB-04"
    assert results[3].enrollment_index == 3
    assert results[3].domain == "LB"
    assert results[3].required is False
    assert results[3].subject_selected is False
    assert results[3].field_decision is None
    assert "was not selected" in results[3].explanation
