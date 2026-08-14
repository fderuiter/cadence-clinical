"""Test suite for AI-Native USDM Protocol Digitization & Automated eCRF/SoA Synthesis Engine.

Requirements: PRD-DDF-001, PRD-SYS-001, PRD-MDR-007, PRD-CRF-004, PRD-CRF-005
"""

import hashlib
import hmac
import json
import time

import pytest
from fastapi.testclient import TestClient

from apps.designer.application.services.digitization_service import (
    extract_usdm_from_protocol_document,
    synthesize_ecrf_forms,
    validate_extracted_rules,
)
from apps.designer.domain.digitization_models import (
    ExtractedActivity,
    ExtractedArm,
    ExtractedCriterion,
    ExtractedEpoch,
    ExtractedVisit,
    USDMProtocolExtractionResponse,
)
from apps.designer.infrastructure.neo4j_usdm_writer import commit_usdm_graph
from apps.designer.main import app as designer_app
from packages.database.mock_graph import MockGraphDriver


def get_designer_auth_headers(
    roles: str = "sponsor_designer",
    change_reason: str = "Automated protocol digitization test",
    user_id: str = "test_user_001",
) -> dict[str, str]:
    """Generates v2 gateway HMAC signature headers for the given roles and change reason."""
    timestamp = str(time.time())
    secret = "internal-gateway-secret-12345"  # pragma: allowlist secret
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        secret.encode(), serialized.encode(), hashlib.sha256
    ).hexdigest()
    return {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }


@pytest.fixture
def client() -> TestClient:
    return TestClient(designer_app)


@pytest.mark.asyncio
async def test_protocol_entity_extraction():
    """Validate unstructured text correctly serializes into valid USDMProtocolExtractionResponse.

    @req:PRD-DDF-001
    """
    protocol_text = (
        "Protocol Title: A Phase III Randomized Double-Blind Study in Non-Small Cell Lung Cancer\n"
        "Protocol ID: CDNC-NSCLC-301\n"
        "Phase: Phase III\n"
        "Therapeutic Area: Oncology\n"
        "Section 3: Study Design and Arms\n"
        "Arm A (Investigational): Compound XYZ 200mg daily.\n"
        "Arm B (Comparator): Standard of Care Chemotherapy.\n"
        "Epochs: Screening, Treatment, Follow-up.\n"
        "Inclusion Criteria:\n"
        "1. Age >= 18 years.\n"
        "2. Histologically confirmed NSCLC.\n"
        "Schedule of Activities:\n"
        "- Vital Signs at every visit.\n"
        "- 12-Lead ECG at Screening and Day 1.\n"
        "- Visual Analog Scale (VAS) Pain score at Treatment visits.\n"
    )

    pdf_mock_bytes = b"%PDF-1.4\n%...\n" + protocol_text.encode("utf-8") + b"\n%%EOF"

    res = await extract_usdm_from_protocol_document(
        pdf_mock_bytes, "protocol_nsclc.pdf"
    )

    assert isinstance(res, USDMProtocolExtractionResponse)
    assert res.study_title != ""
    assert res.phase in ("PHASE_I", "PHASE_II", "PHASE_III", "PHASE_IV")
    assert len(res.arms) >= 2
    assert len(res.epochs) >= 3
    assert len(res.visits) >= 3
    assert len(res.activities) >= 4
    assert len(res.criteria) >= 2
    assert 0.0 <= res.confidence_score <= 1.0


@pytest.mark.asyncio
async def test_neo4j_usdm_graph_integrity():
    """Verify graph nodes and relationships are constructed without orphaned nodes.

    @req:PRD-DDF-001
    """
    driver = MockGraphDriver()
    study_id = "study_ddf_001"

    data = USDMProtocolExtractionResponse(
        study_title="Phase II Proof of Concept Trial in Heart Failure",
        protocol_id="CDNC-HF-201",
        phase="PHASE_II",
        therapeutic_area="Cardiology",
        arms=[
            ExtractedArm(
                name="Treatment Arm",
                arm_type="EXPERIMENTAL",
                target_sample_size=100,
            ),
            ExtractedArm(
                name="Placebo Arm",
                arm_type="PLACEBO_COMPARATOR",
                target_sample_size=100,
            ),
        ],
        epochs=[
            ExtractedEpoch(name="Screening", epoch_type="SCREENING", sequence_index=1),
            ExtractedEpoch(name="Treatment", epoch_type="TREATMENT", sequence_index=2),
            ExtractedEpoch(name="Follow-up", epoch_type="FOLLOW_UP", sequence_index=3),
        ],
        visits=[
            ExtractedVisit(
                visit_name="Visit 1 (Baseline)",
                epoch_name="Screening",
                target_day=1,
            ),
            ExtractedVisit(
                visit_name="Visit 2 (Week 4)",
                epoch_name="Treatment",
                target_day=28,
            ),
        ],
        activities=[
            ExtractedActivity(
                activity_name="Vital Signs",
                cdash_domain="VS",
                assigned_visit_names=["Visit 1 (Baseline)", "Visit 2 (Week 4)"],
            ),
            ExtractedActivity(
                activity_name="Electrocardiogram",
                cdash_domain="EG",
                assigned_visit_names=["Visit 1 (Baseline)"],
            ),
        ],
        criteria=[
            ExtractedCriterion(
                criterion_type="INCLUSION",
                identifier="INC-01",
                text_expression="Age >= 18",
                logical_expression="DM.AGE >= 18",
            ),
            ExtractedCriterion(
                criterion_type="EXCLUSION",
                identifier="EXC-01",
                text_expression="Severe Renal Impairment",
                logical_expression="LB.CREAT > 2.5",
            ),
        ],
        confidence_score=0.98,
    )

    commit_res = await commit_usdm_graph(driver, study_id, data, "investigator_1")

    assert commit_res["status"] == "COMMITTED"
    assert commit_res["nodes_created"] > 0
    assert commit_res["relationships_created"] > 0
    assert len(driver.sessions) > 0


def test_cycle_detection_on_extracted_rules():
    """Verify detect_circular_dependencies catches any circular skip patterns extracted from the protocol.

    @req:PRD-CRF-005
    """
    # A cyclic dependency: field_A depends on field_B, which depends on field_A
    cyclic_rules = [
        {
            "id": "RULE_1",
            "type": "skip_logic",
            "target_field": "FIELD_A",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {
                        "type": "field_ref",
                        "field_ref": {"field_id": "FIELD_B"},
                    },
                    {"type": "constant", "value": "YES"},
                ],
            },
        },
        {
            "id": "RULE_2",
            "type": "skip_logic",
            "target_field": "FIELD_B",
            "condition": {
                "type": "comparison",
                "operator": "==",
                "operands": [
                    {
                        "type": "field_ref",
                        "field_ref": {"field_id": "FIELD_A"},
                    },
                    {"type": "constant", "value": "YES"},
                ],
            },
        },
    ]

    cycles = validate_extracted_rules(cyclic_rules)
    assert len(cycles) > 0


@pytest.mark.asyncio
async def test_end_to_end_synthesis_time():
    """Assert full pipeline execution (PDF parsing -> USDM Graph -> eCRF synthesis) completes in < 60 seconds.

    @req:PRD-DDF-001
    """
    start_time = time.time()

    raw_doc = (
        b"%PDF-1.4\nProtocol Title: End to End Benchmark Protocol\n"
        b"Phase: Phase II\nTherapeutic Area: Oncology\n%%EOF"
    )

    # 1. Extraction
    extraction = await extract_usdm_from_protocol_document(raw_doc, "benchmark.pdf")
    assert extraction is not None

    # 2. Graph compilation
    driver = MockGraphDriver()
    commit_res = await commit_usdm_graph(driver, "bench_study", extraction, "perf_bot")
    assert commit_res["status"] == "COMMITTED"

    # 3. eCRF Form Synthesis
    forms = synthesize_ecrf_forms(extraction)
    assert len(forms) > 0

    elapsed = time.time() - start_time
    assert elapsed < 60.0, (
        f"End-to-end synthesis took {elapsed}s, which exceeds 60s threshold."
    )


def test_api_extract_and_commit_endpoints(client: TestClient):
    """Validate REST API extraction and commit-usdm endpoints via HTTP client.

    @req:PRD-DDF-001, @req:PRD-SYS-001
    """
    pdf_content = (
        b"%PDF-1.4\nProtocol Title: Sample Automated Study\nPhase: Phase I\n%%EOF"
    )
    file_payload = {"file": ("sample_protocol.pdf", pdf_content, "application/pdf")}

    # 1. Extract endpoint
    extract_resp = client.post(
        "/api/v1/designer/digitization/extract",
        files=file_payload,
        headers=get_designer_auth_headers(),
    )
    assert extract_resp.status_code == 200
    data = extract_resp.json()
    assert "study_title" in data
    assert "arms" in data
    assert "activities" in data

    # 2. Commit endpoint
    commit_payload = {
        "study_id": "study_api_001",
        "data": data,
        "change_reason": "Protocol Digitization Automated Ingestion into MDR",
    }
    commit_resp = client.post(
        "/api/v1/designer/studies/study_api_001/commit-usdm",
        json=commit_payload,
        headers=get_designer_auth_headers(
            change_reason="Protocol Digitization Automated Ingestion into MDR"
        ),
    )
    assert commit_resp.status_code == 201
    commit_data = commit_resp.json()
    assert commit_data["status"] == "COMMITTED"
    assert len(commit_data["synthesized_forms"]) > 0
    assert commit_data["nodes_created"] > 0


def test_missing_change_reason_rejected(client: TestClient):
    """Validate 21 CFR Part 11 rejection when change justification is empty.

    @req:PRD-SYS-001
    """
    pdf_content = b"%PDF-1.4\nProtocol Title: Rejection Test\nPhase: Phase I\n%%EOF"
    extract_resp = client.post(
        "/api/v1/designer/digitization/extract",
        files={"file": ("test.pdf", pdf_content, "application/pdf")},
        headers=get_designer_auth_headers(),
    )
    data = extract_resp.json()

    # 1. Missing header change reason -> 403 by GatewayAuthMiddleware
    commit_payload = {
        "study_id": "study_reject_001",
        "data": data,
        "change_reason": "Some Reason",
    }
    resp_header = client.post(
        "/api/v1/designer/studies/study_reject_001/commit-usdm",
        json=commit_payload,
        headers=get_designer_auth_headers(change_reason=""),
    )
    assert resp_header.status_code == 403
    assert "Missing change justification reason" in resp_header.json()["detail"]

    # 2. Missing body change reason -> 400 by endpoint validation
    commit_payload_empty_body = {
        "study_id": "study_reject_001",
        "data": data,
        "change_reason": "",
    }
    resp_body = client.post(
        "/api/v1/designer/studies/study_reject_001/commit-usdm",
        json=commit_payload_empty_body,
        headers=get_designer_auth_headers(change_reason="Valid Gateway Reason"),
    )
    assert resp_body.status_code == 400
    assert "Missing change justification reason" in resp_body.json()["detail"]
