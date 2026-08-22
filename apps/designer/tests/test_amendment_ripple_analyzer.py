"""Tests for Protocol Amendment Ripple-Effect Analyzer and Multi-Domain Ticket Dispatch.

Requirements: PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
"""

import hashlib
import hmac
import json
import time
from typing import Any

import httpx
import pytest

from apps.designer.adapters.tickets_client import DesignerTicketsClient
from apps.designer.application.services.ripple_analyzer import (
    ProtocolAmendmentRippleAnalyzer,
)
from apps.designer.domain.cdisc.ripple_models import (
    DomainQueue,
    ProtocolImpactAssessment,
)
from apps.designer.main import app as designer_app
from apps.tickets.adapters.database import db_manager as tickets_db_manager
from apps.tickets.adapters.models import Base as TicketsBase
from apps.tickets.main import app as tickets_app

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_designer_auth_headers(
    user_id: str = "sponsor_designer_01",
    roles: str = "STUDY_DESIGNER",
    change_reason: str = "Protocol Amendment Ripple Effect Analysis",
) -> dict[str, str]:
    timestamp = str(time.time())
    payload = {
        "change_reason": change_reason,
        "roles": roles,
        "timestamp": timestamp,
        "user_id": user_id,
    }
    serialized = json.dumps(payload, sort_keys=True, separators=(",", ":"))
    signature = hmac.new(
        GATEWAY_SECRET.encode(), serialized.encode(), hashlib.sha256
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
def baseline_protocol_payload() -> dict[str, Any]:
    """Baseline Protocol v1.0.0 payload."""
    return {
        "id": "CADENCE-101",
        "name": "CADENCE-101 Baseline Protocol",
        "version": "1.0.0",
        "arms": [
            {
                "id": "arm_a",
                "name": "Arm A: Active Dose",
                "description": "Cohort 1: 100mg Daily",
                "dose": "100mg",
            }
        ],
        "visits": [
            {
                "id": "v1",
                "name": "Visit 1: Screening",
                "spec": "Demographics, Eligibility",
                "schedule": "Day -7",
                "window": "Day -7 (+/- 2 days)",
            },
            {
                "id": "v2",
                "name": "Visit 2: Baseline",
                "spec": "Vitals, ECG, Labs",
                "schedule": "Day 1",
                "window": "Day 1 (0 days)",
            },
            {
                "id": "v3",
                "name": "Visit 3: Treatment Cycle 1",
                "spec": "Dosing, Safety Labs",
                "schedule": "Day 14",
                "window": "Day 14 (+/- 3 days)",
            },
        ],
        "activities": [
            {
                "id": "act_chem",
                "name": "Standard Safety Chemistry",
                "spec": "Assay: CBC + Chem Panel",
                "schedule": "Bi-weekly",
            }
        ],
        "eligibilityCriteria": [
            {"id": "crit_01", "text": "Age >= 18 and Age <= 75"},
            {"id": "crit_02", "text": "Confirmed solid tumor diagnosis"},
        ],
        "forms": [
            {"id": "f_demo", "form_key": "DEMO", "name": "Demographics"},
            {"id": "f_vs", "form_key": "VITALS", "name": "Vital Signs"},
        ],
        "narrative": {
            "synopsis": {
                "title": "Protocol Synopsis",
                "text": "A Phase II study evaluating efficacy and safety of novel inhibitor in solid tumors.",
            },
            "safety_profile": {
                "title": "Safety and Risk Profile",
                "text": "Standard safety monitoring of hematological and metabolic parameters.",
            },
        },
    }


@pytest.fixture
def amended_protocol_payload() -> dict[str, Any]:
    """Amended Protocol v2.0.0 payload introducing PK encounter, biomarker assay, new cohort, and safety advisory."""
    return {
        "id": "CADENCE-101",
        "name": "CADENCE-101 Protocol (Amended v2.0.0)",
        "version": "2.0.0",
        "arms": [
            {
                "id": "arm_a",
                "name": "Arm A: Active Dose",
                "description": "Cohort 1: 100mg Daily",
                "dose": "100mg",
            },
            {
                "id": "arm_b",
                "name": "Arm B: High Dose Escalation",
                "description": "Cohort 2: 200mg Daily Escalation",
                "dose": "200mg",
            },
        ],
        "visits": [
            {
                "id": "v1",
                "name": "Visit 1: Screening",
                "spec": "Demographics, Eligibility",
                "schedule": "Day -7",
                "window": "Day -7 (+/- 2 days)",
            },
            {
                "id": "v2",
                "name": "Visit 2: Baseline",
                "spec": "Vitals, ECG, Labs",
                "schedule": "Day 1",
                "window": "Day 1 (0 days)",
            },
            {
                "id": "v3",
                "name": "Visit 3: Treatment Cycle 1",
                "spec": "Dosing, Safety Labs, PK Blood Draw",
                "schedule": "Day 14",
                "window": "Day 14 (+/- 1 day)",  # Adjusted window
                "delta_note": "Added PK Blood Draw form and tightened window.",
            },
            {
                "id": "v3_5",
                "name": "Visit 3.5: Interim PK Assessment",
                "spec": "Pharmacokinetics, Biomarkers",
                "schedule": "Day 21",
                "window": "Day 21 (+/- 1 day)",
                "delta_note": "New mid-cycle pharmacokinetic visit added in Amendment 2.0.",
            },
        ],
        "activities": [
            {
                "id": "act_chem",
                "name": "Standard Safety Chemistry",
                "spec": "Assay: CBC + Chem Panel + Biomarkers",
                "schedule": "Bi-weekly",
                "delta_note": "Added high-sensitivity troponin biomarker requirement.",
            },
            {
                "id": "act_pk",
                "name": "PK Blood Draw",
                "spec": "Pharmacokinetics Plasma Assay",
                "schedule": "Visit 3, Visit 3.5",
                "delta_note": "Added PK blood draw procedure.",
            },
        ],
        "eligibilityCriteria": [
            {"id": "crit_01", "text": "Age >= 18 and Age <= 70"},  # Tightened age
            {"id": "crit_02", "text": "Confirmed solid tumor diagnosis"},
            {"id": "crit_03", "text": "Signed informed consent (v2.0)"},  # Added
        ],
        "forms": [
            {"id": "f_demo", "form_key": "DEMO", "name": "Demographics"},
            {"id": "f_vs", "form_key": "VITALS", "name": "Vital Signs"},
            {
                "id": "f_pk",
                "form_key": "PK_ASSAY",
                "name": "Pharmacokinetics Blood Draw",
            },
        ],
        "narrative": {
            "synopsis": {
                "title": "Protocol Synopsis",
                "text": "A Phase II study evaluating efficacy and safety with added 200mg dose escalation cohort.",
            },
            "safety_profile": {
                "title": "Safety and Risk Profile",
                "text": "Cardiotoxicity safety warning: elevated troponin requires dose reduction and immediate safety alert notification.",
            },
        },
    }


def test_ripple_analyzer_graph_and_soa_deltas(
    baseline_protocol_payload: dict[str, Any],
    amended_protocol_payload: dict[str, Any],
) -> None:
    """Validate that ProtocolAmendmentRippleAnalyzer accurately evaluates USDM graph and SoA deltas.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    analyzer = ProtocolAmendmentRippleAnalyzer()
    assessment: ProtocolImpactAssessment = analyzer.analyze_amendment_impact(
        study_id="CADENCE-101",
        base_version_tag="1.0.0",
        amended_version_tag="2.0.0",
        amendment_type="major",
        base_payload=baseline_protocol_payload,
        draft_payload=amended_protocol_payload,
    )

    assert assessment.study_id == "CADENCE-101"
    assert assessment.base_version == "1.0.0"
    assert assessment.amended_version == "2.0.0"
    assert assessment.is_substantial is True
    assert assessment.requires_reconsent is True
    assert assessment.patient_burden_delta > 0.0

    # Graph deltas: 1 added arm (Arm B)
    added_arms = [
        d
        for d in assessment.graph_deltas
        if d.change_type == "ADDED" and d.entity_type == "Arm"
    ]
    assert len(added_arms) == 1
    assert "Arm B" in added_arms[0].name

    # SoA deltas: 1 added visit (Visit 3.5), 1 modified visit (Visit 3), 1 added activity (PK Blood Draw)
    added_visits = [
        d
        for d in assessment.soa_deltas
        if d.change_type == "ADDED" and d.entity_type == "Encounter"
    ]
    assert len(added_visits) == 1
    assert "Visit 3.5" in added_visits[0].name

    modified_visits = [
        d
        for d in assessment.soa_deltas
        if d.change_type == "MODIFIED" and d.entity_type == "Encounter"
    ]
    assert len(modified_visits) == 1
    assert "Visit 3" in modified_visits[0].name

    added_acts = [
        d
        for d in assessment.soa_deltas
        if d.change_type == "ADDED" and d.entity_type == "Activity"
    ]
    assert len(added_acts) == 1
    assert "PK Blood Draw" in added_acts[0].name


def test_ripple_analyzer_narrative_deltas_and_safety_triggers(
    baseline_protocol_payload: dict[str, Any],
    amended_protocol_payload: dict[str, Any],
) -> None:
    """Validate narrative delta parsing and automated safety risk keyword detection.

    @req:PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
    """
    analyzer = ProtocolAmendmentRippleAnalyzer()
    assessment = analyzer.analyze_amendment_impact(
        study_id="CADENCE-101",
        base_version_tag="1.0.0",
        amended_version_tag="2.0.0",
        amendment_type="major",
        base_payload=baseline_protocol_payload,
        draft_payload=amended_protocol_payload,
    )

    assert len(assessment.narrative_deltas) >= 2
    safety_delta = next(
        (nd for nd in assessment.narrative_deltas if nd.section_id == "safety_profile"),
        None,
    )
    assert safety_delta is not None
    assert safety_delta.change_type == "MODIFIED"
    assert safety_delta.safety_risk_impact is True

    # Regulatory manifest must flag HIGH or CRITICAL safety risk and FULL_COMMITTEE submission
    reg_impact = assessment.regulatory_compliance
    assert reg_impact.safety_risk_level in ("HIGH", "CRITICAL")
    assert reg_impact.irb_iec_submission_type == "FULL_COMMITTEE"
    assert reg_impact.requires_reconsent is True


def test_ripple_analyzer_domain_manifests(
    baseline_protocol_payload: dict[str, Any],
    amended_protocol_payload: dict[str, Any],
) -> None:
    """Validate synthesis of DATA_CAPTURE_ECRF, SUBJECT_MANAGEMENT_RTSM, and REGULATORY_COMPLIANCE domain manifests.

    @req:PRD-SYS-001, PRD-SUB-007
    """
    analyzer = ProtocolAmendmentRippleAnalyzer()
    assessment = analyzer.analyze_amendment_impact(
        study_id="CADENCE-101",
        base_version_tag="1.0.0",
        amended_version_tag="2.0.0",
        amendment_type="major",
        base_payload=baseline_protocol_payload,
        draft_payload=amended_protocol_payload,
    )

    # 1. Data Capture (eCRF) Manifest
    ecrf = assessment.data_capture_ecrf
    assert len(ecrf.added_forms) == 1
    assert (
        "Pharmacokinetics Blood Draw" in ecrf.added_forms[0]
        or "f_pk" in ecrf.added_forms[0]
    )
    assert ecrf.estimated_build_hours > 0.0
    assert any("PKDAT" in f for f in ecrf.new_cdash_fields)
    assert len(ecrf.action_items) >= 3

    # 2. RTSM Manifest
    rtsm = assessment.subject_management_rtsm
    assert rtsm.cohort_adjustments_count >= 1
    assert len(rtsm.visit_window_adjustments) >= 1
    assert rtsm.requires_kit_reallocation is True
    assert rtsm.randomization_ratio_changed is True
    assert len(rtsm.action_items) >= 3

    # 3. Regulatory Manifest
    reg = assessment.regulatory_compliance
    assert reg.is_substantial_amendment is True
    assert reg.icf_version_upgrade == "v2.0.0"
    assert "ACTIVE" in reg.affected_subject_cohorts
    assert len(reg.action_items) >= 3


def test_reconsent_gating_plan_flags_active_cohort_subjects(
    baseline_protocol_payload: dict[str, Any],
    amended_protocol_payload: dict[str, Any],
) -> None:
    """Validate in-flight subject re-consent gating flags active cohort subjects accurately.

    @req:PRD-SUB-007, PRD-SYS-001
    """
    analyzer = ProtocolAmendmentRippleAnalyzer()

    # Case A: Major amendment with explicit subject list
    custom_subjects = ["SUBJ-101", "SUBJ-102", "SUBJ-105", "SUBJ-108"]
    assessment_major = analyzer.analyze_amendment_impact(
        study_id="CADENCE-101",
        base_version_tag="1.0.0",
        amended_version_tag="2.0.0",
        amendment_type="major",
        base_payload=baseline_protocol_payload,
        draft_payload=amended_protocol_payload,
        active_subject_ids=custom_subjects,
    )

    gating = assessment_major.reconsent_gating_plan
    assert gating.gating_mandated is True
    assert gating.flagged_subject_count == 4
    assert gating.flagged_subject_ids == sorted(custom_subjects)
    assert "re-consent is mandated" in gating.justification

    # Case B: Minor administrative amendment without safety or SoA additions
    admin_amended_payload = {
        "id": "CADENCE-101",
        "name": "CADENCE-101 (Admin Fix)",
        "version": "1.0.1",
        "arms": baseline_protocol_payload["arms"],
        "visits": baseline_protocol_payload["visits"],
        "activities": baseline_protocol_payload["activities"],
        "eligibilityCriteria": baseline_protocol_payload["eligibilityCriteria"],
        "forms": baseline_protocol_payload["forms"],
        "narrative": {
            "synopsis": {
                "title": "Protocol Synopsis",
                "text": "A Phase II study evaluating efficacy and safety (sponsor contact updated).",
            }
        },
    }

    assessment_minor = analyzer.analyze_amendment_impact(
        study_id="CADENCE-101",
        base_version_tag="1.0.0",
        amended_version_tag="1.0.1",
        amendment_type="minor",
        requires_reconsent_override=False,
        base_payload=baseline_protocol_payload,
        draft_payload=admin_amended_payload,
    )

    assert assessment_minor.requires_reconsent is False
    assert assessment_minor.reconsent_gating_plan.gating_mandated is False
    assert assessment_minor.reconsent_gating_plan.flagged_subject_count == 0


def test_operational_tickets_generation_and_action_plans(
    baseline_protocol_payload: dict[str, Any],
    amended_protocol_payload: dict[str, Any],
) -> None:
    """Validate that structured, actionable operational tickets are generated for all 3 domain queues.

    @req:PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
    """
    analyzer = ProtocolAmendmentRippleAnalyzer()
    assessment = analyzer.analyze_amendment_impact(
        study_id="CADENCE-101",
        base_version_tag="1.0.0",
        amended_version_tag="2.0.0",
        amendment_type="major",
        base_payload=baseline_protocol_payload,
        draft_payload=amended_protocol_payload,
    )

    tickets = assessment.operational_tickets
    assert len(tickets) == 3

    queues = {t.domain_queue for t in tickets}
    assert DomainQueue.DATA_CAPTURE_ECRF in queues
    assert DomainQueue.SUBJECT_MANAGEMENT_RTSM in queues
    assert DomainQueue.REGULATORY_COMPLIANCE in queues

    for t in tickets:
        assert t.title.startswith("[")
        assert "CADENCE-101" in t.title
        assert len(t.action_plan) >= 3
        assert t.assignee_role is not None
        assert t.priority in ("HIGH", "CRITICAL", "MEDIUM")
        assert t.gxp_severity in ("MAJOR", "CRITICAL")
        assert "domain_queue" in t.context_payload


@pytest.mark.asyncio
async def test_tickets_client_dispatch_integration(
    baseline_protocol_payload: dict[str, Any],
    amended_protocol_payload: dict[str, Any],
) -> None:
    """Validate that DesignerTicketsClient dispatches operational tickets to apps/tickets via ASGI transport.

    @req:PRD-SYS-001, PRD-SYS-051
    """
    # Initialize in-memory tickets db for real ASGI verification
    tickets_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with tickets_db_manager.engine.begin() as conn:
        await conn.run_sync(TicketsBase.metadata.create_all)

    try:
        analyzer = ProtocolAmendmentRippleAnalyzer()
        assessment = analyzer.analyze_amendment_impact(
            study_id="CADENCE-101",
            base_version_tag="1.0.0",
            amended_version_tag="2.0.0",
            amendment_type="major",
            base_payload=baseline_protocol_payload,
            draft_payload=amended_protocol_payload,
        )

        transport = httpx.ASGITransport(app=tickets_app)
        client = DesignerTicketsClient(transport=transport)

        dispatched = await client.dispatch_batch(
            blueprints=assessment.operational_tickets,
            study_id="CADENCE-101",
            user_id="sponsor_designer_01",
            change_reason="Protocol Amendment 2.0 Operational Ticket Dispatch",
        )

        assert len(dispatched) == 3
        for d in dispatched:
            assert d.reference.startswith("TKT-")
            assert d.status == "OPEN"
            assert d.domain_queue in (
                DomainQueue.DATA_CAPTURE_ECRF,
                DomainQueue.SUBJECT_MANAGEMENT_RTSM,
                DomainQueue.REGULATORY_COMPLIANCE,
            )
    finally:
        if tickets_db_manager.engine is not None:
            async with tickets_db_manager.engine.begin() as conn:
                await conn.run_sync(TicketsBase.metadata.drop_all)
            await tickets_db_manager.close()


@pytest.mark.asyncio
async def test_designer_amendments_api_endpoints(
    baseline_protocol_payload: dict[str, Any],
    amended_protocol_payload: dict[str, Any],
) -> None:
    """Validate FastAPI endpoints POST /analyze-ripple and POST /dispatch-tickets on apps/designer.

    @req:PRD-SYS-001, PRD-SUB-007, PRD-SYS-051
    """
    headers = get_designer_auth_headers()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=designer_app), base_url="http://test"
    ) as client:
        # 1. Test POST /api/v1/designer/amendments/analyze-ripple
        res_ripple = await client.post(
            "/api/v1/designer/amendments/analyze-ripple",
            json={
                "study_id": "CADENCE-101",
                "base_version_tag": "1.0.0",
                "amended_version_tag": "2.0.0",
                "amendment_type": "major",
                "base_payload": baseline_protocol_payload,
                "draft_payload": amended_protocol_payload,
                "active_subject_ids": ["SUBJ-001", "SUBJ-002"],
            },
            headers=headers,
        )

        assert res_ripple.status_code == 200
        assessment_data = res_ripple.json()
        assert assessment_data["study_id"] == "CADENCE-101"
        assert assessment_data["is_substantial"] is True
        assert assessment_data["requires_reconsent"] is True
        assert assessment_data["reconsent_gating_plan"]["flagged_subject_count"] == 2
        assert len(assessment_data["operational_tickets"]) == 3

        # 2. Test POST /api/v1/designer/amendments/dispatch-tickets
        res_dispatch = await client.post(
            "/api/v1/designer/amendments/dispatch-tickets",
            json={
                "study_id": "CADENCE-101",
                "impact_assessment": assessment_data,
            },
            headers=headers,
        )

        assert res_dispatch.status_code == 200
        dispatch_data = res_dispatch.json()
        assert dispatch_data["study_id"] == "CADENCE-101"
        assert dispatch_data["total_dispatched"] == 3
        assert len(dispatch_data["dispatched_tickets"]) == 3
        for t in dispatch_data["dispatched_tickets"]:
            assert t["reference"].startswith("TKT-")
