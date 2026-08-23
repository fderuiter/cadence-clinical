"""
Tests for Per-Document Activity Feed Endpoints in Fileshare microservice.

Requirements:
- @req:PRD-DOC-001 (Document Activity Feed & Audit Trail)
- @req:PRD-SYS-001 (21 CFR Part 11 Audit Trail Logging)
"""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

import pytest
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.adapters.repositories import (
    SqlAlchemyFileAuditLogRepository,
    SqlAlchemyFileRecordRepository,
    SqlAlchemyShareGrantRepository,
)
from apps.fileshare.domain.models import (
    FileAuditLog,
    FileRecord,
    FileStatus,
    PermissionLevel,
    ScanStatus,
    ShareGrant,
    ShareScope,
)
from apps.fileshare.main import app
from packages.testing.security import create_test_auth_headers


@pytest.fixture
def uploader_headers() -> dict[str, str]:
    return create_test_auth_headers(
        user_id="lead_crc_01",
        roles=["site_crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def viewer_headers() -> dict[str, str]:
    return create_test_auth_headers(
        user_id="monitor_cra_02",
        roles=["cra_monitor"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def admin_headers() -> dict[str, str]:
    return create_test_auth_headers(
        user_id="super_admin_01",
        roles=["super_admin"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def unauthorized_headers() -> dict[str, str]:
    return create_test_auth_headers(
        user_id="unrelated_user",
        roles=["site_crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.mark.asyncio
async def test_get_activity_feed_paginated_and_ordered(
    db_session: AsyncSession,
    uploader_headers: dict[str, str],
):
    """Verify activity feed returns events ordered by timestamp DESC with pagination.

    @req:PRD-DOC-001
    @req:PRD-SYS-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_repo = SqlAlchemyFileRecordRepository(db_session)
    audit_repo = SqlAlchemyFileAuditLogRepository(db_session)

    # 1. Create file record
    file = FileRecord(
        id="file-act-001",
        study_id="STUDY-ACT-101",
        filename="Lab_Report_Subject_101.pdf",
        mime_type="application/pdf",
        size_bytes=1024,
        object_key="studies/STUDY-ACT-101/Lab_Report_Subject_101.pdf",
        uploaded_by="lead_crc_01",
        status=FileStatus.READY,
        scan_status=ScanStatus.CLEAN,
        created_by="lead_crc_01",
        reason_for_change="Baseline lab report upload",
    )
    await file_repo.save(file)

    # 2. Add multiple chronological audit logs
    base_time = datetime(2026, 8, 20, 10, 0, 0, tzinfo=UTC)
    actions = [
        ("FILE_UPLOADED", "lead_crc_01", {"version": 1}, base_time),
        (
            "SCAN_COMPLETE",
            "system",
            {"engine": "clamav", "status": "CLEAN"},
            base_time + timedelta(minutes=5),
        ),
        (
            "GRANT_CREATED",
            "lead_crc_01",
            {"granted_to": "monitor_cra_02", "permission": "view"},
            base_time + timedelta(minutes=10),
        ),
        (
            "VIEW_OPEN",
            "monitor_cra_02",
            {"source": "web"},
            base_time + timedelta(minutes=15),
        ),
        (
            "DOCUMENT_APPROVED",
            "pi_user_01",
            {"meaning": "Approval as Principal Investigator"},
            base_time + timedelta(minutes=20),
        ),
    ]

    for action, user_id, detail, ts in actions:
        log = FileAuditLog(
            file_record_id=file.id,
            action=action,
            user_id=user_id,
            timestamp=ts,
            reason_for_change="GxP audit trail activity",
            detail=detail,
        )
        await audit_repo.save(log)
    await db_session.commit()

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # Request page 1 with page_size = 3
        resp = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity?page=1&page_size=3",
            headers=uploader_headers,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["total"] == 5
        assert data["page"] == 1
        assert data["page_size"] == 3
        assert len(data["entries"]) == 3

        # Verify timestamp DESC ordering (newest first)
        assert data["entries"][0]["event_type"] == "DOCUMENT_APPROVED"
        assert data["entries"][1]["event_type"] == "VIEW_OPEN"
        assert data["entries"][2]["event_type"] == "GRANT_CREATED"
        assert data["entries"][0]["actor_display_name"] == "Pi User 01"

        # Request page 2 with page_size = 3
        resp_page2 = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity?page=2&page_size=3",
            headers=uploader_headers,
        )
        assert resp_page2.status_code == 200
        data2 = resp_page2.json()
        assert len(data2["entries"]) == 2
        assert data2["entries"][0]["event_type"] == "SCAN_COMPLETE"
        assert data2["entries"][1]["event_type"] == "FILE_UPLOADED"


@pytest.mark.asyncio
async def test_get_activity_feed_filtering_by_event_type_and_date(
    db_session: AsyncSession,
    uploader_headers: dict[str, str],
):
    """Verify activity feed filters by event_type and date ranges.

    @req:PRD-DOC-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_repo = SqlAlchemyFileRecordRepository(db_session)
    audit_repo = SqlAlchemyFileAuditLogRepository(db_session)

    file = FileRecord(
        id="file-act-002",
        study_id="STUDY-ACT-102",
        filename="ECG_Trace_Subject_102.pdf",
        mime_type="application/pdf",
        size_bytes=2048,
        object_key="studies/STUDY-ACT-102/ECG_Trace_Subject_102.pdf",
        uploaded_by="lead_crc_01",
        status=FileStatus.READY,
        scan_status=ScanStatus.CLEAN,
        created_by="lead_crc_01",
        reason_for_change="Baseline ECG upload",
    )
    await file_repo.save(file)

    base_time = datetime(2026, 8, 21, 12, 0, 0, tzinfo=UTC)
    for i, action in enumerate(
        ["FILE_UPLOADED", "GRANT_CREATED", "GRANT_REVOKED", "GRANT_CREATED"]
    ):
        log = FileAuditLog(
            file_record_id=file.id,
            action=action,
            user_id="lead_crc_01",
            timestamp=base_time + timedelta(hours=i),
            reason_for_change="Activity test event",
            detail={"idx": i},
        )
        await audit_repo.save(log)
    await db_session.commit()

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # Filter by event_type = GRANT_CREATED
        resp = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity?event_type=GRANT_CREATED",
            headers=uploader_headers,
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["total"] == 2
        assert all(e["event_type"] == "GRANT_CREATED" for e in data["entries"])

        # Filter by date range
        from_str = (base_time + timedelta(hours=1, minutes=30)).isoformat()
        resp_date = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity?from_date={from_str}",
            headers=uploader_headers,
        )
        assert resp_date.status_code == 200
        data_date = resp_date.json()
        assert data_date["total"] == 2
        assert [e["event_type"] for e in data_date["entries"]] == [
            "GRANT_CREATED",
            "GRANT_REVOKED",
        ]


@pytest.mark.asyncio
async def test_get_activity_feed_permission_gating(
    db_session: AsyncSession,
    uploader_headers: dict[str, str],
    viewer_headers: dict[str, str],
    admin_headers: dict[str, str],
    unauthorized_headers: dict[str, str],
):
    """Verify authorization checks for activity feed inspection.

    @req:PRD-DOC-001
    @req:PRD-SYS-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    file_repo = SqlAlchemyFileRecordRepository(db_session)
    grant_repo = SqlAlchemyShareGrantRepository(db_session)
    audit_repo = SqlAlchemyFileAuditLogRepository(db_session)

    file = FileRecord(
        id="file-act-003",
        study_id="STUDY-ACT-103",
        filename="Consent_Form_v1.pdf",
        mime_type="application/pdf",
        size_bytes=4096,
        object_key="studies/STUDY-ACT-103/Consent_Form_v1.pdf",
        uploaded_by="lead_crc_01",
        status=FileStatus.READY,
        scan_status=ScanStatus.CLEAN,
        created_by="lead_crc_01",
        reason_for_change="Baseline consent form",
    )
    await file_repo.save(file)

    # Grant view access to monitor_cra_02
    grant = ShareGrant(
        file_record_id=file.id,
        granted_to_user_id="monitor_cra_02",
        granted_by_user_id="lead_crc_01",
        scope=ShareScope.INDIVIDUAL,
        permission_level=PermissionLevel.VIEW,
        created_by="lead_crc_01",
        reason_for_change="Grant CRA monitor view access",
    )
    await grant_repo.save(grant)

    log = FileAuditLog(
        file_record_id=file.id,
        action="FILE_UPLOADED",
        user_id="lead_crc_01",
        reason_for_change="Initial upload",
    )
    await audit_repo.save(log)
    await db_session.commit()

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Uploader can view
        resp_up = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity",
            headers=uploader_headers,
        )
        assert resp_up.status_code == 200

        # 2. Shared viewer can view
        resp_view = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity",
            headers=viewer_headers,
        )
        assert resp_view.status_code == 200

        # 3. Super Admin can view
        resp_admin = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity",
            headers=admin_headers,
        )
        assert resp_admin.status_code == 200

        # 4. Unauthorized user gets 403
        resp_unauth = await client.get(
            f"/api/v1/fileshare/files/{file.id}/activity",
            headers=unauthorized_headers,
        )
        assert resp_unauth.status_code == 403

        # 5. Non-existent file gets 404
        resp_404 = await client.get(
            "/api/v1/fileshare/files/non-existent-file-id/activity",
            headers=admin_headers,
        )
        assert resp_404.status_code == 404
