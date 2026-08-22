"""Comprehensive integration and unit test suite for Generative Pharmacovigilance Safety Narratives.

Requirements: PRD-SYS-051, PRD-SYS-052
"""

import os
import time
import uuid
from typing import Any

import httpx
import pytest
import pytest_asyncio
from fastapi import HTTPException
from fastapi.testclient import TestClient
from jose import jwt
from sqlalchemy import select, text

from apps.gateway.main import generate_signature
from apps.safety.database import db_manager
from apps.safety.domain.narrative_models import (
    NarrativeSectionType,
    TimelineEventType,
)
from apps.safety.main import app
from apps.safety.models import (
    Base,
    SafetyAuditLog,
    SafetyNarrative,
)
from apps.safety.services.narrative_service import SafetyNarrativeService
from apps.safety.services.timeline_aggregator import (
    build_timeline_from_sdtm_records,
)
from packages.database.audit import AIReviewStatus

pytestmark = pytest.mark.xdist_group("safety_narratives")


@pytest_asyncio.fixture(autouse=True)
async def setup_narrative_db():
    """Setup in-memory database for safety narrative tests."""
    db_uri = f"sqlite+aiosqlite:///file:memdb_narr_{uuid.uuid4().hex}?mode=memory&cache=shared&uri=true"
    db_manager.init_db(db_uri, echo=False)

    async with db_manager.engine.begin() as conn:
        if db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        async with db_manager.engine.begin() as conn:
            await conn.run_sync(Base.metadata.drop_all)
        await db_manager.close()


def make_sig_token(user_id: str, action: str = "") -> str:
    """Creates a mock 21 CFR Part 11 signature token JWT."""
    secret = os.getenv("GATEWAY_SECRET", "internal-gateway-secret-12345")
    payload = {
        "sub": user_id,
        "action": action,
        "exp": int(time.time()) + 300,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(payload, secret, algorithm="HS256")


def get_signed_headers(
    roles: str = "sponsor_medical_monitor",
    user_id: str = "dr_safety_physician_01",
    change_reason: str = "Generative Safety Narrative Action",
    sig_token: str | None = None,
) -> dict[str, str]:
    """Helper to generate internal Gateway HMAC authentication headers."""
    timestamp = str(time.time())
    sig = generate_signature(
        user_id,
        roles,
        timestamp,
        version="2",
        change_reason=change_reason,
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
    }
    if sig_token:
        headers["X-Signature-Token"] = sig_token
        headers["X-Sig-Token"] = sig_token
    return headers


def get_sample_sdtm_bundle(
    subject_id: str = "SUBJ-101",
) -> dict[str, list[dict[str, Any]]]:
    """Provides representative SDTM clinical domain datasets for narrative synthesis."""
    return {
        "DM": [
            {
                "STUDYID": "STUDY-ONCO-2026",
                "USUBJID": subject_id,
                "SUBJID": subject_id,
                "AGE": 64,
                "AGEU": "YEARS",
                "SEX": "M",
                "RACE": "WHITE",
                "ARM": "Investigational Kinase Inhibitor 200mg",
                "RFSTDTC": "2026-06-01",
            }
        ],
        "MH": [
            {
                "USUBJID": subject_id,
                "MHTERM": "Type 2 Diabetes Mellitus",
                "MHDECOD": "Type 2 Diabetes Mellitus",
                "MHBODSYS": "Endocrine disorders",
                "MHSTDTC": "2018-03-15",
                "MHSEQ": 1,
            },
            {
                "USUBJID": subject_id,
                "MHTERM": "Essential Hypertension",
                "MHDECOD": "Hypertension",
                "MHBODSYS": "Vascular disorders",
                "MHSTDTC": "2015-11-20",
                "MHSEQ": 2,
            },
        ],
        "CM": [
            {
                "USUBJID": subject_id,
                "CMTRT": "Metformin",
                "CMDOSE": "500",
                "CMDOSU": "mg",
                "CMROUTE": "ORAL",
                "CMINDC": "Diabetes",
                "CMSTDTC": "2018-04-01",
                "CMSEQ": 1,
            },
            {
                "USUBJID": subject_id,
                "CMTRT": "Lisinopril",
                "CMDOSE": "10",
                "CMDOSU": "mg",
                "CMROUTE": "ORAL",
                "CMINDC": "Hypertension",
                "CMSTDTC": "2016-01-10",
                "CMSEQ": 2,
            },
        ],
        "EX": [
            {
                "USUBJID": subject_id,
                "EXTRT": "CAD-909",
                "EXDOSE": "200",
                "EXDOSU": "mg",
                "EXSTDTC": "2026-06-01",
                "EXSEQ": 1,
            }
        ],
        "AE": [
            {
                "USUBJID": subject_id,
                "AETERM": "Severe Acute Pancreatitis",
                "AEDECOD": "Pancreatitis acute",
                "AESTDTC": "2026-06-20",
                "AEENDTC": "2026-06-28",
                "AESEV": "SEVERE",
                "AESER": "Y",
                "AEREL": "PROBABLE",
                "AEOUT": "RECOVERED",
                "AESEQ": 1,
                "AESHOSP": "Y",
                "AEACN": "DRUG PERMANENTLY WITHDRAWN",
            }
        ],
        "LB": [
            {
                "USUBJID": subject_id,
                "LBTEST": "Serum Lipase",
                "LBTESTCD": "LIPASE",
                "LBORRES": "850",
                "LBORRESU": "U/L",
                "LBSTNRLO": "10",
                "LBSTNRHI": "140",
                "LBNRIND": "HIGH",
                "LBDTC": "2026-06-20T08:30:00",
                "LBSEQ": 1,
            },
            {
                "USUBJID": subject_id,
                "LBTEST": "Serum Amylase",
                "LBTESTCD": "AMYLASE",
                "LBORRES": "420",
                "LBORRESU": "U/L",
                "LBSTNRLO": "30",
                "LBSTNRHI": "110",
                "LBNRIND": "HIGH",
                "LBDTC": "2026-06-20T08:30:00",
                "LBSEQ": 2,
            },
        ],
    }


# =========================================================================
# 1. Timeline Aggregator Unit Tests
# =========================================================================


def test_timeline_aggregator_chronological_ordering() -> None:
    """Validate chronological event aggregation across SDTM domains.

    @req:PRD-SYS-052
    """
    bundle = get_sample_sdtm_bundle("SUBJ-101")
    timeline = build_timeline_from_sdtm_records(
        study_id="STUDY-ONCO-2026",
        subject_id="SUBJ-101",
        sdtm_bundle=bundle,
        target_sae_key="SUBJ-101:SEQ-1",
    )

    assert timeline.study_id == "STUDY-ONCO-2026"
    assert timeline.subject_id == "SUBJ-101"
    assert len(timeline.events) >= 8

    # Verify event types present
    event_types = {e.event_type for e in timeline.events}
    assert TimelineEventType.DEMOGRAPHICS in event_types
    assert TimelineEventType.MEDICAL_HISTORY in event_types
    assert TimelineEventType.CONCOMITANT_MEDICATION in event_types
    assert TimelineEventType.DRUG_ADMINISTRATION in event_types
    assert TimelineEventType.ADVERSE_EVENT in event_types
    assert TimelineEventType.HOSPITALIZATION in event_types
    assert TimelineEventType.DECHALLENGE_RECHALLENGE in event_types
    assert TimelineEventType.DIAGNOSTIC_LAB in event_types

    # Verify timestamps are sorted chronologically
    dates = [e.event_date for e in timeline.events if e.event_date]
    assert dates == sorted(dates)


# =========================================================================
# 2. Service-Level Lifecycle & Part 11 Electronic Signature Tests
# =========================================================================


@pytest.mark.asyncio
async def test_narrative_service_generation_and_draft_state() -> None:
    """Validate safety narrative generation creates DRAFT_AI state with dual-attribution metadata.

    @req:PRD-SYS-051, PRD-SYS-052
    """
    service = SafetyNarrativeService()
    bundle = get_sample_sdtm_bundle("SUBJ-202")

    class MockExecutionClient:
        async def fetch_sdtm_domain(
            self, study_id: str, domain: str, client: Any = None
        ):
            return {domain: bundle.get(domain, [])}

    service.execution_client = MockExecutionClient()

    async with db_manager.get_session_maker()() as session:
        narrative = await service.generate_narrative(
            session=session,
            study_id="STUDY-ONCO-2026",
            subject_id="SUBJ-202",
            sae_event_key="SUBJ-202:SEQ-1",
            created_by="safety_officer_01",
            reason_for_change="Author initial SAE safety narrative",
        )

        assert narrative.id is not None
        assert narrative.study_id == "STUDY-ONCO-2026"
        assert narrative.subject_id == "SUBJ-202"
        assert narrative.review_status == AIReviewStatus.DRAFT_AI
        assert narrative.model_identifier is not None
        assert narrative.prompt_hash is not None
        assert narrative.confidence_score >= 0.90
        assert len(narrative.sections) == 6

        # Check section types match ICH E2B(R3)
        expected_sections = [
            NarrativeSectionType.DEMOGRAPHICS_BASELINE,
            NarrativeSectionType.MEDICAL_TREATMENT_HISTORY,
            NarrativeSectionType.INDEX_AE_CHRONOLOGY,
            NarrativeSectionType.DIAGNOSTIC_LABS,
            NarrativeSectionType.CLINICAL_MANAGEMENT,
            NarrativeSectionType.OUTCOME_CAUSALITY,
        ]
        actual_sections = [s.section_type for s in narrative.sections]
        assert actual_sections == expected_sections

        # Verify claims have grounded event IDs
        for s in narrative.sections:
            for claim in s.grounded_claims:
                assert len(claim.grounded_event_ids) > 0
                assert claim.sentence_text != ""

        # Verify audit log recorded
        stmt_audit = select(SafetyAuditLog).where(
            SafetyAuditLog.action == "SAFETY_NARRATIVE_GENERATED"
        )
        res_audit = await session.execute(stmt_audit)
        audits = res_audit.scalars().all()
        assert len(audits) == 1
        assert audits[0].record_id == narrative.id


@pytest.mark.asyncio
async def test_narrative_service_esignature_and_approval_workflow() -> None:
    """Validate 21 CFR Part 11 cryptographic e-signature sign-off workflow.

    @req:PRD-SYS-051, PRD-SYS-052
    """
    service = SafetyNarrativeService()
    bundle = get_sample_sdtm_bundle("SUBJ-303")

    class MockExecutionClient:
        async def fetch_sdtm_domain(
            self, study_id: str, domain: str, client: Any = None
        ):
            return {domain: bundle.get(domain, [])}

    service.execution_client = MockExecutionClient()

    async with db_manager.get_session_maker()() as session:
        draft = await service.generate_narrative(
            session=session,
            study_id="STUDY-ONCO-2026",
            subject_id="SUBJ-303",
            sae_event_key="SUBJ-303:SEQ-1",
            created_by="safety_officer_01",
            reason_for_change="Draft narrative",
        )

        # 1. Unauthorized role rejection
        with pytest.raises(HTTPException) as exc_info:
            await service.sign_narrative(
                session=session,
                narrative_id=draft.id,
                user_id="unauth_user",
                user_roles="site_crc",
                reason_for_change="Attempt unauthorized sign",
            )
        assert exc_info.value.status_code == 403

        # 2. Empty reason rejection
        with pytest.raises(HTTPException) as exc_info:
            await service.sign_narrative(
                session=session,
                narrative_id=draft.id,
                user_id="physician_01",
                user_roles="safety_physician",
                reason_for_change="",
            )
        assert exc_info.value.status_code == 400

        # 3. Successful Part 11 signing by Safety Physician
        sign_res = await service.sign_narrative(
            session=session,
            narrative_id=draft.id,
            user_id="dr_house",
            user_roles="safety_physician",
            reason_for_change="Medical Monitor Clinical Safety Sign-Off",
            signature_secret="dr-house-secret-key",
        )

        assert sign_res.review_status == AIReviewStatus.APPROVED
        assert sign_res.approved_by_user_id == "dr_house"
        assert sign_res.esignature_manifest_id.startswith("sig_man_")

        # 4. Verify updated state in database
        stmt = select(SafetyNarrative).where(SafetyNarrative.id == draft.id)
        res = await session.execute(stmt)
        updated = res.scalars().first()
        assert updated.review_status == "APPROVED"
        assert updated.approved_by_user_id == "dr_house"
        assert updated.approved_at is not None
        assert updated.version_index == 2

        # 5. Verify audit log
        stmt_audit = select(SafetyAuditLog).where(
            SafetyAuditLog.action == "SAFETY_NARRATIVE_SIGNED"
        )
        res_audit = await session.execute(stmt_audit)
        audits = res_audit.scalars().all()
        assert len(audits) == 1
        assert "dr_house" in audits[0].details


@pytest.mark.asyncio
async def test_narrative_service_export_gate_blocks_unapproved() -> None:
    """Validate ICH E2B(R3) export gate blocks unapproved AI narrative drafts.

    @req:PRD-SYS-052
    """
    service = SafetyNarrativeService()
    bundle = get_sample_sdtm_bundle("SUBJ-404")

    class MockExecutionClient:
        async def fetch_sdtm_domain(
            self, study_id: str, domain: str, client: Any = None
        ):
            return {domain: bundle.get(domain, [])}

    service.execution_client = MockExecutionClient()

    async with db_manager.get_session_maker()() as session:
        draft = await service.generate_narrative(
            session=session,
            study_id="STUDY-ONCO-2026",
            subject_id="SUBJ-404",
            sae_event_key="SUBJ-404:SEQ-1",
            created_by="safety_officer_01",
            reason_for_change="Draft narrative",
        )

        # 1. Attempting export in DRAFT_AI state must fail with HTTP 412
        with pytest.raises(HTTPException) as exc_info:
            await service.export_narrative_to_e2b_xml(
                session=session,
                narrative_id=draft.id,
                user_id="safety_officer_01",
                reason_for_change="Premature export attempt",
            )
        assert exc_info.value.status_code == 412
        assert "Cannot export unapproved AI narrative" in exc_info.value.detail

        # 2. Sign document
        await service.sign_narrative(
            session=session,
            narrative_id=draft.id,
            user_id="dr_cameron",
            user_roles="sponsor_medical_monitor",
            reason_for_change="Approved for 15-Day Expedited Safety Reporting",
        )

        # 3. Export succeeds after approval
        xml_output = await service.export_narrative_to_e2b_xml(
            session=session,
            narrative_id=draft.id,
            user_id="safety_officer_01",
            reason_for_change="Expedited E2B(R3) XML export",
        )
        assert "<?xml version=" in xml_output
        assert "<narrativeincludeclinicalcourse>" in xml_output
        assert "<safetyreportid>" in xml_output


# =========================================================================
# 3. REST API Endpoint Integration Tests
# =========================================================================


@pytest.mark.asyncio
async def test_rest_api_narrative_endpoints_end_to_end() -> None:
    """Full roundtrip REST API integration test for generation, viewing, signing, and E2B export.

    @req:PRD-SYS-051, PRD-SYS-052
    """
    bundle = get_sample_sdtm_bundle("SUBJ-505")

    class MockAsyncClient:
        async def get(
            self,
            url: str,
            headers: Any = None,
            params: Any = None,
            timeout: float = 10.0,
        ):
            for dom in ["DM", "MH", "CM", "AE", "LB", "VS", "EX"]:
                if f"sdtm/{dom}" in url:
                    mock_data = {
                        "clinicalData": {
                            "studyOID": "STUDY-ONCO-2026",
                            "itemGroupData": {
                                f"IG.{dom}": {
                                    "items": [
                                        {"name": k} for k in bundle.get(dom, [{}])[0]
                                    ]
                                    if bundle.get(dom)
                                    else [],
                                    "itemData": [
                                        [v for v in row.values()]
                                        for row in bundle.get(dom, [])
                                    ],
                                }
                            },
                        }
                    }
                    return httpx.Response(status_code=200, json=mock_data)
            return httpx.Response(status_code=404)

        async def post(
            self, url: str, json: Any = None, headers: Any = None, timeout: float = 15.0
        ):
            # AI Gateway generation mock
            if "ai/generate" in url:
                mock_gen = {
                    "model": "cadence-frontier-reasoner-v1",
                    "tier": "tier_3_frontier",
                    "structured_data": {
                        "sections": [
                            {
                                "section_type": "DEMOGRAPHICS_BASELINE",
                                "section_title": "Patient Demographics & Baseline Condition",
                                "content": "Subject SUBJ-505 is a 64-year-old male enrolled in study STUDY-ONCO-2026.",
                                "grounded_claims": [
                                    {
                                        "claim_id": "CLM-DEM-01",
                                        "sentence_text": "Subject SUBJ-505 is a 64-year-old male enrolled in study STUDY-ONCO-2026.",
                                        "grounded_event_ids": ["EVT-DM-01"],
                                        "confidence_score": 0.99,
                                    }
                                ],
                            },
                            {
                                "section_type": "MEDICAL_TREATMENT_HISTORY",
                                "section_title": "Medical & Treatment History",
                                "content": "Past medical history includes Type 2 Diabetes and Hypertension.",
                                "grounded_claims": [
                                    {
                                        "claim_id": "CLM-MH-01",
                                        "sentence_text": "Past medical history includes Type 2 Diabetes.",
                                        "grounded_event_ids": ["EVT-MH-01"],
                                        "confidence_score": 0.98,
                                    }
                                ],
                            },
                            {
                                "section_type": "INDEX_AE_CHRONOLOGY",
                                "section_title": "Index Adverse Event Description & Chronology",
                                "content": "On 2026-06-20, the subject experienced Severe Acute Pancreatitis.",
                                "grounded_claims": [
                                    {
                                        "claim_id": "CLM-IND-01",
                                        "sentence_text": "On 2026-06-20, the subject experienced Severe Acute Pancreatitis.",
                                        "grounded_event_ids": ["EVT-AE-01"],
                                        "confidence_score": 0.98,
                                    }
                                ],
                            },
                            {
                                "section_type": "DIAGNOSTIC_LABS",
                                "section_title": "Diagnostic Workup & Laboratory Results",
                                "content": "Lipase was elevated at 850 U/L.",
                                "grounded_claims": [
                                    {
                                        "claim_id": "CLM-LAB-01",
                                        "sentence_text": "Lipase was elevated at 850 U/L.",
                                        "grounded_event_ids": ["EVT-LB-01"],
                                        "confidence_score": 0.97,
                                    }
                                ],
                            },
                            {
                                "section_type": "CLINICAL_MANAGEMENT",
                                "section_title": "Clinical Management & Hospital Course",
                                "content": "Subject was hospitalized and study drug was permanently withdrawn.",
                                "grounded_claims": [
                                    {
                                        "claim_id": "CLM-MGT-01",
                                        "sentence_text": "Subject was hospitalized.",
                                        "grounded_event_ids": ["EVT-HOSP-01"],
                                        "confidence_score": 0.95,
                                    }
                                ],
                            },
                            {
                                "section_type": "OUTCOME_CAUSALITY",
                                "section_title": "Outcome & Causality Assessment",
                                "content": "The event resolved on 2026-06-28. Relationship assessed as probable.",
                                "grounded_claims": [
                                    {
                                        "claim_id": "CLM-CAU-01",
                                        "sentence_text": "The event resolved on 2026-06-28.",
                                        "grounded_event_ids": ["EVT-AE-01"],
                                        "confidence_score": 0.96,
                                    }
                                ],
                            },
                        ]
                    },
                }
                return httpx.Response(status_code=200, json=mock_gen)
            return httpx.Response(status_code=404)

    app.state.test_httpx_client = MockAsyncClient()
    client = TestClient(app)

    headers = get_signed_headers(
        roles="sponsor_medical_monitor",
        user_id="dr_cuddy",
        change_reason="Perform REST safety narrative test",
    )

    # 1. POST /api/v1/safety/narratives/generate
    gen_payload = {
        "study_id": "STUDY-ONCO-2026",
        "subject_id": "SUBJ-505",
        "sae_event_key": "SUBJ-505:SEQ-1",
        "reason_for_change": "Generate initial regulatory SAE narrative draft",
    }
    res_gen = client.post(
        "/api/v1/safety/narratives/generate", json=gen_payload, headers=headers
    )
    assert res_gen.status_code == 201
    narr_data = res_gen.json()
    narr_id = narr_data["id"]
    assert narr_data["review_status"] == "DRAFT_AI"
    assert len(narr_data["sections"]) == 6

    # 2. GET /api/v1/safety/narratives/{id}
    res_get = client.get(f"/api/v1/safety/narratives/{narr_id}", headers=headers)
    assert res_get.status_code == 200
    assert res_get.json()["id"] == narr_id

    # 3. GET /api/v1/safety/narratives (List with filter)
    res_list = client.get(
        "/api/v1/safety/narratives?study_id=STUDY-ONCO-2026", headers=headers
    )
    assert res_list.status_code == 200
    assert len(res_list.json()) >= 1

    # 4. POST /api/v1/safety/narratives/{id}/export-e2b (Blocked while unapproved)
    res_exp_blocked = client.post(
        f"/api/v1/safety/narratives/{narr_id}/export-e2b", headers=headers
    )
    assert res_exp_blocked.status_code == 412

    # 5. POST /api/v1/safety/narratives/{id}/sign (Part 11 Electronic Signature)
    sign_headers = get_signed_headers(
        roles="sponsor_medical_monitor",
        user_id="dr_cuddy",
        change_reason="Safety Physician final review and sign-off for FDA filing",
        sig_token=make_sig_token(
            "dr_cuddy", f"/api/v1/safety/narratives/{narr_id}/sign"
        ),
    )
    sign_payload = {
        "narrative_id": narr_id,
        "reason_for_change": "Safety Physician final review and sign-off for FDA filing",
    }
    res_sign = client.post(
        f"/api/v1/safety/narratives/{narr_id}/sign",
        json=sign_payload,
        headers=sign_headers,
    )
    assert res_sign.status_code == 200
    assert res_sign.json()["review_status"] == "APPROVED"

    # 6. POST /api/v1/safety/narratives/{id}/export-e2b (Allowed after approval)
    res_exp_ok = client.post(
        f"/api/v1/safety/narratives/{narr_id}/export-e2b", headers=headers
    )
    assert res_exp_ok.status_code == 200
    assert "application/xml" in res_exp_ok.headers["content-type"]
    assert "<narrativeincludeclinicalcourse>" in res_exp_ok.text
