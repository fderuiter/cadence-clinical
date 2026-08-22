"""Unit tests for 21 CFR Part 11 dual-attribution AI compliance ledger and mixin.

Requirements: PRD-SYS-051
"""

from datetime import UTC, datetime

import pytest
from pydantic import ValidationError

from packages.compliance.services.esignature_verifier import (
    UnapprovedAIRecordError,
    assert_ai_record_approved,
    verify_ai_assisted_record_approval,
)
from packages.database.audit import (
    AIAssistedRecordMixin,
    AIGenerationManifest,
    AIReviewStatus,
)


class SampleClinicalNote(AIAssistedRecordMixin):
    """Sample clinical note entity decorated with AI assistance mixin."""

    note_id: str
    patient_id: str
    clinical_text: str


def test_ai_assisted_record_default_state_draft_ai():
    """Verify newly generated AI records start in DRAFT_AI status.

    @req:PRD-SYS-051
    """
    record = SampleClinicalNote(
        note_id="NOTE-001",
        patient_id="SUBJ-101",
        clinical_text="Patient reported mild fatigue.",
        model_identifier="gemini-1.5-pro",
        prompt_hash="a" * 64,
        confidence_score=0.95,
    )

    assert record.review_status == AIReviewStatus.DRAFT_AI
    assert record.is_active_clinical_data() is False
    assert record.approved_by_user_id is None
    assert record.approved_at is None
    assert record.esignature_manifest_id is None


def test_confidence_score_validation_bounds():
    """Verify confidence score must be strictly bounded between 0.0 and 1.0.

    @req:PRD-SYS-051
    """
    with pytest.raises(ValidationError):
        SampleClinicalNote(
            note_id="NOTE-002",
            patient_id="SUBJ-102",
            clinical_text="Test",
            model_identifier="gemini-1.5-pro",
            prompt_hash="b" * 64,
            confidence_score=1.5,  # Invalid: > 1.0
        )

    with pytest.raises(ValidationError):
        SampleClinicalNote(
            note_id="NOTE-002",
            patient_id="SUBJ-102",
            clinical_text="Test",
            model_identifier="gemini-1.5-pro",
            prompt_hash="b" * 64,
            confidence_score=-0.1,  # Invalid: < 0.0
        )


def test_approved_state_requires_hitl_fields():
    """Verify setting status to APPROVED without approver or signature is rejected.

    @req:PRD-SYS-051
    """
    with pytest.raises(ValidationError, match="approved_by_user_id"):
        SampleClinicalNote(
            note_id="NOTE-003",
            patient_id="SUBJ-103",
            clinical_text="Approved text",
            model_identifier="gemini-1.5-pro",
            prompt_hash="c" * 64,
            confidence_score=0.98,
            review_status=AIReviewStatus.APPROVED,
            # Missing approved_by_user_id, approved_at, esignature_manifest_id
        )


def test_valid_approved_ai_record():
    """Verify valid human approval with electronic signature passes validation.

    @req:PRD-SYS-051
    """
    now = datetime.now(UTC)
    record = SampleClinicalNote(
        note_id="NOTE-004",
        patient_id="SUBJ-104",
        clinical_text="Physician approved text",
        model_identifier="gemini-1.5-pro",
        prompt_hash="d" * 64,
        confidence_score=0.99,
        review_status=AIReviewStatus.APPROVED,
        approved_by_user_id="dr_smith",
        approved_at=now,
        esignature_manifest_id="SIG-MAN-9001",
    )

    assert record.review_status == AIReviewStatus.APPROVED
    assert record.is_active_clinical_data() is True
    assert record.approved_by_user_id == "dr_smith"


def test_esignature_verifier_blocks_draft_ai():
    """Verify ESignatureVerifier helper blocks unapproved AI records from clinical execution.

    @req:PRD-SYS-051
    """
    draft_record = SampleClinicalNote(
        note_id="NOTE-005",
        patient_id="SUBJ-105",
        clinical_text="Unapproved AI output",
        model_identifier="claude-3-5-sonnet",
        prompt_hash="e" * 64,
        confidence_score=0.88,
        review_status=AIReviewStatus.DRAFT_AI,
    )

    result = verify_ai_assisted_record_approval(draft_record)
    assert result.is_valid is False
    assert result.status == "UNAPPROVED_AI_DRAFT"

    with pytest.raises(UnapprovedAIRecordError):
        assert_ai_record_approved(draft_record)


def test_ai_generation_manifest_structure():
    """Verify AIGenerationManifest audit ledger structure and validation.

    @req:PRD-SYS-051
    """
    manifest = AIGenerationManifest(
        manifest_id="AI-MAN-101",
        model_identifier="gpt-4o",
        model_version="2024-08-06",
        prompt_hash="f" * 64,
        prompt_template_version="v2.1",
        hyperparameters={"temperature": 0.2, "top_p": 0.95},
        completion_hash="1" * 64,
        raw_completion='{"diagnosis": "Type 2 Diabetes"}',
        confidence_score=0.96,
        deid_applied=True,
        deid_tokens_count=3,
        created_by="ai_gateway_service",
        reason_for_change="AI assisted medical coding prediction",
    )

    assert manifest.manifest_id == "AI-MAN-101"
    assert manifest.confidence_score == 0.96
    assert manifest.deid_applied is True
    assert manifest.deid_tokens_count == 3
    assert manifest.version_index == 1
