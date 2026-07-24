import time
from datetime import datetime

import httpx
import pytest
import pytest_asyncio
from fastapi import HTTPException
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import selectinload

from apps.gateway.main import generate_signature
from apps.quality.database import db_manager
from apps.quality.main import (
    app,
    create_deviation,
    create_or_update_rca,
    create_capa,
    transition_capa,
    update_capa,
    write_audit_log,
)
from apps.quality.models import (
    Base,
    CAPARecord,
    CAPAStatus,
    Deviation,
    DeviationSeverity,
    DeviationStatus,
    DeviationType,
    QualityAuditLog,
    RootCauseAnalysis,
)


@pytest_asyncio.fixture(autouse=True)
async def setup_quality_db():
    """
    Setup in-memory Quality database for unit and integration testing.
    """
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    if db_manager.engine is not None:
        try:
            async with db_manager.engine.begin() as conn:
                await conn.run_sync(Base.metadata.drop_all)
        except Exception:
            pass
        try:
            await db_manager.close()
        except Exception:
            pass


def get_auth_headers(
    roles: str = "admin", change_reason: str = "Compliance change justification"
) -> dict:
    """
    Helper to generate valid gateway V2 signed headers for testing.
    """
    timestamp = str(time.time())
    user_id = "quality_test_user"
    sig = generate_signature(
        user_id, roles, timestamp, version="2", change_reason=change_reason
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": sig,
        "X-Signature-Version": "2",
    }
    if change_reason:
        headers["X-Change-Reason"] = change_reason
    return headers


class MockRequest:
    """
    Mock request object for direct route function invocation to bypass middleware.
    """
    def __init__(self, state_dict=None, headers_dict=None):
        class State:
            pass
        self.state = State()
        if state_dict is not None:
            for k, v in state_dict.items():
                setattr(self.state, k, v)
        self.headers = headers_dict or {}


# ==========================================
# 1. Basic / Initialization Tests
# ==========================================

def test_quality_lifespan_via_testclient():
    """
    Verify that the FastAPI lifespan startup/shutdown executes.
    """
    from fastapi.testclient import TestClient
    with TestClient(app) as client:
        res = client.get("/health")
        assert res.status_code == 200
        assert res.json()["service"] == "quality"
    # Re-initialize after lifespan closes it, so other tests are not affected
    db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)


@pytest.mark.asyncio
async def test_quality_health_check():
    """
    Verify health check of independent Quality & CAPA service.
    """
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        response = await client.get("/health")
        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "ok"
        assert data["service"] == "quality"


@pytest.mark.asyncio
async def test_quality_database_schema_creation():
    """
    Verify that all four quality tables can be created and queried successfully.
    """
    async with db_manager.get_session_maker()() as session:
        deviations = await session.execute(select(Deviation))
        rcas = await session.execute(select(RootCauseAnalysis))
        capas = await session.execute(select(CAPARecord))
        logs = await session.execute(select(QualityAuditLog))

        assert deviations.scalars().all() == []
        assert rcas.scalars().all() == []
        assert capas.scalars().all() == []
        assert logs.scalars().all() == []


@pytest.mark.asyncio
async def test_database_manager_uninitialized_raises_exception():
    """
    Verify that QualityDatabaseManager raises an exception if get_session_maker is called before init_db.
    """
    from apps.quality.database import QualityDatabaseManager

    uninit_manager = QualityDatabaseManager()
    with pytest.raises(Exception) as exc_info:
        uninit_manager.get_session_maker()
    assert "not initialized" in str(exc_info.value)


@pytest.mark.asyncio
async def test_sqlite_foreign_key_constraints():
    """
    Ensure that SQLite foreign key constraint is enforced and throws IntegrityError on invalid ForeignKey.
    """
    async with db_manager.get_session_maker()() as session:
        rca = RootCauseAnalysis(
            deviation_id="non-existent-deviation-id",
            methodology="5 Whys",
            investigation_details="No investigation",
            root_cause_summary="None",
            study_id="study_001",
            created_by="test",
            version_index=1,
            reason_for_change="Invalid RCA",
        )
        session.add(rca)
        with pytest.raises(IntegrityError):
            await session.commit()


# ==========================================
# 2. Deviation / Protocol-Violation Tests
# ==========================================

@pytest.mark.asyncio
async def test_deviation_lifecycle_and_traceability_fields():
    """
    Verify that a Deviation can be persisted with string-backed enums and all mandatory traceability fields.
    """
    async with db_manager.get_session_maker()() as session:
        dev = Deviation(
            study_id="study_999",
            site_id="site_111",
            title="Informed Consent Missing Signature Date",
            description="Subject 002 signed consent but did not date it.",
            severity=DeviationSeverity.MAJOR,
            status=DeviationStatus.REPORTED,
            type=DeviationType.INFORMED_CONSENT,
            is_protocol_violation=True,
            created_by="auditor_jane",
            version_index=1,
            reason_for_change="Initial reporting of informed consent deviation.",
        )
        session.add(dev)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        stmt = select(Deviation).where(Deviation.study_id == "study_999")
        result = await session.execute(stmt)
        retrieved = result.scalar_one()

        assert retrieved.id is not None
        assert retrieved.site_id == "site_111"
        assert retrieved.title == "Informed Consent Missing Signature Date"
        assert retrieved.severity == DeviationSeverity.MAJOR
        assert retrieved.status == DeviationStatus.REPORTED
        assert retrieved.type == DeviationType.INFORMED_CONSENT
        assert retrieved.is_protocol_violation is True
        assert retrieved.created_by == "auditor_jane"
        assert retrieved.version_index == 1
        assert (
            retrieved.reason_for_change
            == "Initial reporting of informed consent deviation."
        )
        assert isinstance(retrieved.created_at, datetime)


@pytest.mark.asyncio
async def test_create_and_list_deviations_api():
    """
    Verify that a deviation can be created and retrieved via API.
    """
    headers = get_auth_headers(change_reason="Reporting protocol deviation")
    payload = {
        "study_id": "study_123",
        "site_id": "site_abc",
        "title": "Informed consent missing signature",
        "description": "The subject signed the form but did not date it.",
        "severity": "MAJOR",
        "type": "INFORMED_CONSENT",
        "is_protocol_violation": True,
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        response = await client.post("/api/v1/quality/deviations", json=payload, headers=headers)
        assert response.status_code == 201
        data = response.json()
        assert data["id"] is not None
        assert data["study_id"] == "study_123"
        assert data["site_id"] == "site_abc"
        assert data["title"] == "Informed consent missing signature"
        assert data["status"] == "REPORTED"
        assert data["version_index"] == 1
        assert data["reason_for_change"] == "Reporting protocol deviation"

        # List deviations with filters to trigger different conditional branches
        response_list = await client.get(
            "/api/v1/quality/deviations?study_id=study_123&site_id=site_abc&status=REPORTED",
            headers=headers,
        )
        assert response_list.status_code == 200
        list_data = response_list.json()
        assert len(list_data) == 1
        assert list_data[0]["id"] == data["id"]

        # View deviation happy path
        response_view = await client.get(
            f"/api/v1/quality/deviations/{data['id']}", headers=headers
        )
        assert response_view.status_code == 200
        assert response_view.json()["id"] == data["id"]

        # View deviation 404
        response_view_404 = await client.get(
            "/api/v1/quality/deviations/nonexistent-id", headers=headers
        )
        assert response_view_404.status_code == 404


# ==========================================
# 3. Root Cause Analysis (RCA) Tests
# ==========================================

@pytest.mark.asyncio
async def test_deviation_rca_capa_relationships_and_cascading():
    """
    Verify the relationships and Cascade constraints (SQLite foreign-key enforcement is enabled).
    - Deviation (1) -> (0..1) RootCauseAnalysis
    - Deviation (1) -> (0..N) CAPARecord
    - RootCauseAnalysis (1) -> (0..N) CAPARecord
    """
    async with db_manager.get_session_maker()() as session:
        # 1. Create Deviation
        dev = Deviation(
            study_id="study_999",
            site_id="site_111",
            title="Temperature Excursion",
            description="Investigational product stored at 15C instead of 2-8C.",
            severity=DeviationSeverity.CRITICAL,
            status=DeviationStatus.UNDER_INVESTIGATION,
            type=DeviationType.IP_MANAGEMENT,
            is_protocol_violation=False,
            created_by="cra_bob",
            version_index=1,
            reason_for_change="IP storage excursion.",
        )
        session.add(dev)
        await session.flush()

        # 2. Create RCA linked to Deviation
        rca = RootCauseAnalysis(
            deviation_id=dev.id,
            methodology="5 Whys",
            investigation_details="Power outage -> Backup generator failed to start -> Temp rose -> Monitored after 6 hours.",
            root_cause_summary="Generator maintenance oversight.",
            study_id="study_999",
            site_id="site_111",
            created_by="cra_bob",
            version_index=1,
            reason_for_change="RCA investigation complete.",
        )
        session.add(rca)
        await session.flush()

        # 3. Create CAPA Record linked to Deviation and RCA
        capa = CAPARecord(
            deviation_id=dev.id,
            rca_id=rca.id,
            capa_type="PREVENTIVE",
            action_plan="Perform weekly generator testing and sign-offs.",
            status=CAPAStatus.INITIATED,
            preventive_measures="Update SOP-QA-09 for facilities monitoring.",
            target_completion_date=datetime(2026, 12, 31),
            study_id="study_999",
            site_id="site_111",
            created_by="cra_bob",
            version_index=1,
            reason_for_change="Initiate preventive action plan.",
        )
        session.add(capa)
        await session.commit()

    # Verify relationships are loaded correctly using selectinload
    async with db_manager.get_session_maker()() as session:
        stmt = (
            select(Deviation)
            .where(Deviation.study_id == "study_999")
            .options(
                selectinload(Deviation.root_cause_analysis),
                selectinload(Deviation.capa_records).selectinload(CAPARecord.rca),
            )
        )
        result = await session.execute(stmt)
        retrieved_dev = result.scalar_one()

        assert retrieved_dev.root_cause_analysis is not None
        assert retrieved_dev.root_cause_analysis.methodology == "5 Whys"
        assert len(retrieved_dev.capa_records) == 1
        assert retrieved_dev.capa_records[0].capa_type == "PREVENTIVE"
        assert retrieved_dev.capa_records[0].rca is not None
        assert (
            retrieved_dev.capa_records[0].rca.id == retrieved_dev.root_cause_analysis.id
        )

    # Verify cascading deletes (on deleting Deviation, RCA and CAPAs are deleted)
    async with db_manager.get_session_maker()() as session:
        stmt = select(Deviation).where(Deviation.study_id == "study_999")
        result = await session.execute(stmt)
        retrieved_dev = result.scalar_one()

        await session.delete(retrieved_dev)
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        # Verify RCA and CAPA are deleted due to CASCADE
        deviations = await session.execute(select(Deviation))
        rcas = await session.execute(select(RootCauseAnalysis))
        capas = await session.execute(select(CAPARecord))

        assert len(deviations.scalars().all()) == 0
        assert len(rcas.scalars().all()) == 0
        assert len(capas.scalars().all()) == 0


@pytest.mark.asyncio
async def test_create_and_update_rca_api():
    """
    Verify that an RCA can be created or updated for an existing deviation.
    """
    headers = get_auth_headers(change_reason="Create deviation for RCA test")

    # 1. Create Deviation
    dev_payload = {
        "study_id": "study_123",
        "title": "Temp excursion",
        "description": "IP storage excursion",
        "severity": "CRITICAL",
        "type": "IP_MANAGEMENT",
        "is_protocol_violation": False,
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        dev_res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=headers
        )
        dev_id = dev_res.json()["id"]

        # 2. Attach RCA
        rca_headers = get_auth_headers(change_reason="Perform initial RCA investigation")
        rca_payload = {
            "methodology": "5 Whys",
            "investigation_details": "Power went down in facility",
            "root_cause_summary": "Backup generator failed",
        }
        rca_res = await client.post(
            f"/api/v1/quality/deviations/{dev_id}/rca",
            json=rca_payload,
            headers=rca_headers,
        )
        assert rca_res.status_code == 200
        rca_data = rca_res.json()
        assert rca_data["deviation_id"] == dev_id
        assert rca_data["methodology"] == "5 Whys"
        assert rca_data["version_index"] == 1

        # Verify parent deviation transitioned to RCA_COMPLETE
        dev_view = await client.get(f"/api/v1/quality/deviations/{dev_id}", headers=rca_headers)
        assert dev_view.json()["status"] == "RCA_COMPLETE"
        assert dev_view.json()["version_index"] == 2

        # 3. Update RCA with optimistic lock check
        update_headers = get_auth_headers(change_reason="Update RCA with details")
        rca_payload_update = {
            "methodology": "Fishbone Diagram",
            "investigation_details": "Updated investigation details",
            "root_cause_summary": "Updated root cause summary",
            "version_index": 1,
        }
        rca_update_res = await client.put(
            f"/api/v1/quality/deviations/{dev_id}/rca",
            json=rca_payload_update,
            headers=update_headers,
        )
        assert rca_update_res.status_code == 200
        assert rca_update_res.json()["version_index"] == 2
        assert rca_update_res.json()["methodology"] == "Fishbone Diagram"

        # 4. Trigger version conflict (409)
        conflict_payload = {
            "methodology": "Fishbone Diagram",
            "investigation_details": "Conflict",
            "root_cause_summary": "Conflict",
            "version_index": 1,  # Stale version index
        }
        conflict_res = await client.put(
            f"/api/v1/quality/deviations/{dev_id}/rca",
            json=conflict_payload,
            headers=update_headers,
        )
        assert conflict_res.status_code == 409
        assert "Version conflict" in conflict_res.json()["detail"]

        # 5. Non-existent deviation for RCA creation/update (404)
        response_rca_404 = await client.post(
            "/api/v1/quality/deviations/nonexistent-id/rca",
            json=rca_payload,
            headers=headers,
        )
        assert response_rca_404.status_code == 404


# ==========================================
# 4. CAPA Creation and Validation Tests
# ==========================================

@pytest.mark.asyncio
async def test_capa_creation_validations_api():
    """
    Verify that CAPA creation validates parent records and statuses.
    """
    headers = get_auth_headers(change_reason="Create deviation for CAPA validations")

    # 1. Non-existent deviation
    capa_payload = {
        "deviation_id": "non-existent-id",
        "capa_type": "CORRECTIVE",
        "action_plan": "Testing",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        res = await client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
        assert res.status_code == 422
        assert "Parent deviation" in res.json()["detail"]

        # 2. Create valid deviation
        dev_payload = {
            "study_id": "study_123",
            "title": "Temp excursion",
            "description": "IP storage excursion",
            "severity": "CRITICAL",
            "type": "IP_MANAGEMENT",
        }
        dev_res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=headers
        )
        dev_id = dev_res.json()["id"]

        # 3. Supply invalid/non-existent RCA ID
        capa_payload_invalid_rca = {
            "deviation_id": dev_id,
            "rca_id": "non-existent-rca-id",
            "capa_type": "CORRECTIVE",
            "action_plan": "Testing",
        }
        res_rca = await client.post(
            "/api/v1/quality/capas", json=capa_payload_invalid_rca, headers=headers
        )
        assert res_rca.status_code == 422
        assert "RCA with ID" in res_rca.json()["detail"]

        # 4. Supply mismatched RCA ID (belonging to another deviation)
        another_dev_res = await client.post(
            "/api/v1/quality/deviations",
            json={**dev_payload, "study_id": "study_other"},
            headers=headers,
        )
        another_dev_id = another_dev_res.json()["id"]

        rca_payload = {
            "methodology": "5 Whys",
            "investigation_details": "Details",
            "root_cause_summary": "Summary",
        }
        another_rca_res = await client.post(
            f"/api/v1/quality/deviations/{another_dev_id}/rca",
            json=rca_payload,
            headers=headers,
        )
        another_rca_id = another_rca_res.json()["id"]

        capa_payload_mismatched_rca = {
            "deviation_id": dev_id,  # deviation_id is first dev
            "rca_id": another_rca_id,  # rca_id is second dev's rca
            "capa_type": "CORRECTIVE",
            "action_plan": "Testing",
        }
        res_mismatched = await client.post(
            "/api/v1/quality/capas", json=capa_payload_mismatched_rca, headers=headers
        )
        assert res_mismatched.status_code == 422
        assert "not linked to deviation ID" in res_mismatched.json()["detail"]


@pytest.mark.asyncio
async def test_capa_creation_for_closed_or_resolved_deviation():
    """
    Verify that creating a CAPA for a settled or closed deviation is rejected with 422.
    """
    # Create deviation
    async with db_manager.get_session_maker()() as session:
        dev = Deviation(
            study_id="study_123",
            title="Closed deviation",
            description="Testing closed dev boundary",
            severity=DeviationSeverity.MINOR,
            status=DeviationStatus.CLOSED,  # Terminal status
            type=DeviationType.OTHER,
            created_by="admin",
            version_index=1,
            reason_for_change="Seeded as closed",
        )
        session.add(dev)
        await session.commit()
        dev_id = dev.id

    headers = get_auth_headers(change_reason="Try creating CAPA for closed dev")
    capa_payload = {
        "deviation_id": dev_id,
        "capa_type": "CORRECTIVE",
        "action_plan": "Testing",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        res = await client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
        assert res.status_code == 422
        assert "Cannot create CAPA for a settled or closed deviation" in res.json()["detail"]


# ==========================================
# 5. CAPA Lifecycle Transitions & Updates
# ==========================================

@pytest.mark.asyncio
async def test_capa_lifecycle_transitions_api():
    """
    Verify legal and illegal CAPA status transitions.
    """
    headers = get_auth_headers(change_reason="CAPA lifecycle testing")

    # 1. Create deviation and CAPA
    dev_payload = {
        "study_id": "study_123",
        "title": "Protocol violation",
        "description": "Protocol violation desc",
        "severity": "MAJOR",
        "type": "ELIGIBILITY",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        dev_res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=headers
        )
        dev_id = dev_res.json()["id"]

        capa_payload = {
            "deviation_id": dev_id,
            "capa_type": "CORRECTIVE",
            "action_plan": "Do training on eligibility",
        }
        capa_res = await client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
        assert capa_res.status_code == 201
        capa_id = capa_res.json()["id"]
        assert capa_res.json()["status"] == "INITIATED"
        assert capa_res.json()["version_index"] == 1

        # Verify deviation status automatically transitioned to CAPA_INITIATED
        dev_view = await client.get(f"/api/v1/quality/deviations/{dev_id}", headers=headers)
        assert dev_view.json()["status"] == "CAPA_INITIATED"

        # 2. Transition: INITIATED -> UNDER_REVIEW (Legal)
        trans_res1 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "UNDER_REVIEW", "version_index": 1},
            headers=headers,
        )
        assert trans_res1.status_code == 200
        assert trans_res1.json()["status"] == "UNDER_REVIEW"
        assert trans_res1.json()["version_index"] == 2

        # 3. Transition: UNDER_REVIEW -> EFFECTIVENESS_CHECK (Illegal - bypasses IMPLEMENTATION)
        trans_res_illegal = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 2},
            headers=headers,
        )
        assert trans_res_illegal.status_code == 422
        assert "Invalid transition" in trans_res_illegal.json()["detail"]

        # 4. Transition: UNDER_REVIEW -> IMPLEMENTATION (Legal)
        trans_res2 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "IMPLEMENTATION", "version_index": 2},
            headers=headers,
        )
        assert trans_res2.status_code == 200
        assert trans_res2.json()["status"] == "IMPLEMENTATION"
        assert trans_res2.json()["version_index"] == 3

        # 5. Transition: IMPLEMENTATION -> EFFECTIVENESS_CHECK (Legal)
        trans_res3 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 3},
            headers=headers,
        )
        assert trans_res3.status_code == 200
        assert trans_res3.json()["status"] == "EFFECTIVENESS_CHECK"
        assert trans_res3.json()["version_index"] == 4

        # 6. Transition: EFFECTIVENESS_CHECK -> CLOSED (Legal)
        trans_res4 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "CLOSED", "version_index": 4},
            headers=headers,
        )
        assert trans_res4.status_code == 200
        assert trans_res4.json()["status"] == "CLOSED"
        assert trans_res4.json()["version_index"] == 5

        # Verify linked deviation status settlement (status must be CLOSED now)
        dev_view2 = await client.get(f"/api/v1/quality/deviations/{dev_id}", headers=headers)
        assert dev_view2.json()["status"] == "CLOSED"
        assert dev_view2.json()["version_index"] > 1

        # 7. Try to transition out of terminal state (Illegal)
        trans_terminal = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "UNDER_REVIEW", "version_index": 5},
            headers=headers,
        )
        assert trans_terminal.status_code == 422
        assert "terminal state" in trans_terminal.json()["detail"]


@pytest.mark.asyncio
async def test_capa_updates_and_concurrency_api():
    """
    Verify updates to CAPA records and optimistic locking behavior.
    """
    headers = get_auth_headers(change_reason="Concurrency testing")

    # 1. Create parent deviation and CAPA
    dev_payload = {
        "study_id": "study_123",
        "title": "IP Temp Excursion",
        "description": "IP excursion",
        "severity": "CRITICAL",
        "type": "IP_MANAGEMENT",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        dev_res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=headers
        )
        dev_id = dev_res.json()["id"]

        capa_payload = {
            "deviation_id": dev_id,
            "capa_type": "CORRECTIVE",
            "action_plan": "Initial Plan",
        }
        capa_res = await client.post("/api/v1/quality/capas", json=capa_payload, headers=headers)
        capa_id = capa_res.json()["id"]

        # 2. Update CAPA details (Legal)
        update_res = await client.put(
            f"/api/v1/quality/capas/{capa_id}",
            json={
                "action_plan": "Updated Action Plan",
                "preventive_measures": "New preventive measures",
                "target_completion_date": "2026-12-31T00:00:00",
                "version_index": 1,
            },
            headers=headers,
        )
        assert update_res.status_code == 200
        assert update_res.json()["action_plan"] == "Updated Action Plan"
        assert update_res.json()["preventive_measures"] == "New preventive measures"
        assert update_res.json()["target_completion_date"] is not None
        assert update_res.json()["version_index"] == 2

        # 3. Update CAPA with stale version index (Illegal -> 409)
        stale_update_res = await client.put(
            f"/api/v1/quality/capas/{capa_id}",
            json={"action_plan": "Stale Update Plan", "version_index": 1},
            headers=headers,
        )
        assert stale_update_res.status_code == 409
        assert "Version conflict" in stale_update_res.json()["detail"]

        # 4. Transition CAPA to CLOSED
        await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "UNDER_REVIEW", "version_index": 2},
            headers=headers,
        )
        await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "IMPLEMENTATION", "version_index": 3},
            headers=headers,
        )
        await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 4},
            headers=headers,
        )
        await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "CLOSED", "version_index": 5},
            headers=headers,
        )

        # 5. Try updating details of a closed CAPA (Illegal -> 422)
        closed_update = await client.put(
            f"/api/v1/quality/capas/{capa_id}",
            json={"action_plan": "No changes allowed", "version_index": 6},
            headers=headers,
        )
        assert closed_update.status_code == 422
        assert "terminal state" in closed_update.json()["detail"]


@pytest.mark.asyncio
async def test_capa_endpoints_404_and_409():
    """
    Cover 404 nonexistent resource checks and 409 version conflicts for transition.
    """
    headers = get_auth_headers(change_reason="Error checking")

    # Nonexistent transition
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        res = await client.post(
            "/api/v1/quality/capas/nonexistent-id/transition",
            json={"to_status": "UNDER_REVIEW"},
            headers=headers,
        )
        assert res.status_code == 404

        # Nonexistent update
        res2 = await client.put(
            "/api/v1/quality/capas/nonexistent-id",
            json={"action_plan": "Plan"},
            headers=headers,
        )
        assert res2.status_code == 404

        # Create CAPA to trigger transition 409
        dev_payload = {
            "study_id": "study_123",
            "title": "Protocol violation",
            "description": "Protocol violation desc",
            "severity": "MAJOR",
            "type": "ELIGIBILITY",
        }
        dev_res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=headers
        )
        dev_id = dev_res.json()["id"]

        capa_res = await client.post(
            "/api/v1/quality/capas",
            json={"deviation_id": dev_id, "capa_type": "CORRECTIVE", "action_plan": "Plan"},
            headers=headers,
        )
        capa_id = capa_res.json()["id"]

        # Transition with wrong version index
        res3 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "UNDER_REVIEW", "version_index": 999},
            headers=headers,
        )
        assert res3.status_code == 409


# ==========================================
# 6. Authorization & Role-Based Access Control
# ==========================================

@pytest.mark.asyncio
async def test_read_only_roles_forbidden_api():
    """
    Verify that read-only roles (auditor, inspector, viewer, etc.) receive 403 Forbidden on all mutations.
    """
    ro_headers = get_auth_headers(roles="auditor", change_reason="Trying to write")

    payload = {
        "study_id": "study_123",
        "title": "Unpermitted deviation",
        "description": "Should fail",
        "severity": "MAJOR",
        "type": "INFORMED_CONSENT",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        res = await client.post("/api/v1/quality/deviations", json=payload, headers=ro_headers)
        assert res.status_code == 403
        assert "Forbidden" in res.json()["detail"]

        # Create with admin to obtain a valid ID for further test stages
        admin_headers = get_auth_headers(
            roles="admin", change_reason="Creating base deviation"
        )
        dev_res = await client.post(
            "/api/v1/quality/deviations", json=payload, headers=admin_headers
        )
        assert dev_res.status_code == 201
        dev_id = dev_res.json()["id"]

        # 2. Attach/Update RCA
        rca_payload = {
            "methodology": "5 Whys",
            "investigation_details": "Failed attempt",
            "root_cause_summary": "Should fail",
        }
        rca_res = await client.post(
            f"/api/v1/quality/deviations/{dev_id}/rca", json=rca_payload, headers=ro_headers
        )
        assert rca_res.status_code == 403

        # 3. Create CAPA
        capa_payload = {
            "deviation_id": dev_id,
            "capa_type": "CORRECTIVE",
            "action_plan": "Should fail",
        }
        capa_res = await client.post(
            "/api/v1/quality/capas", json=capa_payload, headers=ro_headers
        )
        assert capa_res.status_code == 403

        # Create CAPA with admin for transition/update checks
        admin_capa_res = await client.post(
            "/api/v1/quality/capas", json=capa_payload, headers=admin_headers
        )
        assert admin_capa_res.status_code == 201
        capa_id = admin_capa_res.json()["id"]

        # 4. Transition CAPA
        trans_res = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "UNDER_REVIEW", "version_index": 1},
            headers=ro_headers,
        )
        assert trans_res.status_code == 403

        # 5. Update CAPA
        update_res = await client.put(
            f"/api/v1/quality/capas/{capa_id}",
            json={"action_plan": "Should fail", "version_index": 1},
            headers=ro_headers,
        )
        assert update_res.status_code == 403


@pytest.mark.asyncio
async def test_capa_approval_closure_requires_quality_oversight_api():
    """
    Verify that general write roles (e.g. cra) can perform general transitions but only quality oversight roles
    (e.g. quality_manager, qa_lead, quality_oversight, admin) can perform approval/closure transitions.
    """
    admin_headers = get_auth_headers(
        roles="admin", change_reason="Creating deviation and CAPA"
    )

    # Create deviation
    dev_payload = {
        "study_id": "study_123",
        "title": "Base deviation",
        "description": "Desc",
        "severity": "MAJOR",
        "type": "INFORMED_CONSENT",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        dev_res = await client.post(
            "/api/v1/quality/deviations", json=dev_payload, headers=admin_headers
        )
        dev_id = dev_res.json()["id"]

        # Create CAPA
        capa_payload = {
            "deviation_id": dev_id,
            "capa_type": "CORRECTIVE",
            "action_plan": "Action",
        }
        capa_res = await client.post(
            "/api/v1/quality/capas", json=capa_payload, headers=admin_headers
        )
        capa_id = capa_res.json()["id"]

        # Check broader write role can transition to UNDER_REVIEW
        cra_headers = get_auth_headers(
            roles="cra", change_reason="Transitioning to under review"
        )
        trans_res1 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "UNDER_REVIEW", "version_index": 1},
            headers=cra_headers,
        )
        assert trans_res1.status_code == 200
        assert trans_res1.json()["status"] == "UNDER_REVIEW"

        # Transition to IMPLEMENTATION via cra
        trans_res2 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "IMPLEMENTATION", "version_index": 2},
            headers=cra_headers,
        )
        assert trans_res2.status_code == 200

        # Transition to EFFECTIVENESS_CHECK via cra
        trans_res3 = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "EFFECTIVENESS_CHECK", "version_index": 3},
            headers=cra_headers,
        )
        assert trans_res3.status_code == 200

        # Try transitioning to CLOSED (closure) via general write role (cra) - should fail with 403
        trans_res4_fail = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "CLOSED", "version_index": 4},
            headers=cra_headers,
        )
        assert trans_res4_fail.status_code == 403
        assert "Quality oversight role required" in trans_res4_fail.json()["detail"]

        # Successfully transition to CLOSED using a quality oversight role (e.g. quality_manager)
        qm_headers = get_auth_headers(
            roles="quality_manager", change_reason="Closing CAPA and deviation"
        )
        trans_res4_success = await client.post(
            f"/api/v1/quality/capas/{capa_id}/transition",
            json={"to_status": "CLOSED", "version_index": 4},
            headers=qm_headers,
        )
        assert_trans = trans_res4_success.status_code == 200
        assert assert_trans
        assert trans_res4_success.json()["status"] == "CLOSED"


# ==========================================
# 7. Audit Logging & Atomicity
# ==========================================

@pytest.mark.asyncio
async def test_mutation_without_change_reason_rejected_api():
    """
    Verify that mutations without X-Change-Reason are rejected by the gateway middleware.
    """
    headers = get_auth_headers(roles="admin")
    headers.pop("X-Change-Reason", None)

    payload = {
        "study_id": "study_123",
        "title": "Failed deviation",
        "description": "Missing reason",
        "severity": "MINOR",
        "type": "OTHER",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        res = await client.post("/api/v1/quality/deviations", json=payload, headers=headers)
        assert res.status_code in (401, 403)
        assert "change justification" in res.json()["detail"].lower()


@pytest.mark.asyncio
async def test_successful_mutation_creates_audit_log_and_is_atomic_api():
    """
    Verify that every successful mutation creates an atomic audit log entry with detailed information.
    """
    headers = get_auth_headers(roles="admin", change_reason="Initial mutation testing")

    payload = {
        "study_id": "study_123",
        "title": "Audit-logged Deviation",
        "description": "Testing audit log creation",
        "severity": "CRITICAL",
        "type": "PROTOCOL_PROCEDURE",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        res = await client.post("/api/v1/quality/deviations", json=payload, headers=headers)
        assert res.status_code == 201
        dev_id = res.json()["id"]

        audit_res = await client.get("/api/v1/quality/audit-logs", headers=headers)
        assert audit_res.status_code == 200
        logs = audit_res.json()

        # Find DEVIATION_CREATE log
        create_log = next(
            (log for log in logs if log["action"] == "DEVIATION_CREATE"), None
        )
        assert create_log is not None
        assert create_log["user_id"] == "quality_test_user"
        assert create_log["user_role"] == "admin"
        assert create_log["record_id"] == dev_id
        assert create_log["change_reason"] == "Initial mutation testing"


@pytest.mark.asyncio
async def test_audit_log_endpoint_properties_api():
    """
    Verify that the audit logs endpoint:
    - returns entries in newest-first (descending chronological) order.
    - exposes no write endpoints (POST/PUT/DELETE return 405).
    """
    headers = get_auth_headers(roles="admin", change_reason="Audit check")

    payload = {
        "study_id": "study_123",
        "title": "Dev 1",
        "description": "Desc 1",
        "severity": "MINOR",
        "type": "OTHER",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        await client.post("/api/v1/quality/deviations", json=payload, headers=headers)

        payload["title"] = "Dev 2"
        await client.post("/api/v1/quality/deviations", json=payload, headers=headers)

        res = await client.get("/api/v1/quality/audit-logs", headers=headers)
        assert res.status_code == 200
        logs = res.json()

        assert len(logs) >= 2
        from datetime import datetime

        timestamps = [datetime.fromisoformat(log["timestamp"]) for log in logs]
        for i in range(len(timestamps) - 1):
            assert timestamps[i] >= timestamps[i + 1]

        # Verify write endpoints are blocked with 405 Method Not Allowed
        res_post = await client.post("/api/v1/quality/audit-logs", json={}, headers=headers)
        assert res_post.status_code == 405


@pytest.mark.asyncio
async def test_permission_failure_leaves_no_misleading_audit_entry_api():
    """
    Verify that if permission checks fail, no domain entities are created/modified,
    and no misleading audit log is written (atomicity & consistency).
    """
    ro_headers = get_auth_headers(roles="auditor", change_reason="Unpermitted attempt")

    admin_headers = get_auth_headers(roles="admin", change_reason="Counting")
    payload = {
        "study_id": "study_123",
        "title": "Forbidden deviation",
        "description": "No record should be created",
        "severity": "MINOR",
        "type": "OTHER",
    }
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as client:
        res = await client.post("/api/v1/quality/deviations", json=payload, headers=ro_headers)
        assert res.status_code == 403

        dev_list_res = await client.get(
            "/api/v1/quality/deviations?study_id=study_123", headers=admin_headers
        )
        assert len(dev_list_res.json()) == 0

        final_audit_res = await client.get("/api/v1/quality/audit-logs", headers=admin_headers)
        final_logs = final_audit_res.json()
        assert not any(
            log["action"] == "DEVIATION_CREATE" and "Forbidden deviation" in log["details"]
            for log in final_logs
        )


# ==========================================
# 8. Direct Route Handler Invocation
# ==========================================

@pytest.mark.asyncio
async def test_route_handlers_missing_change_reason():
    """
    Directly call route handlers with a mock Request missing change_reason to cover
    the secondary fallback validation checks (HTTP 403 / Missing change justification).
    """
    async with db_manager.get_session_maker()() as session:
        # Mock request authorized as admin, but lacking change_reason in state/headers
        mock_request = MockRequest(
            state_dict={"user_id": "test_user", "roles": ["admin"], "change_reason": None},
            headers_dict={"X-User-Roles": "admin"}
        )

        # 1. create_deviation
        from apps.quality.main import DeviationCreate
        dev_payload = DeviationCreate(
            study_id="study_1",
            site_id="site_1",
            title="Title",
            description="Desc",
            severity=DeviationSeverity.MINOR,
            type=DeviationType.OTHER,
        )
        with pytest.raises(HTTPException) as exc_info:
            await create_deviation(request=mock_request, payload=dev_payload, session=session)
        assert exc_info.value.status_code == 403
        assert "Missing change justification reason" in exc_info.value.detail

        # Seed deviation to test other handlers
        dev = Deviation(
            study_id="study_1",
            title="Title",
            description="Desc",
            severity=DeviationSeverity.MINOR,
            status=DeviationStatus.REPORTED,
            type=DeviationType.OTHER,
            created_by="admin",
            version_index=1,
            reason_for_change="Initial",
        )
        session.add(dev)
        await session.flush()
        dev_id = dev.id

        # 2. create_or_update_rca
        from apps.quality.main import RCACreateOrUpdate
        rca_payload = RCACreateOrUpdate(
            methodology="5 Whys",
            investigation_details="Details",
            root_cause_summary="Summary",
        )
        with pytest.raises(HTTPException) as exc_info:
            await create_or_update_rca(request=mock_request, id=dev_id, payload=rca_payload, session=session)
        assert exc_info.value.status_code == 403

        # 3. create_capa
        from apps.quality.main import CAPACreate
        capa_payload = CAPACreate(
            deviation_id=dev_id,
            capa_type="CORRECTIVE",
            action_plan="Plan",
        )
        with pytest.raises(HTTPException) as exc_info:
            await create_capa(request=mock_request, payload=capa_payload, session=session)
        assert exc_info.value.status_code == 403

        # Seed CAPA for transition and update checks
        capa = CAPARecord(
            deviation_id=dev_id,
            capa_type="CORRECTIVE",
            action_plan="Plan",
            study_id="study_1",
            created_by="admin",
            version_index=1,
            reason_for_change="Seeded",
        )
        session.add(capa)
        await session.flush()
        capa_id = capa.id

        # 4. transition_capa
        from apps.quality.main import CAPATransitionRequest
        trans_payload = CAPATransitionRequest(to_status=CAPAStatus.UNDER_REVIEW)
        with pytest.raises(HTTPException) as exc_info:
            await transition_capa(request=mock_request, id=capa_id, payload=trans_payload, session=session)
        assert exc_info.value.status_code == 403

        # 5. update_capa
        from apps.quality.main import CAPAUpdate
        update_payload = CAPAUpdate(action_plan="Updated Plan")
        with pytest.raises(HTTPException) as exc_info:
            await update_capa(request=mock_request, id=capa_id, payload=update_payload, session=session)
        assert exc_info.value.status_code == 403
