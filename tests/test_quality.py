import time
from datetime import datetime

import pytest
import pytest_asyncio
from fastapi.testclient import TestClient
from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import selectinload

from apps.gateway.main import generate_signature
from apps.quality.database import db_manager
from apps.quality.main import app, write_audit_log
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
    async with db_manager.engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
    await db_manager.close()


def get_auth_headers(roles: str = "admin", change_reason: str = "") -> dict:
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


def test_quality_health_check():
    """
    Verify health check of independent Quality & CAPA service.
    """
    client = TestClient(app)
    response = client.get("/health")
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
        # Querying each table using empty selects to ensure they exist and have columns defined correctly
        deviations = await session.execute(select(Deviation))
        rcas = await session.execute(select(RootCauseAnalysis))
        capas = await session.execute(select(CAPARecord))
        logs = await session.execute(select(QualityAuditLog))

        assert deviations.scalars().all() == []
        assert rcas.scalars().all() == []
        assert capas.scalars().all() == []
        assert logs.scalars().all() == []


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
        # Ensure enums persist and compare properly
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


@pytest.mark.asyncio
async def test_quality_audit_log_append_only():
    """
    Verify writing to the QualityAuditLog as an append-only ledger.
    """
    async with db_manager.get_session_maker()() as session:
        await write_audit_log(
            session=session,
            user_id="user_admin",
            user_role="Admin",
            action="DEVIATION_CREATE",
            details="Created deviation event for study_abc",
        )
        await session.commit()

    async with db_manager.get_session_maker()() as session:
        stmt = select(QualityAuditLog).order_by(QualityAuditLog.timestamp.desc())
        result = await session.execute(stmt)
        logs = result.scalars().all()

        assert len(logs) == 1
        log = logs[0]
        assert log.user_id == "user_admin"
        assert log.user_role == "Admin"
        assert log.action == "DEVIATION_CREATE"
        assert log.details == "Created deviation event for study_abc"
        assert isinstance(log.timestamp, datetime)


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
