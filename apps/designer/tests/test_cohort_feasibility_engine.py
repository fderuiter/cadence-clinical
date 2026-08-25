"""
Comprehensive unit and integration test suite for Autonomous Agentic Multi-Criteria Cohort Reasoning & Synthetic Feasibility Engine.

Validates:
1. Asynchronous background worker execution without blocking API callers (PRD-SYS-051).
2. PostgreSQL / in-memory advisory locks preventing duplicate cohort analysis tasks.
3. Automated de-identification scrubbing (packages/deid) over patient contexts and evaluation prompts.
4. Three-valued logic (Kleene 3VL) feasibility simulation under complete, partial, and missing observation data.
5. Granular node-level AST evaluation fit traces and explanations.
6. Multi-criteria sub-cohort recommendations with criteria breakdown summaries and estimated fit percentages.
"""

import time

import pytest
from fastapi.testclient import TestClient

from apps.designer.application.services.cohort_feasibility_engine import (
    CohortFeasibilityEngine,
)
from apps.designer.domain.cohort_feasibility_models import (
    CohortFeasibilityRequest,
)
from apps.designer.domain.eligibility.evaluator import (
    evaluate_eligibility,
)
from apps.designer.domain.eligibility.models import (
    EligibilityCriterion,
    ExpressionNode,
    FieldReference,
)
from apps.designer.main import app
from apps.designer.workers.cohort_feasibility_worker import (
    acquire_advisory_lock,
    release_advisory_lock,
)
from packages.security.signing import generate_gateway_signature

client = TestClient(app)
GATEWAY_SECRET = b"internal-gateway-secret-12345"


def build_auth_headers(
    user_id: str = "usr_designer_lead",
    roles: str = "Study_Designer,Protocol_Author",
    tenant_id: str = "tenant_cadence_beta",
    change_reason: str = "Cohort feasibility simulation execution",
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


def create_sample_criteria() -> list[EligibilityCriterion]:
    """Helper to generate inclusion/exclusion AST criteria."""
    inc = EligibilityCriterion(
        id="INC_01",
        criterion_type="inclusion",
        identifier="INC-001",
        human_readable_text="Subject must be aged 18 to 75 years inclusive.",
        dsl_expression_string="eCRF.DM.AGE >= 18 and eCRF.DM.AGE <= 75",
        created_by="usr_designer_lead",
        reason_for_change="Protocol inclusion specification",
        structured_expression_tree=ExpressionNode(
            type="logical",
            operator="and",
            operands=[
                ExpressionNode(
                    type="comparison",
                    operator=">=",
                    operands=[
                        ExpressionNode(
                            type="field_ref",
                            field_ref=FieldReference(
                                raw_reference="eCRF.DM.AGE", domain="DM", variable="AGE"
                            ),
                        ),
                        ExpressionNode(type="constant", value=18),
                    ],
                ),
                ExpressionNode(
                    type="comparison",
                    operator="<=",
                    operands=[
                        ExpressionNode(
                            type="field_ref",
                            field_ref=FieldReference(
                                raw_reference="eCRF.DM.AGE", domain="DM", variable="AGE"
                            ),
                        ),
                        ExpressionNode(type="constant", value=75),
                    ],
                ),
            ],
        ),
        expected_outcome=True,
    )

    exc = EligibilityCriterion(
        id="EXC_01",
        criterion_type="exclusion",
        identifier="EXC-001",
        human_readable_text="Excludes patients with severe renal impairment (eGFR < 30 mL/min/1.73m2).",
        dsl_expression_string="eCRF.LB.EGFR < 30",
        created_by="usr_designer_lead",
        reason_for_change="Protocol exclusion specification",
        structured_expression_tree=ExpressionNode(
            type="comparison",
            operator="<",
            operands=[
                ExpressionNode(
                    type="field_ref",
                    field_ref=FieldReference(
                        raw_reference="eCRF.LB.EGFR", domain="LB", variable="EGFR"
                    ),
                ),
                ExpressionNode(type="constant", value=30),
            ],
        ),
        expected_outcome=False,
    )

    return [inc, exc]


@pytest.mark.asyncio
async def test_async_background_feasibility_execution():
    """Verify background worker processes feasibility requests asynchronously without blocking API caller.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()
    criteria = create_sample_criteria()

    payload = {
        "analysis_id": "feas_async_test_001",
        "study_id": "PROTOCOL-2026-ASYNC",
        "cohort_id": "COHORT_ASYNC_A",
        "structured_criteria": [c.model_dump(mode="json") for c in criteria],
        "synthetic_simulation_count": 15,
        "is_async": True,
    }

    # 1. Trigger evaluate endpoint asynchronously
    response = client.post(
        "/api/v1/designer/cohort-feasibility/evaluate",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 200, response.text
    data = response.json()
    assert data["job_id"] == "feas_async_test_001"
    assert data["status"] in ("PENDING", "RUNNING", "COMPLETED")

    # 2. Poll job status endpoint until COMPLETED
    job_completed = False
    for _ in range(10):
        time.sleep(0.2)
        poll_res = client.get(
            f"/api/v1/designer/cohort-feasibility/jobs/{data['job_id']}",
            headers=headers,
        )
        assert poll_res.status_code == 200
        poll_data = poll_res.json()
        if poll_data["status"] == "COMPLETED":
            job_completed = True
            report = poll_data["report"]
            assert report is not None
            assert report["starting_cohort_size"] == 15
            assert report["overall_eligibility_rate"] >= 0.0
            assert len(report["sub_cohort_recommendations"]) >= 1
            assert len(report["node_explanations"]) >= 15
            break

    assert job_completed is True


@pytest.mark.asyncio
async def test_advisory_lock_prevents_duplicate_processing():
    """Verify advisory lock prevents duplicate background jobs when multiple users trigger identical cohort analyses.

    @req:PRD-SYS-051
    """
    study_id = "PROTOCOL-2026-LOCK-TEST"
    cohort_id = "COHORT_LOCK_01"

    # 1. Manually acquire advisory lock for study cohort
    lock_acquired = await acquire_advisory_lock(
        session=None, study_id=study_id, cohort_id=cohort_id
    )
    assert lock_acquired is True

    try:
        # 2. Execute feasibility engine on locked cohort
        engine = CohortFeasibilityEngine()
        req = CohortFeasibilityRequest(
            analysis_id="feas_dup_test",
            study_id=study_id,
            cohort_id=cohort_id,
            structured_criteria=create_sample_criteria(),
            is_async=False,
        )
        report = await engine.execute_feasibility_analysis(req)

        # 3. Should return DUPLICATE_LOCKED status with advisory_lock_acquired=False
        assert report.status == "DUPLICATE_LOCKED"
        assert report.advisory_lock_acquired is False
        assert "Duplicate feasibility analysis job currently running" in (
            report.error_message or ""
        )

    finally:
        await release_advisory_lock(study_id=study_id, cohort_id=cohort_id)


def test_three_valued_logic_missing_data_simulation():
    """Verify engine correctly evaluates eligibility conditions with complete, partial, and missing data (3VL).

    @req:PRD-SYS-051
    """
    criteria = create_sample_criteria()

    # Profile 1: Complete Data & Eligible (Age 45, eGFR 90) -> TRUE
    pt_complete = {
        "eCRF.DM.AGE": 45,
        "eCRF.LB.EGFR": 90,
    }
    res_complete = evaluate_eligibility(criteria, pt_complete)
    assert res_complete.eligible is True
    assert len(res_complete.failed_criteria) == 0
    assert len(res_complete.indeterminate_criteria) == 0

    # Profile 2: Ineligible Data (Age 80 > 75) -> FALSE
    pt_ineligible = {
        "eCRF.DM.AGE": 80,
        "eCRF.LB.EGFR": 90,
    }
    res_ineligible = evaluate_eligibility(criteria, pt_ineligible)
    assert res_ineligible.eligible is False
    assert "INC_01" in res_ineligible.failed_criteria

    # Profile 3: Missing Observation Data (eGFR is None) -> INDETERMINATE
    pt_missing = {
        "eCRF.DM.AGE": 45,
        "eCRF.LB.EGFR": None,
    }
    res_missing = evaluate_eligibility(criteria, pt_missing)
    assert res_missing.eligible is None
    assert "EXC_01" in res_missing.indeterminate_criteria

    # Verify synthetic simulation endpoint returns missing data attrition rates
    headers = build_auth_headers()
    payload = {
        "study_id": "PROTOCOL-2026-3VL",
        "cohort_id": "COHORT_3VL",
        "structured_criteria": [c.model_dump(mode="json") for c in criteria],
        "synthetic_simulation_count": 20,
        "is_async": False,
    }

    response = client.post(
        "/api/v1/designer/cohort-feasibility/synthetic-simulate",
        json=payload,
        headers=headers,
    )
    assert response.status_code == 200, response.text
    report = response.json()

    sim = report["synthetic_simulation_summary"]
    assert sim is not None
    assert sim["simulated_subject_count"] == 20
    assert sim["complete_data_fit_rate"] >= 0.0
    assert sim["missing_data_fit_rate"] >= 0.0
    assert "eCRF.LB.EGFR" in sim["high_risk_missing_fields"]


def test_deidentification_scrubbing_on_evaluation_context():
    """Verify all outgoing prompts and patient contexts pass through de-identification scrubber (packages/deid).

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()
    criteria = create_sample_criteria()

    patient_with_phi = [
        {
            "subject_id": "PT_PHI_001",
            "patient_name": "Johnathan Doe",
            "ssn": "000-12-3456",
            "dob": "1975-08-20",
            "eCRF.DM.AGE": 50,
            "eCRF.LB.EGFR": 85.0,
        }
    ]

    payload = {
        "study_id": "PROTOCOL-2026-DEID",
        "cohort_id": "COHORT_DEID",
        "unstructured_protocol_text": "Inclusion guidelines for patient Johnathan Doe SSN 000-12-3456 born 1975-08-20.",
        "structured_criteria": [c.model_dump(mode="json") for c in criteria],
        "patient_profiles": patient_with_phi,
        "is_async": False,
        "compliance_profile": "HIPAA",
    }

    response = client.post(
        "/api/v1/designer/cohort-feasibility/evaluate",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 200, response.text
    report = response.json()

    assert report["deid_applied"] is True
    assert report["deid_tokens_count"] >= 1


def test_subcohort_recommendations_and_granular_node_explanations():
    """Verify sub-cohort recommendations include specific criteria breakdown summaries and granular node-level fit traces.

    @req:PRD-SYS-051
    """
    headers = build_auth_headers()
    criteria = create_sample_criteria()

    payload = {
        "study_id": "PROTOCOL-2026-RECOMMENDATIONS",
        "cohort_id": "COHORT_MAIN",
        "structured_criteria": [c.model_dump(mode="json") for c in criteria],
        "synthetic_simulation_count": 10,
        "is_async": False,
    }

    response = client.post(
        "/api/v1/designer/cohort-feasibility/evaluate",
        json=payload,
        headers=headers,
    )

    assert response.status_code == 200, response.text
    report = response.json()

    # 1. Sub-cohort recommendations validation
    sub_cohorts = report["sub_cohort_recommendations"]
    assert len(sub_cohorts) >= 2

    core_subcohort = next(
        s for s in sub_cohorts if s["sub_cohort_id"] == "subcohort_core_active"
    )
    assert core_subcohort["name"] == "Standard Active Trial Cohort"
    assert core_subcohort["estimated_fit_percentage"] >= 0.0
    assert len(core_subcohort["criteria_breakdown"]) == 2

    breakdown_inc = next(
        b for b in core_subcohort["criteria_breakdown"] if b["criterion_id"] == "INC_01"
    )
    assert breakdown_inc["identifier"] in ("INC-001", "INC_01")
    assert (
        breakdown_inc["passed_count"]
        + breakdown_inc["failed_count"]
        + breakdown_inc["indeterminate_count"]
        == 10
    )

    # 2. Granular node-level trace explanation validation
    explanations = report["node_explanations"]
    assert len(explanations) >= 10

    exp_item = explanations[0]
    assert exp_item["subject_id"].startswith("SYNTH_PT_")
    assert exp_item["result_status"] in ("TRUE", "FALSE", "INDETERMINATE")
    assert exp_item["node_trace"] is not None
    assert "evaluated to" in exp_item["explanation"]
