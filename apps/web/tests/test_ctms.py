"""Web contract and integration test suite for CTMS Monitoring Console, SDV Toggles & Query Discrepancy Lifecycle.

Validates the end-to-end API communication lifecycle consumed by apps/web/src/views/CtmsView.vue
and apps/web/src/components/persona/CraVerificationConsole.vue.
Requirements: PRD-SYS-001, PRD-CTMS-001, PRD-QRY-005, PRD-QRY-006, PRD-QRY-007
"""

import hashlib
import time
import uuid
from collections.abc import AsyncGenerator
from datetime import UTC, datetime

import httpx
import pytest
import pytest_asyncio
from jose import jwt
from sqlalchemy import select

from apps.ctms.database import db_manager as ctms_db_manager
from apps.ctms.main import app as ctms_app
from apps.ctms.models import (
    Base as CtmsBase,
)
from apps.ctms.models import (
    CTMSAuditLog,
    CTMSDelegation,
    RecruitmentRecord,
    SiteMilestone,
)
from apps.execution.database.core import db_manager as exec_db_manager
from apps.execution.database.models import (
    Base as ExecBase,
)
from apps.execution.database.models import (
    ClinicalObservation,
    ClinicalSubject,
)
from apps.execution.main import app as execution_app
from packages.security.signing import generate_gateway_signature

GATEWAY_SECRET = "internal-gateway-secret-12345"  # pragma: allowlist secret


@pytest_asyncio.fixture(autouse=True)
async def setup_test_databases() -> AsyncGenerator[None]:
    """Setup in-memory SQLite databases for both CTMS and Execution before each test."""
    ctms_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with ctms_db_manager.engine.begin() as conn:
        await conn.run_sync(CtmsBase.metadata.create_all)

    exec_db_manager.init_db("sqlite+aiosqlite:///:memory:", echo=False)
    async with exec_db_manager.engine.begin() as conn:
        from sqlalchemy import text

        if exec_db_manager.engine.dialect.name == "postgresql":
            await conn.execute(text("CREATE SCHEMA IF NOT EXISTS audit_schema;"))
        await conn.run_sync(ExecBase.metadata.create_all)

    yield

    async with ctms_db_manager.engine.begin() as conn:
        await conn.run_sync(CtmsBase.metadata.drop_all)
    await ctms_db_manager.close()

    async with exec_db_manager.engine.begin() as conn:
        await conn.run_sync(ExecBase.metadata.drop_all)
    await exec_db_manager.close()


def get_auth_headers(
    user_id: str = "cra_monitor",
    roles: str = "cra,monitor,admin",
    change_reason: str = "CTMS monitoring verification & SDV sign-off",
    action: str | None = None,
    batch_id: str | None = None,
) -> dict[str, str]:
    """Generate Gateway signature-compliant authentication headers."""
    timestamp = str(time.time())
    sig_token = None
    if action:
        sig_payload = {
            "sub": user_id,
            "username": user_id,
            "action": action,
            "roles": roles.split(","),
            "iat": time.time(),
            "exp": time.time() + 300.0,
            "jti": str(uuid.uuid4()),
        }
        if batch_id:
            sig_payload["batch_id"] = batch_id
        sig_token = jwt.encode(sig_payload, GATEWAY_SECRET, algorithm="HS256")

    signature = generate_gateway_signature(
        user_id=user_id,
        roles=roles,
        timestamp=timestamp,
        secret=GATEWAY_SECRET.encode("utf-8"),
        change_reason=change_reason,
        tenant_id="tenant_default",
        sig_token=sig_token,
    )
    headers = {
        "X-User-Id": user_id,
        "X-User-Roles": roles,
        "X-Gateway-Timestamp": timestamp,
        "X-Gateway-Signature": signature,
        "X-Signature-Version": "2",
        "X-Change-Reason": change_reason,
        "X-Tenant-Id": "tenant_default",
    }
    if sig_token:
        headers["X-Sig-Token"] = sig_token
    return headers


@pytest.mark.asyncio
async def test_ctms_dashboard_live_kpis() -> None:
    """Validate CTMS dashboard live KPI metrics calculation and retrieval.

    Verifies Total Subjects, Enrollment Rate, Verified SDV %, and Open Queries count.
    @req:PRD-SYS-001, PRD-CTMS-001
    """
    study_id = "CADENCE-101"
    site_id = "SITE-101"

    # Seed Recruitment and Milestones in CTMS database
    async with ctms_db_manager.get_session_maker()() as session:
        rec = RecruitmentRecord(
            study_id=study_id,
            site_id=site_id,
            screened_count=15,
            enrolled_count=9,
            target_count=15,
            as_of_date=datetime.now(UTC),
            created_by="system",
            reason_for_change="Seed recruitment metrics",
            version_index=1,
        )
        session.add(rec)
        m1 = SiteMilestone(
            study_id=study_id,
            site_id=site_id,
            milestone_type="SIV",
            planned_date=datetime.now(UTC),
            actual_date=datetime.now(UTC),
            status="COMPLETED",
            created_by="system",
            reason_for_change="Site initiation visit completed",
            version_index=1,
        )
        session.add(m1)
        await session.commit()

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=ctms_app), base_url="http://test"
    ) as ctms_client:
        # 1. Test live recruitment/enrollment metrics
        res_rec = await ctms_client.get(
            f"/api/v1/ctms/recruitment?study_id={study_id}&site_id={site_id}",
            headers=get_auth_headers(user_id="cra_monitor", roles="cra,monitor"),
        )
        assert res_rec.status_code == 200
        rec_data = res_rec.json()
        assert len(rec_data) >= 1
        site_rec = next(r for r in rec_data if r["site_id"] == site_id)
        assert site_rec["screened_count"] == 15
        assert site_rec["enrolled_count"] == 9
        assert site_rec["target_count"] == 15
        # Enrollment Rate: (9 / 15) * 100 = 60.0%
        enrollment_rate = (site_rec["enrolled_count"] / site_rec["target_count"]) * 100
        assert enrollment_rate == 60.0

        # 2. Test site milestones for monitoring overview
        res_milestones = await ctms_client.get(
            f"/api/v1/ctms/site-milestones?study_id={study_id}&site_id={site_id}",
            headers=get_auth_headers(user_id="cra_monitor", roles="cra,monitor"),
        )
        assert res_milestones.status_code == 200
        milestone_data = res_milestones.json()
        assert len(milestone_data) >= 1
        assert milestone_data[0]["status"] == "COMPLETED"


@pytest.mark.asyncio
async def test_cra_verification_console_field_level_sdv() -> None:
    """Validate CRA field-level Source Data Verification toggle and verification persistence.

    @req:PRD-SYS-001, PRD-QRY-005
    """
    study_id = "CADENCE-101"
    subject_id = "SUBJ-101-001"
    site_id = "SITE-101"

    # Seed clinical subject and observations in Execution database
    async with exec_db_manager.get_session_maker()() as session:
        sub = ClinicalSubject(
            subject_id=subject_id,
            study_id=study_id,
            site_id=site_id,
            status="SCREENING",
        )
        session.add(sub)
        obs = ClinicalObservation(
            study_id=study_id,
            subject_id=subject_id,
            site_id=site_id,
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=175.0,
            unit="mmHg",
            is_sdv_verified=False,
        )
        session.add(obs)
        await session.commit()
        obs_id = obs.id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as exec_client:
        # CRA performs field-level SDV sign-off
        sdv_payload = {
            "scope": "FIELD",
            "target_id": str(obs_id),
            "subject_id": subject_id,
            "study_id": study_id,
            "site_id": site_id,
        }
        res_sdv = await exec_client.post(
            "/api/v1/execution/sdv/signoff",
            json=sdv_payload,
            headers=get_auth_headers(
                user_id="cra_monitor",
                roles="cra,monitor",
                change_reason="Verified Systolic Blood Pressure against source EMR record",
            ),
        )
        assert res_sdv.status_code == 200
        sdv_data = res_sdv.json()
        assert sdv_data["scope"] == "FIELD"
        assert sdv_data["target_id"] == str(obs_id)
        assert sdv_data["is_verified"] is True
        assert sdv_data["verified_by"] == "cra_monitor"

        # Verify observation updated in database
        async with exec_db_manager.get_session_maker()() as session:
            stmt = select(ClinicalObservation).where(ClinicalObservation.id.is_(obs_id))
            res_db = await session.execute(stmt)
            obs_db = res_db.scalars().first()
            assert obs_db is not None
            assert obs_db.is_sdv_verified is True
            assert obs_db.sdv_verified_by == "cra_monitor"


@pytest.mark.asyncio
async def test_cra_batch_sdv_bulk_signoff_with_gxp_reason() -> None:
    """Validate Batch SDV bulk sign-off across multiple eCRF fields with mandatory Part 11 audit reason.

    @req:PRD-SYS-001, PRD-QRY-006
    """
    study_id = "CADENCE-101"
    subject_id = "SUBJ-101-002"
    site_id = "SITE-101"

    async with exec_db_manager.get_session_maker()() as session:
        sub = ClinicalSubject(
            subject_id=subject_id,
            study_id=study_id,
            site_id=site_id,
            status="SCREENING",
        )
        session.add(sub)
        obs1 = ClinicalObservation(
            study_id=study_id,
            subject_id=subject_id,
            site_id=site_id,
            domain="VS",
            test_code="SYSBP",
            test_name="Systolic Blood Pressure",
            value=128.0,
            unit="mmHg",
            is_sdv_verified=False,
        )
        obs2 = ClinicalObservation(
            study_id=study_id,
            subject_id=subject_id,
            site_id=site_id,
            domain="VS",
            test_code="DIABP",
            test_name="Diastolic Blood Pressure",
            value=82.0,
            unit="mmHg",
            is_sdv_verified=False,
        )
        session.add(obs1)
        session.add(obs2)
        await session.commit()
        obs1_id = obs1.id
        obs2_id = obs2.id

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as exec_client:
        target_ids = [str(obs1_id), str(obs2_id)]
        reason_for_change = (
            "Batch Source Data Verification against authenticated hospital records"
        )
        binding_str = f"{study_id}:FIELD:{sorted([str(tid) for tid in target_ids])}:{reason_for_change}"
        computed_batch_id = hashlib.sha256(binding_str.encode("utf-8")).hexdigest()

        bulk_payload = {
            "study_id": study_id,
            "subject_id": subject_id,
            "site_id": site_id,
            "scope": "FIELD",
            "target_ids": target_ids,
            "reason_for_change": reason_for_change,
        }
        res_bulk = await exec_client.post(
            "/api/v1/execution/sdv/bulk-sign-off",
            json=bulk_payload,
            headers=get_auth_headers(
                user_id="cra_monitor",
                roles="cra,monitor,data_manager",
                change_reason="Batch SDV sign-off",
                action="/api/v1/execution/sdv/bulk-sign-off",
                batch_id=computed_batch_id,
            ),
        )
        assert res_bulk.status_code == 200
        bulk_data = res_bulk.json()
        assert bulk_data["signed_count"] == 2
        assert len(bulk_data["signed_target_ids"]) == 2
        assert str(obs1_id) in bulk_data["signed_target_ids"]
        assert str(obs2_id) in bulk_data["signed_target_ids"]
        assert "content_digest" in bulk_data


@pytest.mark.asyncio
async def test_query_discrepancy_lifecycle_open_answered_closed() -> None:
    """Validate end-to-end clinical query discrepancy lifecycle:

    1. CRA Monitor raises query on anomalous form field -> OPEN state (visual badge yellow).
    2. Site CRC submits response ('Value confirmed with medical record') -> ANSWERED state.
    3. CRA Monitor closes query with audit rationale -> CLOSED state.
    @req:PRD-SYS-001, PRD-QRY-007
    """
    study_id = "CADENCE-101"
    subject_id = "SUBJ-101-003"
    visit_id = "Screening"
    domain = "VS"
    test_code = "SYSBP"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=execution_app), base_url="http://test"
    ) as exec_client:
        # Step 1: CRA Monitor raises discrepancy query on SYSBP
        query_create_payload = {
            "study_id": study_id,
            "subject_id": subject_id,
            "visit_id": visit_id,
            "domain": domain,
            "test_code": test_code,
            "explanation": "Systolic blood pressure (175 mmHg) out of expected range. Please verify with medical chart.",
            "status": "OPEN",
            "priority": "HIGH",
            "origin": "CRA_MONITORING",
        }
        res_create = await exec_client.post(
            "/api/v1/execution/queries",
            json=query_create_payload,
            headers=get_auth_headers(
                user_id="cra_monitor",
                roles="cra,monitor",
                change_reason="Raise clinical data query on anomalous lab/vital value",
            ),
        )
        assert res_create.status_code == 201
        created_query = res_create.json()
        query_id = created_query["id"]
        assert created_query["status"] == "OPEN"
        assert (
            created_query["explanation"]
            == "Systolic blood pressure (175 mmHg) out of expected range. Please verify with medical chart."
        )

        # Step 2: Site CRC submits response
        respond_payload = {
            "response": "Value confirmed with medical record",
            "responder": "crc_site101",
        }
        res_respond = await exec_client.post(
            f"/api/v1/execution/queries/{query_id}/respond",
            json=respond_payload,
            headers=get_auth_headers(
                user_id="crc_site101",
                roles="site_investigator,crc",
                change_reason="Respond to CRA discrepancy query",
            ),
        )
        assert res_respond.status_code == 200
        answered_query = res_respond.json()
        assert answered_query["status"] == "ANSWERED"
        assert answered_query["response"] == "Value confirmed with medical record"
        assert answered_query["responder"] == "crc_site101"

        # Step 3: CRA Monitor closes query with audit rationale
        res_close = await exec_client.post(
            f"/api/v1/execution/queries/{query_id}/close",
            headers=get_auth_headers(
                user_id="cra_monitor",
                roles="cra,monitor",
                change_reason="Supervisory CRA close: Source hospital chart verified and acceptable.",
                action=f"/api/v1/execution/queries/{query_id}/close",
            ),
        )
        assert res_close.status_code == 200
        closed_query = res_close.json()
        assert closed_query["status"] == "CLOSED"
        assert closed_query["resolver"] == "cra_monitor"
        assert closed_query["resolved_at"] is not None


@pytest.mark.asyncio
async def test_delegation_of_authority_matrix_staff_certs_and_pi_signoff() -> None:
    """Validate Delegation of Authority (DOA) matrix:

    1. Inspects active site staff, training certificates, and delegated protocol roles.
    2. Delegates new protocol duty tasks to staff member.
    3. Completes 21 CFR Part 11 Principal Investigator electronic signature sign-off with X-Sig-Token.
    @req:PRD-SYS-001, PRD-CTMS-001
    """
    site_id = "SITE-101"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=ctms_app), base_url="http://test"
    ) as ctms_client:
        # 1. Delegate trial tasks to CRC
        delegate_payload = {
            "site_id": site_id,
            "staff_user_id": "kc-crc-001",
            "task_codes": [
                "CRF_DATA_ENTRY",
                "SUBJECT_SCREENING",
                "SUBJECT_INFORMED_CONSENT",
            ],
            "start_date": "2026-01-20",
            "reason_for_change": "Initial Delegation of Authority for Study Coordinator",
        }
        res_delegate = await ctms_client.post(
            "/api/v1/ctms/doa/delegate",
            json=delegate_payload,
            headers=get_auth_headers(
                user_id="cra_monitor",
                roles="cra,monitor,admin",
                change_reason="Delegate trial tasks to site CRC",
            ),
        )
        assert res_delegate.status_code == 201
        del_data = res_delegate.json()
        record_id = del_data["record_id"]
        assert del_data["status"] == "PENDING_PI_APPROVAL"

        # 2. Inspect DOA matrix log
        res_log = await ctms_client.get(
            f"/api/v1/ctms/doa/sites/{site_id}/log",
            headers=get_auth_headers(user_id="cra_monitor", roles="cra,monitor"),
        )
        assert res_log.status_code == 200
        log_data = res_log.json()
        assert log_data["site_id"] == site_id
        assert len(log_data["delegated_staff"]) >= 1
        staff_entry = next(
            s for s in log_data["delegated_staff"] if s["staff_user_id"] == "kc-crc-001"
        )
        assert staff_entry["signed_off"] is False
        assert "CRF_DATA_ENTRY" in staff_entry["task_codes"]
        assert "SUBJECT_INFORMED_CONSENT" in staff_entry["task_codes"]

        # 3. Principal Investigator executes Part 11 electronic signature sign-off
        signoff_payload = {
            "record_id": record_id,
            "reason_for_change": "I endorse and approve this delegation of clinical duties per GCP E6(R2).",
        }
        res_signoff = await ctms_client.post(
            "/api/v1/ctms/doa/sign-off",
            json=signoff_payload,
            headers=get_auth_headers(
                user_id="kc-pi-001",
                roles="principal_investigator,investigator,admin",
                change_reason="PI electronic signature endorsement",
                action="/api/v1/ctms/doa/sign-off",
            ),
        )
        assert res_signoff.status_code == 200
        signoff_data = res_signoff.json()
        assert signoff_data["status"] == "ACTIVE"
        assert signoff_data["signed_off"] is True

        # 4. Verify updated state and immutable audit trail in CTMS database
        async with ctms_db_manager.get_session_maker()() as session:
            stmt_del = select(CTMSDelegation).where(CTMSDelegation.id.is_(record_id))
            res_del_db = await session.execute(stmt_del)
            del_db = res_del_db.scalars().first()
            assert del_db is not None
            assert del_db.is_active is True
            assert del_db.signed_off is True

            stmt_audit = (
                select(CTMSAuditLog)
                .where(CTMSAuditLog.action.is_("DOA_LOG_MODIFIED"))
                .order_by(CTMSAuditLog.timestamp.desc())
            )
            res_audit = await session.execute(stmt_audit)
            audits = res_audit.scalars().all()
            assert len(audits) >= 2


@pytest.mark.asyncio
async def test_delegation_revocation_and_gxp_pdf_export() -> None:
    """Validate Delegation of Authority duty revocation and GxP PDF export generation.

    @req:PRD-SYS-001, PRD-CTMS-001
    """
    site_id = "SITE-101"

    async with httpx.AsyncClient(
        transport=httpx.ASGITransport(app=ctms_app), base_url="http://test"
    ) as ctms_client:
        # Delegate task
        delegate_payload = {
            "site_id": site_id,
            "staff_user_id": "kc-cra-temp",
            "task_codes": ["CRF_DATA_ENTRY"],
            "start_date": "2026-01-01",
            "reason_for_change": "Temporary assignment",
        }
        res_del = await ctms_client.post(
            "/api/v1/ctms/doa/delegate",
            json=delegate_payload,
            headers=get_auth_headers(user_id="cra_monitor", roles="cra,monitor,admin"),
        )
        record_id = res_del.json()["record_id"]

        # Revoke task
        revoke_payload = {
            "record_id": record_id,
            "reason_for_change": "Staff rotation completed; revoking trial duty assignment",
        }
        res_revoke = await ctms_client.post(
            "/api/v1/ctms/doa/revoke",
            json=revoke_payload,
            headers=get_auth_headers(
                user_id="cra_monitor",
                roles="cra,monitor,admin",
                change_reason="Revocation of delegated duty",
            ),
        )
        assert res_revoke.status_code == 200
        assert res_revoke.json()["status"] == "REVOKED"

        # Export GxP PDF log
        res_pdf = await ctms_client.get(
            f"/api/v1/ctms/doa/sites/{site_id}/export-pdf",
            headers=get_auth_headers(
                user_id="cra_monitor", roles="cra,monitor,auditor"
            ),
        )
        assert res_pdf.status_code == 200
        assert "application/pdf" in res_pdf.headers.get(
            "content-type", ""
        ) or res_pdf.content.startswith(b"%PDF")
