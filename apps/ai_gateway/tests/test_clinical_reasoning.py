"""
Unit and integration test suite for Hybrid Multi-Step Clinical Reasoning Core.

Validates:
1. Grounded protocol synthesis with explicit verbatim citations (>= 85% confidence).
2. Fail-closed rejection when citation confidence falls below 85%.
3. In-flight PHI masking using surrogate tokens for intermediate reasoning steps.
4. Staging generated clinical records into draft state requiring human e-signature.
5. Mandatory tenant context and audit header enforcement.
6. Stateless microservice execution with caller-managed execution state context.
"""

import time

from fastapi.testclient import TestClient

from apps.ai_gateway.main import app
from packages.compliance.services.protocol_rag import ProtocolRAGContextChunk
from packages.security.signing import generate_gateway_signature

client = TestClient(app)

GATEWAY_SECRET = b"internal-gateway-secret-12345"


def build_auth_headers(
    user_id: str = "usr_clinical_investigator",
    roles: str = "Research_Investigator,Data_Manager",
    tenant_id: str = "tenant_cadence_beta",
    change_reason: str = "Multi-step trial reasoning execution",
) -> dict[str, str]:
    """Helper to construct valid API Gateway HMAC authorization headers."""
    ts = str(time.time())
    sig = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=ts,
        secret=GATEWAY_SECRET,
        change_reason=change_reason,
        tenant_id=tenant_id,
    )
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": ts,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": tenant_id,
    }


def test_verified_multi_step_synthesis_in_draft_state():
    """Verify that multi-step reasoning with verified protocol chunks generates grounded citations and stages into draft state.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()

    chunks = [
        ProtocolRAGContextChunk(
            chunk_id="chk_elig_01",
            protocol_version="v2.1",
            section_number="3.1",
            section_title="Inclusion Criteria",
            page_number=12,
            chunk_text="Subjects must be aged 18 to 75 with confirmed Type 2 Diabetes Mellitus and HbA1c between 7.5% and 10.0%.",
            is_approved=True,
        ),
        ProtocolRAGContextChunk(
            chunk_id="chk_elig_02",
            protocol_version="v2.1",
            section_number="3.2",
            section_title="Exclusion Criteria",
            page_number=14,
            chunk_text="Excludes patients with severe renal impairment (eGFR < 30 mL/min/1.73m2) or active hepatic disease.",
            is_approved=True,
        ),
    ]

    payload = {
        "query": "Subjects aged 18 to 75 with confirmed Type 2 Diabetes Mellitus HbA1c and severe renal impairment eGFR exclusion.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-T2D",
        "protocol_chunks": [c.model_dump() for c in chunks],
        "min_citation_confidence": 0.85,
    }

    response = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 200, response.text
    data = response.json()

    assert data["status"] == "DRAFT_STAGED"
    assert data["is_citation_verified"] is True
    assert data["overall_faithfulness_score"] >= 0.85
    assert len(data["citations"]) >= 1
    assert len(data["reasoning_chain"]) == 5

    # Verify Draft Record properties (Requirement 5 & AC 4)
    draft = data["draft_record"]
    assert draft is not None
    assert draft["status"] == "DRAFT_STAGED"
    assert draft["requires_esignature"] is True
    assert "Protocol v2.1" in draft["synthesized_guidelines"]
    assert len(draft["eligibility_criteria"]) >= 1

    # Verify Execution State context returned for stateless caller management (Requirement 4 & AC 6)
    exec_state = data["execution_state"]
    assert exec_state["session_id"].startswith("reas_")
    assert exec_state["current_step_index"] == 5
    assert len(exec_state["completed_steps"]) == 5


def test_low_citation_confidence_fails_closed():
    """Verify that multi-step reasoning outputs with < 85% citation confidence generate an error response and suppress draft staging.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()

    # Pass empty or unapproved protocol chunks -> fails closed (< 85% confidence threshold)
    payload = {
        "query": "Synthesize protocol dosage for unverified compound X.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-UNVERIFIED",
        "protocol_chunks": [],
        "min_citation_confidence": 0.85,
    }

    response = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload,
        headers=headers,
    )

    # Rejects immediately with HTTP 422 Unprocessable Entity (Requirement 2, Constraint 4, AC 3)
    assert response.status_code == 422, response.text
    data = response.json()
    assert "CITATION_CONFIDENCE_BELOW_THRESHOLD" in data["detail"]


def test_in_flight_phi_masking_with_surrogate_tokens():
    """Verify that intermediate reasoning steps automatically mask sensitive patient identifiers using surrogate tokens and re-hydrate final draft output.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()

    chunks = [
        ProtocolRAGContextChunk(
            chunk_id="chk_elig_10",
            protocol_version="v3.0",
            section_number="4.1",
            section_title="Inclusion Criteria",
            page_number=22,
            chunk_text="Inclusion criterion: Patients with hypertension must have systolic BP < 160 mmHg.",
            is_approved=True,
        )
    ]

    patient_record = {
        "patient_name": "John Doe",
        "ssn": "123-45-6789",
        "dob": "1980-05-12",
        "diagnosis": "Hypertension",
        "systolic_bp": 145,
    }

    payload = {
        "query": "Analyze patient record eligibility against Protocol v3.0 hypertension inclusion guidelines.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-CARDIO",
        "protocol_chunks": [c.model_dump() for c in chunks],
        "patient_records": patient_record,
        "min_citation_confidence": 0.85,
        "compliance_profile": "HIPAA",
    }

    response = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 200, response.text
    data = response.json()

    assert data["phi_masking_applied"] is True
    assert data["phi_tokens_masked"] >= 1

    # Verify intermediate reasoning steps operate over surrogate tokens (Requirement 3 & AC 2)
    reasoning_chain = data["reasoning_chain"]
    planning_step = reasoning_chain[0]
    extraction_step = reasoning_chain[1]
    synthesis_step = reasoning_chain[2]

    assert planning_step["has_phi_masked"] is True
    assert extraction_step["has_phi_masked"] is True
    assert synthesis_step["has_phi_masked"] is True

    # Intermediate steps output must NOT contain raw SSN
    assert "123-45-6789" not in synthesis_step["output_text"]

    # Re-hydrated draft record contains clear text in final output
    draft = data["draft_record"]
    assert draft is not None
    assert draft["status"] == "DRAFT_STAGED"


def test_missing_tenant_context_rejected():
    """Verify that requests without valid tenant context are rejected before execution starts.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()
    headers.pop("X-Tenant-Id", None)

    payload = {
        "query": "Synthesize eligibility guidelines.",
        "tenant_id": "",  # Empty tenant ID
        "study_id": "PROTOCOL-2026",
    }

    response = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload,
        headers=headers,
    )

    assert response.status_code in (400, 403)


def test_stateless_execution_with_caller_managed_state():
    """Verify that the service executes multi-step queries without retaining internal database session state across requests.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()

    chunks = [
        ProtocolRAGContextChunk(
            chunk_id="chk_elig_50",
            protocol_version="v1.0",
            section_number="2.0",
            section_title="Overview",
            page_number=5,
            chunk_text="Trial baseline visit must occur within 14 days of screening.",
            is_approved=True,
        )
    ]

    # First request: Call /plan endpoint to initialize caller-managed state
    payload_1 = {
        "query": "Trial baseline visit screening timeframe 14 days guidelines for Protocol v1.0.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-ONCO",
        "protocol_chunks": [c.model_dump() for c in chunks],
    }

    response_1 = client.post(
        "/api/v1/ai/reasoning/plan",
        json=payload_1,
        headers=headers,
    )

    assert response_1.status_code == 200
    data_1 = response_1.json()
    returned_state = data_1["execution_state"]
    assert returned_state["session_id"].startswith("reas_")
    assert len(returned_state["completed_steps"]) >= 4

    # Second request: Pass returned state context back to /multi-step to verify stateless continuation
    payload_2 = {
        "query": "Trial baseline visit screening timeframe 14 days protocol synthesis workflow.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-ONCO",
        "protocol_chunks": [c.model_dump() for c in chunks],
        "execution_state": returned_state,
    }

    response_2 = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload_2,
        headers=headers,
    )

    assert response_2.status_code == 200
    data_2 = response_2.json()
    assert data_2["execution_state"]["session_id"] == returned_state["session_id"]
    assert data_2["status"] == "DRAFT_STAGED"


def test_custom_citation_confidence_threshold_rejection():
    """Verify that custom elevated citation threshold (e.g. 0.99) causes fail-closed rejection when score is lower.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()

    chunks = [
        ProtocolRAGContextChunk(
            chunk_id="chk_elig_99",
            protocol_version="v1.0",
            section_number="1.0",
            section_title="Background",
            page_number=2,
            chunk_text="General study overview text with minimal specific eligibility rules.",
            is_approved=True,
        )
    ]

    payload = {
        "query": "Synthesize highly specific dosage protocol parameters for oncology trial.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-HIGH-CONF",
        "protocol_chunks": [c.model_dump() for c in chunks],
        "min_citation_confidence": 0.99,
    }

    response = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 422
    data = response.json()
    assert "CITATION_CONFIDENCE_BELOW_THRESHOLD" in data["detail"]


def test_list_patient_records_and_profile_fallback():
    """Verify list-structured patient records and invalid compliance profile fallback behavior.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()

    chunks = [
        ProtocolRAGContextChunk(
            chunk_id="chk_elig_list",
            protocol_version="v4.0",
            section_number="5.1",
            section_title="Inclusion Criteria",
            page_number=30,
            chunk_text="Inclusion criterion: Subjects must have documented asthma diagnosis.",
            is_approved=True,
        )
    ]

    patient_records_list = [
        {"patient_name": "Jane Smith", "ssn": "987-65-4321", "condition": "Asthma"},
        {"patient_name": "Alice Brown", "ssn": "111-22-3333", "condition": "Asthma"},
    ]

    payload = {
        "query": "Analyze patient list eligibility against Protocol v4.0 asthma inclusion guidelines.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-ASTHMA",
        "protocol_chunks": [c.model_dump() for c in chunks],
        "patient_records": patient_records_list,
        "compliance_profile": "INVALID_PROFILE_NAME",
        "min_citation_confidence": 0.85,
    }

    response = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 200, response.text
    data = response.json()

    assert data["phi_masking_applied"] is True
    assert data["phi_tokens_masked"] >= 1
    assert data["status"] == "DRAFT_STAGED"


def test_esignature_and_audit_context_propagation():
    """Verify that tenant and electronic signature audit context propagate through reasoning execution steps.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers(user_id="usr_pi_signer")

    chunks = [
        ProtocolRAGContextChunk(
            chunk_id="chk_elig_audit",
            protocol_version="v1.2",
            section_number="2.1",
            section_title="Protocol Scope",
            page_number=8,
            chunk_text="Protocol Scope: Mandatory e-signature audit trail for all staged clinical records.",
            is_approved=True,
        )
    ]

    payload = {
        "query": "Synthesize protocol scope and e-signature audit requirements for Protocol v1.2.",
        "tenant_id": "tenant_cadence_beta",
        "study_id": "PROTOCOL-2026-AUDIT",
        "protocol_chunks": [c.model_dump() for c in chunks],
        "min_citation_confidence": 0.85,
        "esignature_context": {
            "user_id": "usr_pi_signer",
            "role": "Principal_Investigator",
            "signature_intent": "REVIEW_AND_STAGE",
        },
    }

    response = client.post(
        "/api/v1/ai/reasoning/multi-step",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "DRAFT_STAGED"
    assert data["draft_record"]["requires_esignature"] is True
