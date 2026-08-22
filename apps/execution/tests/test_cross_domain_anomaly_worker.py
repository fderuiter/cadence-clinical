"""Unit, integration, and lifecycle tests for asynchronous cross-domain anomaly detection worker.

Requirements: PRD-QRY-008, PRD-SYS-001, PRD-SYS-051
"""

import hashlib
import hmac
import json
import time
from collections.abc import AsyncGenerator
from datetime import UTC, datetime
from unittest.mock import patch

import httpx
import pytest
import pytest_asyncio
from sqlalchemy import select

from apps.execution.adapters.ai_anomaly_client import AIAnomalyGatewayClient
from apps.execution.database.core import db_manager
from apps.execution.database.models import (
    AuditLog,
    Base,
    ClinicalObservation,
    ClinicalQuery,
    ClinicalSubject,
)
from apps.execution.domain.anomaly import (
    AnomalySeverity,
    CrossDomainAnomalyType,
)
from apps.execution.main import app
from apps.execution.services.cross_domain_anomaly_service import (
    CrossDomainAnomalyService,
)
from apps.execution.workers.anomaly_worker import (
    poll_and_evaluate_anomalies,
    run_asynchronous_subject_anomaly_checks,
    start_anomaly_worker,
    stop_anomaly_worker,
)

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


def get_v2_auth_headers(
    user_id: str = "data_manager_user",
    roles: str = "data_manager",
    change_reason: str = "cross domain anomaly testing",
) -> dict[str, str]:
    """Generate Gateway signature version 2 authentication headers."""
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


@pytest_asyncio.fixture(autouse=True)
async def setup_test_db() -> AsyncGenerator[None]:
    """Setup in-memory SQLite database for test execution."""
    db_manager.init_db("sqlite+aiosqlite:///:memory:")
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    yield

    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


@pytest.mark.asyncio
async def test_ae_without_concomitant_medication_detection() -> None:
    """Validate detection of severe adverse events lacking concomitant medications.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)

        ae_obs = ClinicalObservation(
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Severe Hepatotoxicity",
            additional_properties={
                "AETERM": "Severe Hepatotoxicity",
                "AESEV": "SEVERE",
                "AESER": "Y",
            },
            observation_date=datetime.now(UTC),
        )
        session.add(ae_obs)
        await session.commit()

        service = CrossDomainAnomalyService(ai_client=None)
        res = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-101",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()

        assert len(res.anomalies) >= 1
        ae_anomaly = next(
            a
            for a in res.anomalies
            if a.anomaly_type == CrossDomainAnomalyType.AE_WITHOUT_CONCOMITANT_MED
        )
        assert ae_anomaly.severity == AnomalySeverity.HIGH
        assert ae_anomaly.primary_domain == "AE"
        assert ae_anomaly.correlated_domain == "CM"
        assert res.queries_staged_count == 1

        # Verify staged CANDIDATE query in database
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.subject_id == "SUBJ-101",
            ClinicalQuery.status == "CANDIDATE",
        )
        q_res = await session.execute(stmt)
        candidate_query = q_res.scalar_one()
        assert candidate_query.origin == "ANOMALY_DETECTOR"
        assert candidate_query.query_type == "CROSS_DOMAIN_ANOMALY"
        assert "Hepatotoxicity" in candidate_query.message


@pytest.mark.asyncio
async def test_concomitant_medication_without_ae_detection() -> None:
    """Validate detection of acute concomitant medications without matching AE or MH records.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-102",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)

        cm_obs = ClinicalObservation(
            subject_id="SUBJ-102",
            study_id="STUDY-001",
            domain="CM",
            test_code="CMTRT",
            test_name="Concomitant Medication",
            value_string="Sumatriptan",
            additional_properties={
                "CMTRT": "Sumatriptan",
                "CMINDC": "Severe Acute Migraine Headache",
            },
            observation_date=datetime.now(UTC),
        )
        session.add(cm_obs)
        await session.commit()

        service = CrossDomainAnomalyService(ai_client=None)
        res = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-102",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()

        assert len(res.anomalies) == 1
        anomaly = res.anomalies[0]
        assert anomaly.anomaly_type == CrossDomainAnomalyType.CONCOMITANT_MED_WITHOUT_AE
        assert anomaly.primary_domain == "CM"
        assert anomaly.correlated_domain == "AE"


@pytest.mark.asyncio
async def test_marked_lab_abnormality_without_ae() -> None:
    """Validate detection of marked laboratory transaminitis without reported AE.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-103",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)

        # Critical ALT elevation (320 U/L)
        lb_obs = ClinicalObservation(
            subject_id="SUBJ-103",
            study_id="STUDY-001",
            domain="LB",
            test_code="ALT",
            test_name="Alanine Aminotransferase",
            value=320.0,
            unit="U/L",
            lab_indicator="CRITICAL_HIGH",
            observation_date=datetime.now(UTC),
        )
        session.add(lb_obs)
        await session.commit()

        service = CrossDomainAnomalyService(ai_client=None)
        res = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-103",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()

        assert len(res.anomalies) == 1
        anomaly = res.anomalies[0]
        assert (
            anomaly.anomaly_type
            == CrossDomainAnomalyType.MARKED_LAB_ABNORMALITY_WITHOUT_AE
        )
        assert anomaly.severity == AnomalySeverity.HIGH
        assert anomaly.primary_domain == "LB"


@pytest.mark.asyncio
async def test_critical_vital_signs_without_ae() -> None:
    """Validate detection of critical vital signs (hypertensive crisis) without documented AE.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-104",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)

        vs_obs = ClinicalObservation(
            subject_id="SUBJ-104",
            study_id="STUDY-001",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=195.0,
            unit="mmHg",
            observation_date=datetime.now(UTC),
        )
        session.add(vs_obs)
        await session.commit()

        service = CrossDomainAnomalyService(ai_client=None)
        res = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-104",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()

        assert len(res.anomalies) == 1
        anomaly = res.anomalies[0]
        assert anomaly.anomaly_type == CrossDomainAnomalyType.CRITICAL_VITALS_WITHOUT_AE
        assert anomaly.severity == AnomalySeverity.HIGH


@pytest.mark.asyncio
async def test_drug_discontinuation_without_ae() -> None:
    """Validate detection of study drug discontinuation attributed to AE without AE records.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-105",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)

        ds_obs = ClinicalObservation(
            subject_id="SUBJ-105",
            study_id="STUDY-001",
            domain="DS",
            test_code="DSTERM",
            test_name="Disposition Term",
            value_string="Adverse Event",
            additional_properties={
                "DSTERM": "Adverse Event",
                "DSDECOD": "ADVERSE EVENT",
            },
            observation_date=datetime.now(UTC),
        )
        session.add(ds_obs)
        await session.commit()

        service = CrossDomainAnomalyService(ai_client=None)
        res = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-105",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()

        assert len(res.anomalies) == 1
        anomaly = res.anomalies[0]
        assert (
            anomaly.anomaly_type
            == CrossDomainAnomalyType.DRUG_DISCONTINUATION_WITHOUT_AE
        )
        assert anomaly.primary_domain == "DS"


@pytest.mark.asyncio
async def test_temporal_sequence_mismatch_detection() -> None:
    """Validate detection of temporal sequence inversion where CM ends prior to AE onset.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-106",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)

        ae_obs = ClinicalObservation(
            subject_id="SUBJ-106",
            study_id="STUDY-001",
            domain="AE",
            test_code="AETERM",
            test_name="Adverse Event Term",
            value_string="Hypertensive Urgency",
            additional_properties={
                "AETERM": "Hypertensive Urgency",
                "AESTDTC": "2026-05-10T00:00:00Z",
                "AESEV": "MODERATE",
            },
            observation_date=datetime(2026, 5, 10, tzinfo=UTC),
        )
        cm_obs = ClinicalObservation(
            subject_id="SUBJ-106",
            study_id="STUDY-001",
            domain="CM",
            test_code="CMTRT",
            test_name="Concomitant Medication",
            value_string="Labetalol",
            additional_properties={
                "CMTRT": "Labetalol",
                "CMINDC": "Treatment for Hypertensive Urgency",
                "CMSTDTC": "2026-04-20T00:00:00Z",
                "CMENDTC": "2026-05-01T00:00:00Z",
            },
            observation_date=datetime(2026, 4, 20, tzinfo=UTC),
        )
        session.add_all([ae_obs, cm_obs])
        await session.commit()

        service = CrossDomainAnomalyService(ai_client=None)
        res = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-106",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()

        assert any(
            a.anomaly_type == CrossDomainAnomalyType.TEMPORAL_SEQUENCE_MISMATCH
            for a in res.anomalies
        )


@pytest.mark.asyncio
async def test_candidate_query_deduplication() -> None:
    """Validate that re-evaluating cross-domain anomalies does not create duplicate candidate queries.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-107",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)
        vs_obs = ClinicalObservation(
            subject_id="SUBJ-107",
            study_id="STUDY-001",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=200.0,
            observation_date=datetime.now(UTC),
        )
        session.add(vs_obs)
        await session.commit()

        service = CrossDomainAnomalyService(ai_client=None)

        # First evaluation
        res1 = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-107",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()
        assert res1.queries_staged_count == 1

        # Second evaluation
        res2 = await service.evaluate_subject_cross_domain_anomalies(
            session=session,
            subject_id="SUBJ-107",
            study_id="STUDY-001",
            enable_ai=False,
            auto_stage_queries=True,
        )
        await session.commit()
        assert res2.queries_staged_count == 0

        # Query total count
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.subject_id == "SUBJ-107",
            ClinicalQuery.status == "CANDIDATE",
        )
        q_res = await session.execute(stmt)
        assert len(q_res.scalars().all()) == 1


@pytest.mark.asyncio
async def test_ai_gateway_client_integration_and_fallback() -> None:
    """Validate AI Gateway client invocation with structured schema and fallback on error.

    @req:PRD-QRY-008
    """
    client = AIAnomalyGatewayClient(base_url="http://ai-gateway.mock:8000")

    mock_ai_response = {
        "model": "gpt-4o-mini-tier-2",
        "structured_data": {
            "anomalies": [
                {
                    "anomaly_type": "AI_CONTEXTUAL_INCONSISTENCY",
                    "primary_domain": "AE",
                    "primary_test_code": "AETERM",
                    "correlated_domain": "CM",
                    "correlated_test_code": "CMTRT",
                    "severity": "HIGH",
                    "message": "AI-detected contradiction between reported rash and antihistamine dosage.",
                    "explanation": "Subject reported severe rash but antihistamine was documented as discontinued.",
                    "confidence_score": 0.92,
                }
            ]
        },
    }

    mock_success_resp = httpx.Response(
        status_code=200,
        json=mock_ai_response,
        request=httpx.Request("POST", "http://ai-gateway.mock:8000/api/v1/ai/generate"),
    )

    with patch.object(httpx.AsyncClient, "post", return_value=mock_success_resp):
        results = await client.analyze_cross_domain_consistency(
            subject_id="SUBJ-108",
            study_id="STUDY-001",
            events_summary="Subject observations summary text...",
        )
        assert len(results) == 1
        assert (
            results[0].anomaly_type
            == CrossDomainAnomalyType.AI_CONTEXTUAL_INCONSISTENCY
        )
        assert results[0].model_identifier == "gpt-4o-mini-tier-2"
        assert results[0].confidence_score == 0.92
        assert results[0].prompt_hash is not None

    mock_err_resp = httpx.Response(
        status_code=500,
        text="Internal Server Error",
        request=httpx.Request("POST", "http://ai-gateway.mock:8000/api/v1/ai/generate"),
    )

    with patch.object(httpx.AsyncClient, "post", return_value=mock_err_resp):
        results_err = await client.analyze_cross_domain_consistency(
            subject_id="SUBJ-108",
            study_id="STUDY-001",
            events_summary="Subject observations summary text...",
        )
        assert results_err == []


@pytest.mark.asyncio
async def test_rest_api_anomaly_evaluate_and_candidate_adjudication() -> None:
    """Validate REST endpoints for anomaly evaluation, candidate listing, and Data Manager adjudication.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-109",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)
        vs_obs = ClinicalObservation(
            subject_id="SUBJ-109",
            study_id="STUDY-001",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=210.0,
            observation_date=datetime.now(UTC),
        )
        session.add(vs_obs)
        await session.commit()

    headers = get_v2_auth_headers(
        user_id="dm_user",
        roles="data_manager",
        change_reason="Evaluate and adjudicate candidate anomaly",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        # 1. Trigger Evaluate endpoint
        eval_resp = await client.post(
            "/api/v1/execution/anomalies/evaluate",
            headers=headers,
            json={
                "subject_id": "SUBJ-109",
                "study_id": "STUDY-001",
                "enable_ai": False,
                "auto_stage_queries": True,
            },
        )
        assert eval_resp.status_code == 200
        eval_data = eval_resp.json()
        assert eval_data["queries_staged_count"] == 1

        # 2. List candidate queries
        list_resp = await client.get(
            "/api/v1/execution/anomalies/candidates?study_id=STUDY-001&subject_id=SUBJ-109",
            headers=headers,
        )
        assert list_resp.status_code == 200
        candidates = list_resp.json()
        assert len(candidates) == 1
        query_id = candidates[0]["query_id"]
        assert candidates[0]["status"] == "CANDIDATE"

        # 3. Adjudicate - APPROVE (promote to OPEN)
        adj_resp = await client.post(
            f"/api/v1/execution/anomalies/candidates/{query_id}/adjudicate",
            headers=headers,
            json={
                "action": "APPROVE",
                "reason": "Confirmed clinical discrepancy across VS and AE domains.",
                "updated_message": "Critical BP observed. Please verify if hypertensive AE occurred.",
            },
        )
        assert adj_resp.status_code == 200
        adj_data = adj_resp.json()
        assert adj_data["new_status"] == "OPEN"

    # Verify query status in DB and audit log
    async with session_maker() as session:
        stmt = select(ClinicalQuery).where(ClinicalQuery.id == query_id)
        res = await session.execute(stmt)
        q = res.scalar_one()
        assert q.status == "OPEN"
        assert (
            q.message
            == "Critical BP observed. Please verify if hypertensive AE occurred."
        )

        # Verify Part 11 AuditLog entry
        audit_stmt = select(AuditLog).where(
            AuditLog.table_name == "clinical_queries",
            AuditLog.record_id == query_id,
            AuditLog.action == "UPDATE",
        )
        audit_res = await session.execute(audit_stmt)
        audits = audit_res.scalars().all()
        assert len(audits) >= 1
        assert "Confirmed clinical discrepancy" in audits[-1].change_reason


@pytest.mark.asyncio
async def test_rest_api_candidate_rejection() -> None:
    """Validate Data Manager candidate query rejection/dismissal to CANCELLED status.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        q = ClinicalQuery(
            study_id="STUDY-001",
            site_id="SITE-01",
            subject_id="SUBJ-110",
            domain="AE",
            test_code="AETERM",
            status="CANDIDATE",
            origin="ANOMALY_DETECTOR",
            rule_id="ANOMALY_AE_WITHOUT_CONCOMITANT_MED",
            message="Test candidate query",
            created_by="ANOMALY_DETECTOR_WORKER",
        )
        session.add(q)
        await session.commit()
        query_id = q.id

    headers = get_v2_auth_headers(
        user_id="dm_user",
        roles="data_manager",
        change_reason="Dismiss candidate false positive",
    )

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=app), base_url="http://test"
    ) as client:
        adj_resp = await client.post(
            f"/api/v1/execution/anomalies/candidates/{query_id}/adjudicate",
            headers=headers,
            json={
                "action": "REJECT",
                "reason": "Investigator noted AE was self-limiting and did not warrant medication.",
            },
        )
        assert adj_resp.status_code == 200
        adj_data = adj_resp.json()
        assert adj_data["new_status"] == "CANCELLED"

    async with session_maker() as session:
        stmt = select(ClinicalQuery).where(ClinicalQuery.id == query_id)
        res = await session.execute(stmt)
        updated_q = res.scalar_one()
        assert updated_q.status == "CANCELLED"
        assert "self-limiting" in (updated_q.cancellation_reason or "")


@pytest.mark.asyncio
async def test_anomaly_worker_lifecycle_and_background_triggers() -> None:
    """Validate worker start/stop lifecycle and background sweep execution.

    @req:PRD-QRY-008
    """
    session_maker = db_manager.get_session_maker()
    async with session_maker() as session:
        subj = ClinicalSubject(
            subject_id="SUBJ-111",
            study_id="STUDY-001",
            site_id="SITE-01",
            status="SCREENING",
        )
        session.add(subj)
        vs_obs = ClinicalObservation(
            subject_id="SUBJ-111",
            study_id="STUDY-001",
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=205.0,
            observation_date=datetime.now(UTC),
        )
        session.add(vs_obs)
        await session.commit()

    # 1. Run poll_and_evaluate_anomalies
    await poll_and_evaluate_anomalies(session_maker)

    async with session_maker() as session:
        stmt = select(ClinicalQuery).where(
            ClinicalQuery.subject_id == "SUBJ-111",
            ClinicalQuery.status == "CANDIDATE",
        )
        res = await session.execute(stmt)
        queries = res.scalars().all()
        assert len(queries) == 1

    # 2. Run immediate post-submission hook
    await run_asynchronous_subject_anomaly_checks(
        session_factory=session_maker,
        subject_id="SUBJ-111",
        study_id="STUDY-001",
    )

    # 3. Test worker start and stop functions
    start_anomaly_worker(session_maker)
    stop_anomaly_worker()
