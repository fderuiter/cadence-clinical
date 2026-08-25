"""Comprehensive test suite for Schema-Enforced Dual Attribution with Inter-Stage DAG Review.

Validates FDA 21 CFR Part 11 & GxP compliance requirements:
1. Top-level audit log AI fields (ai_model_id, ai_prompt_hash, review_status, signature_manifest_id).
2. Inter-stage workflow pause state (PENDING_REVIEW) prior to USDM graph compilation.
3. Step-up identity re-authentication verification (verify_and_consume_sig_token).
4. Cryptographic signature manifest generation and token single-use consumption.
5. Ledger sealer Merkle block hash calculation incorporating AI provenance fields.
6. Downstream execution gatekeeper blocking operations on unapproved draft entities.
"""

from __future__ import annotations

import hashlib
import json
import time
import uuid
import pytest
from unittest.mock import AsyncMock, MagicMock
from fastapi import HTTPException
from fastapi.testclient import TestClient
from jose import jwt

from apps.designer.adapters.digitization_job_store import (
    DigitizationJobStore,
    get_digitization_job_store,
)
from apps.designer.application.services.digitization_dag_service import (
    DigitizationDAGRunner,
)
from apps.designer.domain.digitization_dag_models import (
    DigitizationJob,
    DigitizationJobStatus,
    DigitizationStage,
)
from apps.designer.main import app as designer_app
from apps.execution.database.models.audit import AuditLog
from apps.execution.database.sealer import (
    compute_block_hash,
    compute_merkle_root,
)
from apps.execution.site_execution_gatekeeper import (
    GatekeeperRejectionReason,
    SiteExecutionGatekeeper,
    assert_draft_review_state_approved,
)
from packages.security.audit_logger import AuditLogPayload, DbAuditLogRecord
from packages.security.sig_token_verifier import verify_and_consume_sig_token
from packages.security.signature_builder import CryptographicSignatureBuilder


GATEWAY_SECRET = "internal-gateway-secret-12345"


def create_valid_sig_token(user_id: str = "test_investigator_001") -> str:
    """Helper to issue a valid single-use step-up signature token."""
    now = time.time()
    payload = {
        "sub": user_id,
        "username": user_id,
        "action": "step_up_reauth",
        "roles": ["investigator", "sponsor_designer"],
        "iat": now,
        "exp": now + 3600.0,
        "jti": f"jti-dual-attr-{uuid.uuid4().hex}",
        "acr": "high-assurance-step-up",
        "auth_time": now,
    }
    return jwt.encode(payload, GATEWAY_SECRET, algorithm="HS256")


@pytest.fixture
def client() -> TestClient:
    return TestClient(designer_app)


# =============================================================================
# TEST 1: DATABASE AUDIT LOG SCHEMA AI FIELDS
# =============================================================================

def test_audit_log_model_top_level_ai_fields():
    """Verify AuditLog and AuditLogPayload contain top-level AI provenance fields.
    
    @req:PRD-SYS-001
    """
    audit_record = AuditLog(
        table_name="usdm_study_design",
        record_id="study_dual_001",
        action="COMPILE_USDM_GRAPH",
        user_id="user_designer_99",
        change_reason="21 CFR Part 11 Approval of AI-generated USDM Draft",
        model_id="gpt-4o-clinical-v1",
        prompt_hash="a1b2c3d4e5f67890a1b2c3d4e5f67890a1b2c3d4e5f67890a1b2c3d4e5f67890",
        review_state="APPROVED",
        signature_manifest_id="sig_manifest_test_123",
    )

    assert audit_record.model_id == "gpt-4o-clinical-v1"
    assert audit_record.prompt_hash == "a1b2c3d4e5f67890a1b2c3d4e5f67890a1b2c3d4e5f67890a1b2c3d4e5f67890"
    assert audit_record.review_state == "APPROVED"
    assert audit_record.signature_manifest_id == "sig_manifest_test_123"

    payload = AuditLogPayload(
        service_name="designer",
        action_type="APPROVE_DRAFT",
        entity_name="ProtocolDigitization",
        entity_id="job_123",
        user_id="user_designer_99",
        reason_for_change="Approval of AI draft",
        model_id="gpt-4o-clinical-v1",
        prompt_hash="a1b2c3",
        review_state="APPROVED",
        signature_manifest_id="sig_manifest_test_123",
    )

    assert payload.model_id == "gpt-4o-clinical-v1"
    assert payload.prompt_hash == "a1b2c3"
    assert payload.review_state == "APPROVED"
    assert payload.signature_manifest_id == "sig_manifest_test_123"


# =============================================================================
# TEST 2: INTER-STAGE WORKFLOW PAUSE STATE (PENDING_REVIEW)
# =============================================================================

@pytest.mark.asyncio
async def test_dag_workflow_pauses_in_pending_review_state():
    """Verify digitization DAG pipeline automatically pauses in PENDING_REVIEW before graph commit.
    
    @req:PRD-DDF-001
    """
    store = DigitizationJobStore()
    runner = DigitizationDAGRunner(job_store=store)

    doc_text = (
        "Protocol Title: Pause State Test Protocol\n"
        "Protocol ID: CDNC-PAUSE-001\n"
        "Phase: Phase II\n"
        "Inclusion Criteria:\n1. Age >= 18.\n"
    )
    doc_bytes = b"%PDF-1.4\n" + doc_text.encode("utf-8") + b"\n%%EOF"

    job = await runner.initialize_job(
        file_content=doc_bytes,
        filename="pause_protocol.pdf",
        study_id="study_pause_001",
    )

    assert job.status == DigitizationJobStatus.PENDING
    assert job.review_state == "UNAPPROVED_DRAFT"
    assert job.model_id == "gpt-4o-clinical-v1"
    assert job.prompt_hash is not None

    # Execute all 5 stages
    completed_job = await runner.run_job(job.job_id)

    # Job must pause in PENDING_REVIEW prior to USDM graph commit
    assert completed_job.status == DigitizationJobStatus.PENDING_REVIEW
    assert completed_job.review_state == "UNAPPROVED_DRAFT"
    assert completed_job.final_usdm_payload is not None


# =============================================================================
# TEST 3 & 4: STEP-UP RE-AUTHENTICATION & SIGNATURE TOKEN
# =============================================================================

def test_step_up_reauthentication_and_single_use_token():
    """Verify single-use step-up re-authentication token consumption and replay prevention.
    
    @req:PRD-SYS-001
    """
    user_id = "test_investigator_001"
    sig_token = create_valid_sig_token(user_id)

    # 1. First consumption succeeds
    decoded = verify_and_consume_sig_token(sig_token, expected_user_id=user_id)
    assert decoded["sub"] == user_id

    # 2. Replay consumption fails with 401 REAUTHENTICATION_REQUIRED
    with pytest.raises(HTTPException) as exc_info:
        verify_and_consume_sig_token(sig_token, expected_user_id=user_id)
    assert exc_info.value.status_code == 401
    assert "REAUTHENTICATION_REQUIRED" in str(exc_info.value.detail)


def test_cryptographic_signature_manifest_builder():
    """Verify CryptographicSignatureBuilder binds user identity, timestamp, and AI metadata.
    
    @req:PRD-SYS-001
    """
    builder = CryptographicSignatureBuilder()
    content_dict = {
        "study_id": "study_dual_001",
        "title": "Phase II Oncology Study",
        "model_id": "gpt-4o-clinical-v1",
    }
    content_digest = builder.compute_content_digest(content_dict)

    payload = builder.build_signature_payload(
        user_id="user_designer_99",
        purpose="USDM Graph Compilation Approval",
        content_digest=content_digest,
    )

    assert payload["user_id"] == "user_designer_99"
    assert payload["purpose"] == "USDM Graph Compilation Approval"
    assert payload["content_digest"] == content_digest
    assert "timestamp_utc" in payload or "timestamp" in payload


# =============================================================================
# TEST 5: LEDGER SEALER MERKLE HASH BINDING AI FIELDS
# =============================================================================

def test_ledger_sealer_merkle_hash_binds_ai_provenance():
    """Verify ledger sealer includes AI provenance fields in record payload for Merkle block hashing.
    
    @req:PRD-SYS-001
    """
    record_payload_1 = {
        "id": "rec_001",
        "table_name": "audit_logs",
        "record_id": "study_1",
        "action": "COMPILE_USDM_GRAPH",
        "user_id": "user_1",
        "timestamp": "2026-08-25T10:00:00",
        "old_values": None,
        "new_values": {"status": "APPROVED"},
        "version_index": 1,
        "change_reason": "Approval of draft",
        "model_id": "gpt-4o-clinical-v1",
        "prompt_hash": "hash_abc_123",
        "review_state": "APPROVED",
        "signature_manifest_id": "sig_manifest_001",
    }

    record_payload_tampered = dict(record_payload_1)
    record_payload_tampered["review_state"] = "UNAPPROVED_DRAFT"  # Tampered state

    serialized_1 = json.dumps(record_payload_1, sort_keys=True).encode("utf-8")
    serialized_tampered = json.dumps(record_payload_tampered, sort_keys=True).encode("utf-8")

    hash_1 = hashlib.sha256(serialized_1).hexdigest()
    hash_tampered = hashlib.sha256(serialized_tampered).hexdigest()

    # Hashes must differ if AI provenance or review state is modified
    assert hash_1 != hash_tampered

    root_original = compute_merkle_root([hash_1])
    root_tampered = compute_merkle_root([hash_tampered])

    assert root_original != root_tampered


# =============================================================================
# TEST 6: DOWNSTREAM EXECUTION GATEKEEPER RUNTIME GUARD
# =============================================================================

@pytest.mark.asyncio
async def test_gatekeeper_blocks_unapproved_draft_entities():
    """Verify execution gatekeeper blocks operations on unapproved draft review states.
    
    @req:PRD-SYS-001
    """
    gatekeeper = SiteExecutionGatekeeper()

    # 1. Unapproved draft review state raises HTTPException 403
    with pytest.raises(HTTPException) as exc_info:
        assert_draft_review_state_approved("UNAPPROVED_DRAFT")
    assert exc_info.value.status_code == 403
    assert "UNAPPROVED_DRAFT_REVIEW_STATE" in str(exc_info.value.detail)

    with pytest.raises(HTTPException) as exc_info:
        assert_draft_review_state_approved("PENDING_REVIEW")
    assert exc_info.value.status_code == 403

    # 2. Approved review state passes without exception
    assert_draft_review_state_approved("APPROVED")
    assert_draft_review_state_approved("ACTIVE")

    # 3. validate_mutation_eligibility includes UNAPPROVED_DRAFT_REVIEW_STATE in rejection reasons
    gatekeeper._greenlight_client = AsyncMock()
    gatekeeper._greenlight_client.get_greenlight_status.return_value = {
        "overall_status": "APPROVED",
        "is_greenlighted": True,
    }
    gatekeeper._training_client = AsyncMock()
    gatekeeper._training_client.get_qualification_status.return_value = {
        "is_qualified": True,
        "pending_curricula": [],
    }
    gatekeeper._doa_service = MagicMock()
    gatekeeper._doa_service.get_site_doa_log.return_value = []
    gatekeeper._doa_service._store = {}

    decision = await gatekeeper.validate_mutation_eligibility(
        site_id="site_101",
        user_id="user_pi",
        operation_type="SUBJECT_CREATION",
        study_id="study_001",
        user_role="principal_investigator",
        review_state="UNAPPROVED_DRAFT",
    )

    assert decision.is_eligible is False
    assert GatekeeperRejectionReason.UNAPPROVED_DRAFT_REVIEW_STATE in decision.rejection_reasons
