"""Tests for Per-Document Activity Feed and GxP Inspection Trail.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, 21 CFR Part 11, ICH E6 GCP
"""

from datetime import UTC, datetime, timedelta

import httpx
import pytest
from httpx import ASGITransport
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.domain.models import PermissionLevel, ShareScope
from apps.fileshare.main import app
from packages.testing.fakes import InMemoryStoragePort
from packages.testing.security import create_test_auth_headers


@pytest.fixture
def auth_headers_uploader() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a CRC uploader."""
    return create_test_auth_headers(
        user_id="crc_user_01",
        roles=["crc", "site_crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_reviewer() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a reviewer."""
    return create_test_auth_headers(
        user_id="reviewer_user_01",
        roles=["data_manager"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_unauthorized() -> dict[str, str]:
    """Generates authentic gateway headers for an unrelated user."""
    return create_test_auth_headers(
        user_id="unrelated_user_99",
        roles=["site_crc"],
        tenant_id="tenant_trial_01",
    )


@pytest.fixture
def auth_headers_super_admin() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a super admin."""
    return create_test_auth_headers(
        user_id="admin_01",
        roles=["super_admin"],
        tenant_id="tenant_trial_01",
    )


@pytest.mark.asyncio
async def test_activity_feed_lifecycle_events(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
    auth_headers_super_admin: dict[str, str],
):
    """Verify activity feed captures full document lifecycle events chronologically.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Upload File
        upload_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-ACT-01",
                "site_id": "SITE-01",
                "filename": "protocol_amendment_v1.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 10240,
                "reason_for_change": "Initial regulatory protocol upload",
            },
            headers=auth_headers_uploader,
        )
        assert upload_resp.status_code == 201
        upload_data = upload_resp.json()
        file_id = upload_data["file_id"]
        object_key = upload_data["object_key"]

        await mock_storage.put_object(
            key=object_key,
            data=b"%PDF-1.4 mock pdf content",
            content_type="application/pdf",
        )

        # 2. Complete Upload / Virus Scan
        complete_resp = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={
                "reason_for_change": "Completed upload and scan check",
            },
            headers=auth_headers_uploader,
        )
        assert complete_resp.status_code == 200

        # 3. Create Share Grant
        grant_resp = await client.post(
            f"/api/v1/fileshare/files/{file_id}/grants",
            json={
                "granted_to_user_id": "cra_monitor_02",
                "scope": ShareScope.INDIVIDUAL.value,
                "permission_level": PermissionLevel.VIEW.value,
                "reason_for_change": "Grant monitoring review access",
            },
            headers=auth_headers_uploader,
        )
        assert grant_resp.status_code == 201
        grant_id = grant_resp.json()["id"]

        # 4. Revoke Share Grant
        revoke_resp = await client.delete(
            f"/api/v1/fileshare/files/{file_id}/grants/{grant_id}",
            headers=auth_headers_uploader,
        )
        assert revoke_resp.status_code == 200

        # 5. Create Guest Link
        guest_resp = await client.post(
            f"/api/v1/fileshare/files/{file_id}/guest-links",
            json={
                "expires_in_hours": 12,
                "reason_for_change": "External auditor inspection link",
            },
            headers=auth_headers_uploader,
        )
        assert guest_resp.status_code == 201
        guest_url = guest_resp.json()["guest_url"]
        guest_link_id = guest_resp.json()["id"]

        # 6. Access Guest Link
        token = guest_url.split("/")[-1]
        access_resp = await client.get(f"/api/v1/fileshare/guest/{token}")
        assert access_resp.status_code == 200

        # 7. Revoke Guest Link
        revoke_guest_resp = await client.delete(
            f"/api/v1/fileshare/files/{file_id}/guest-links/{guest_link_id}",
            headers=auth_headers_uploader,
        )
        assert revoke_guest_resp.status_code == 200

        # 8. Set and Clear Hold
        hold_resp = await client.post(
            f"/api/v1/fileshare/files/{file_id}/hold",
            json={
                "hold_reason": "Audit freeze",
                "reason_for_change": "Placing retention hold",
            },
            headers=auth_headers_super_admin,
        )
        assert hold_resp.status_code == 200

        clear_hold_resp = await client.delete(
            f"/api/v1/fileshare/files/{file_id}/hold",
            headers=auth_headers_super_admin,
        )
        assert clear_hold_resp.status_code == 200

        # 9. Query Activity Feed (Full)
        feed_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/activity",
            headers=auth_headers_uploader,
        )
        assert feed_resp.status_code == 200
        feed_data = feed_resp.json()

        assert "entries" in feed_data
        assert feed_data["total"] >= 8
        assert feed_data["page"] == 1
        assert feed_data["page_size"] == 50

        events = [e["event_type"] for e in feed_data["entries"]]
        assert "FILE_UPLOADED" in events
        assert "SCAN_COMPLETE" in events
        assert "GRANT_CREATED" in events
        assert "GRANT_REVOKED" in events
        assert "GUEST_LINK_CREATED" in events
        assert "GUEST_LINK_ACCESSED" in events
        assert "GUEST_LINK_REVOKED" in events
        assert "HOLD_SET" in events
        assert "HOLD_CLEARED" in events

        # Verify entry schema
        first_entry = feed_data["entries"][0]
        assert "event_type" in first_entry
        assert "actor_user_id" in first_entry
        assert "actor_display_name" in first_entry
        assert "timestamp" in first_entry


@pytest.mark.asyncio
async def test_activity_feed_filtering_and_pagination(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
):
    """Verify activity feed event_type filtering, date filtering, and pagination.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        upload_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-FILTER-01",
                "site_id": "SITE-01",
                "filename": "document_filtering_test.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 4096,
                "reason_for_change": "Upload for filter tests",
            },
            headers=auth_headers_uploader,
        )
        file_id = upload_resp.json()["file_id"]

        # Create multiple grants to test pagination
        for i in range(5):
            await client.post(
                f"/api/v1/fileshare/files/{file_id}/grants",
                json={
                    "granted_to_user_id": f"user_filter_{i}",
                    "scope": ShareScope.INDIVIDUAL.value,
                    "permission_level": PermissionLevel.VIEW.value,
                    "reason_for_change": f"Grant index {i}",
                },
                headers=auth_headers_uploader,
            )

        # 1. Filter by event_type
        filtered_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/activity?event_type=GRANT_CREATED",
            headers=auth_headers_uploader,
        )
        assert filtered_resp.status_code == 200
        filtered_data = filtered_resp.json()
        assert filtered_data["total"] == 5
        for entry in filtered_data["entries"]:
            assert entry["event_type"] == "GRANT_CREATED"

        # 2. Pagination test (page_size=2)
        page1_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/activity?event_type=GRANT_CREATED&page=1&page_size=2",
            headers=auth_headers_uploader,
        )
        assert page1_resp.status_code == 200
        page1_data = page1_resp.json()
        assert len(page1_data["entries"]) == 2
        assert page1_data["total"] == 5
        assert page1_data["page"] == 1
        assert page1_data["page_size"] == 2

        page2_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/activity?event_type=GRANT_CREATED&page=2&page_size=2",
            headers=auth_headers_uploader,
        )
        assert page2_resp.status_code == 200
        page2_data = page2_resp.json()
        assert len(page2_data["entries"]) == 2
        assert page2_data["page"] == 2

        # 3. Date range filter
        now = datetime.now(UTC)
        from_str = (now - timedelta(minutes=5)).isoformat()
        to_str = (now + timedelta(minutes=5)).isoformat()

        date_filtered_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/activity?from_date={from_str}&to_date={to_str}",
            headers=auth_headers_uploader,
        )
        assert date_filtered_resp.status_code == 200
        assert date_filtered_resp.json()["total"] >= 6


@pytest.mark.asyncio
async def test_activity_feed_authorization_and_alias(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    auth_headers_uploader: dict[str, str],
    auth_headers_unauthorized: dict[str, str],
    auth_headers_super_admin: dict[str, str],
):
    """Verify authorization checks (403 for unauthorized, 404 for missing) and alias router route.

    @req:PRD-SYS-001
    @req:PRD-DOC-001
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        upload_resp = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json={
                "study_id": "STUDY-AUTH-01",
                "site_id": "SITE-01",
                "filename": "security_test.pdf",
                "mime_type": "application/pdf",
                "size_bytes": 2048,
                "reason_for_change": "Security test upload",
            },
            headers=auth_headers_uploader,
        )
        file_id = upload_resp.json()["file_id"]

        # 1. 403 Forbidden for unauthorized user
        forbidden_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/activity",
            headers=auth_headers_unauthorized,
        )
        assert forbidden_resp.status_code == 403

        # 2. 404 Not Found for non-existent file
        not_found_resp = await client.get(
            "/api/v1/fileshare/files/non-existent-id-000/activity",
            headers=auth_headers_super_admin,
        )
        assert not_found_resp.status_code == 404

        # 3. Super admin can access
        admin_resp = await client.get(
            f"/api/v1/fileshare/files/{file_id}/activity",
            headers=auth_headers_super_admin,
        )
        assert admin_resp.status_code == 200

        # 4. Alias route `/api/v1/files/{file_id}/activity` works identically
        alias_resp = await client.get(
            f"/api/v1/files/{file_id}/activity",
            headers=auth_headers_uploader,
        )
        assert alias_resp.status_code == 200
        assert alias_resp.json()["total"] >= 1
