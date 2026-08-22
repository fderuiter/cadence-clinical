"""Integration tests for FileShare virus and malware scanning lifecycle and quarantine gating.

Requirements: PRD-SYS-001, PRD-DOC-001, PRD-DOC-002, PRD-DOC-003
"""

import httpx
import pytest
from httpx import ASGITransport
from sqlalchemy.ext.asyncio import AsyncSession

from apps.fileshare.adapters.clamav_adapter import MockScanAdapter
from apps.fileshare.adapters.database import get_db_session
from apps.fileshare.adapters.repositories import (
    SqlAlchemyFileRecordRepository,
    SqlAlchemyGuestLinkRepository,
    SqlAlchemyShareGrantRepository,
)
from apps.fileshare.application.fileshare_service import FileShareService
from apps.fileshare.domain.models import FileStatus, ScanStatus, TranscodeStatus
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
def auth_headers_admin() -> dict[str, str]:
    """Generates authentic gateway authentication headers for a super_admin."""
    return create_test_auth_headers(
        user_id="admin_user_01",
        roles=["super_admin"],
        tenant_id="tenant_trial_01",
    )


@pytest.mark.asyncio
async def test_clean_file_upload_scan_lifecycle(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    auth_headers_uploader: dict[str, str],
):
    """Verify clean file upload undergoes scanning, marks status READY and CLEAN, and permits download.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Request presigned upload URL
        upload_req = {
            "study_id": "STUDY-PHASE3-001",
            "site_id": "SITE-101",
            "filename": "clinical_protocol.pdf",
            "mime_type": "application/pdf",
            "size_bytes": 1024,
            "reason_for_change": "Initial upload of protocol document",
        }
        res_upload = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=upload_req,
            headers=auth_headers_uploader,
        )
        assert res_upload.status_code == 201
        upload_data = res_upload.json()
        file_id = upload_data["file_id"]
        object_key = upload_data["object_key"]

        # 2. Put clean payload into storage
        clean_bytes = b"Benign clinical study trial document payload."
        await mock_storage.put_object(
            key=object_key,
            data=clean_bytes,
            content_type="application/pdf",
        )

        # 3. Complete upload and trigger virus scan
        complete_req = {
            "reason_for_change": "Completed upload of clinical protocol",
        }
        res_complete = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json=complete_req,
            headers=auth_headers_uploader,
        )
        assert res_complete.status_code == 200
        completed_data = res_complete.json()

        assert completed_data["status"] == FileStatus.READY
        assert completed_data["scan_status"] == ScanStatus.CLEAN
        assert completed_data["scan_engine"] == "clamav-1.3"
        assert completed_data["scan_completed_at"] is not None
        assert completed_data["scan_threat_name"] is None

        # 4. Verify file is accessible for download
        res_download = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_uploader,
        )
        assert res_download.status_code == 200
        download_data = res_download.json()
        assert download_data["file_id"] == file_id
        assert "download_url" in download_data


@pytest.mark.asyncio
async def test_eicar_infected_file_upload_quarantined_and_blocked(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    auth_headers_uploader: dict[str, str],
):
    """Verify infected file upload is quarantined, relocated to quarantine/ storage prefix, and blocked from all endpoints.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    @req:PRD-DOC-003
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # 1. Request upload URL
        upload_req = {
            "study_id": "STUDY-PHASE3-001",
            "site_id": "SITE-101",
            "filename": "infected_lab_results.pdf",
            "mime_type": "application/pdf",
            "size_bytes": 68,
            "reason_for_change": "Lab report upload",
        }
        res_upload = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=upload_req,
            headers=auth_headers_uploader,
        )
        assert res_upload.status_code == 201
        upload_data = res_upload.json()
        file_id = upload_data["file_id"]
        original_key = upload_data["object_key"]

        # 2. Put EICAR test payload into storage
        eicar_payload = (
            b"X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"
        )
        await mock_storage.put_object(
            key=original_key,
            data=eicar_payload,
            content_type="application/pdf",
        )

        # 3. Complete upload
        res_complete = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={"reason_for_change": "Finishing upload"},
            headers=auth_headers_uploader,
        )
        assert res_complete.status_code == 200
        record_data = res_complete.json()

        # Verify quarantine status and metadata
        assert record_data["status"] == FileStatus.QUARANTINED
        assert record_data["scan_status"] == ScanStatus.INFECTED
        assert record_data["scan_threat_name"] == "Win.Test.EICAR_HDB-1"
        assert record_data["scan_engine"] == "clamav-1.3"
        assert record_data["scan_completed_at"] is not None
        assert record_data["object_key"] == f"quarantine/{original_key}"

        # 4. Verify storage relocation
        assert f"quarantine/{original_key}" in mock_storage._objects
        assert original_key not in mock_storage._objects

        # 5. Verify download endpoint returns 403 Forbidden with reason QUARANTINED
        res_download = await client.get(
            f"/api/v1/fileshare/files/{file_id}/download-url",
            headers=auth_headers_uploader,
        )
        assert res_download.status_code == 403
        error_detail = res_download.json()["detail"]
        assert error_detail["reason"] == "QUARANTINED"
        assert "quarantined" in error_detail["message"].lower()

        # 6. Verify share grant creation returns 403 Forbidden
        grant_req = {
            "granted_to_user_id": "colleague_01",
            "scope": "individual",
            "permission_level": "view",
            "reason_for_change": "Attempting to share quarantined file",
        }
        res_grant = await client.post(
            f"/api/v1/fileshare/files/{file_id}/grants",
            json=grant_req,
            headers=auth_headers_uploader,
        )
        assert res_grant.status_code == 403
        assert res_grant.json()["detail"]["reason"] == "QUARANTINED"

        # 7. Verify guest link creation returns 403 Forbidden
        guest_req = {
            "expires_in_hours": 24,
            "reason_for_change": "Attempting guest link for quarantined file",
        }
        res_guest = await client.post(
            f"/api/v1/fileshare/files/{file_id}/guest-links",
            json=guest_req,
            headers=auth_headers_uploader,
        )
        assert res_guest.status_code == 403
        assert res_guest.json()["detail"]["reason"] == "QUARANTINED"

        # 8. Verify stream URL generation returns 403 Forbidden
        res_stream = await client.get(
            f"/api/v1/fileshare/files/{file_id}/stream-url",
            headers=auth_headers_uploader,
        )
        assert res_stream.status_code == 403
        assert res_stream.json()["detail"]["reason"] == "QUARANTINED"


@pytest.mark.asyncio
async def test_transient_scan_error_retries_and_succeeds(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    auth_headers_uploader: dict[str, str],
):
    """Verify transient scan errors trigger exponential retry and succeed if subsequent attempt passes.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    # Configure scanner to fail first 2 attempts then succeed on 3rd
    mock_scanner.simulate_error_count = 2

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        upload_req = {
            "study_id": "STUDY-PHASE3-001",
            "filename": "retry_test.pdf",
            "mime_type": "application/pdf",
            "size_bytes": 500,
            "reason_for_change": "Retry resilience test",
        }
        res_upload = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=upload_req,
            headers=auth_headers_uploader,
        )
        file_id = res_upload.json()["file_id"]
        object_key = res_upload.json()["object_key"]

        await mock_storage.put_object(
            key=object_key,
            data=b"Clean bytes with transient scanner hiccup",
            content_type="application/pdf",
        )

        res_complete = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={"reason_for_change": "Complete upload"},
            headers=auth_headers_uploader,
        )
        assert res_complete.status_code == 200
        record_data = res_complete.json()

        assert record_data["status"] == FileStatus.READY
        assert record_data["scan_status"] == ScanStatus.CLEAN
        assert mock_scanner.scan_count == 3


@pytest.mark.asyncio
async def test_persistent_scan_error_marks_scan_error_state(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    auth_headers_uploader: dict[str, str],
):
    """Verify that when all 3 scan attempts fail, the file is marked PENDING_SCAN and SCAN_ERROR.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    # Configure scanner to fail all attempts
    mock_scanner.simulate_error_count = 5

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        upload_req = {
            "study_id": "STUDY-PHASE3-001",
            "filename": "persistent_error.pdf",
            "mime_type": "application/pdf",
            "size_bytes": 500,
            "reason_for_change": "Error resilience test",
        }
        res_upload = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=upload_req,
            headers=auth_headers_uploader,
        )
        file_id = res_upload.json()["file_id"]
        object_key = res_upload.json()["object_key"]

        await mock_storage.put_object(
            key=object_key,
            data=b"Clean bytes but scanner is completely down",
            content_type="application/pdf",
        )

        res_complete = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={"reason_for_change": "Complete upload"},
            headers=auth_headers_uploader,
        )
        assert res_complete.status_code == 200
        record_data = res_complete.json()

        assert record_data["status"] == FileStatus.PENDING_SCAN
        assert record_data["scan_status"] == ScanStatus.SCAN_ERROR
        assert mock_scanner.scan_count == 3


@pytest.mark.asyncio
async def test_clean_video_advances_to_pending_transcode(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    auth_headers_uploader: dict[str, str],
):
    """Verify clean video upload transitions to PENDING_TRANSCODE upon passing virus scan.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        upload_req = {
            "study_id": "STUDY-PHASE3-001",
            "filename": "surgical_procedure.mp4",
            "mime_type": "video/mp4",
            "size_bytes": 10485760,
            "reason_for_change": "Surgical video recording",
        }
        res_upload = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=upload_req,
            headers=auth_headers_uploader,
        )
        file_id = res_upload.json()["file_id"]
        object_key = res_upload.json()["object_key"]

        await mock_storage.put_object(
            key=object_key,
            data=b"Mock video container data stream",
            content_type="video/mp4",
        )

        res_complete = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={"reason_for_change": "Complete upload"},
            headers=auth_headers_uploader,
        )
        assert res_complete.status_code == 200
        record_data = res_complete.json()

        assert record_data["status"] == FileStatus.PENDING_TRANSCODE
        assert record_data["scan_status"] == ScanStatus.CLEAN
        assert record_data["transcode_status"] == TranscodeStatus.PENDING


@pytest.mark.asyncio
async def test_infected_video_quarantined_and_transcode_failed(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    auth_headers_uploader: dict[str, str],
):
    """Verify infected video is quarantined and transcoding is failed rather than scheduled.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        upload_req = {
            "study_id": "STUDY-PHASE3-001",
            "filename": "infected_video.mp4",
            "mime_type": "video/mp4",
            "size_bytes": 5000,
            "reason_for_change": "Video upload test",
        }
        res_upload = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=upload_req,
            headers=auth_headers_uploader,
        )
        file_id = res_upload.json()["file_id"]
        object_key = res_upload.json()["object_key"]

        eicar_video_payload = (
            b"X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"
        )
        await mock_storage.put_object(
            key=object_key,
            data=eicar_video_payload,
            content_type="video/mp4",
        )

        res_complete = await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={"reason_for_change": "Complete upload"},
            headers=auth_headers_uploader,
        )
        assert res_complete.status_code == 200
        record_data = res_complete.json()

        assert record_data["status"] == FileStatus.QUARANTINED
        assert record_data["scan_status"] == ScanStatus.INFECTED
        assert record_data["transcode_status"] == TranscodeStatus.FAILED
        assert "quarantine" in record_data["object_key"]


@pytest.mark.asyncio
async def test_access_guest_link_blocked_if_file_quarantined(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    auth_headers_uploader: dict[str, str],
):
    """Verify that accessing a previously generated guest link for a quarantined file is blocked with 403.

    @req:PRD-DOC-001
    @req:PRD-DOC-003
    """
    app.dependency_overrides[get_db_session] = lambda: db_session

    transport = ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as client:
        # Upload clean file
        upload_req = {
            "study_id": "STUDY-PHASE3-001",
            "filename": "guest_quarantine_test.pdf",
            "mime_type": "application/pdf",
            "size_bytes": 100,
            "reason_for_change": "Guest quarantine test",
        }
        res_upload = await client.post(
            "/api/v1/fileshare/files/upload-url",
            json=upload_req,
            headers=auth_headers_uploader,
        )
        file_id = res_upload.json()["file_id"]
        object_key = res_upload.json()["object_key"]

        await mock_storage.put_object(
            key=object_key,
            data=b"Initial clean bytes",
            content_type="application/pdf",
        )

        await client.post(
            f"/api/v1/fileshare/files/{file_id}/complete-upload",
            json={"reason_for_change": "Complete upload"},
            headers=auth_headers_uploader,
        )

        # Generate guest link
        res_guest = await client.post(
            f"/api/v1/fileshare/files/{file_id}/guest-links",
            json={"expires_in_hours": 24, "reason_for_change": "Guest share"},
            headers=auth_headers_uploader,
        )
        assert res_guest.status_code == 201
        guest_url = res_guest.json()["guest_url"]
        token = guest_url.split("/")[-1]

        # Verify guest access works initially
        res_access1 = await client.get(f"/api/v1/fileshare/guest/{token}")
        assert res_access1.status_code == 200

        # Now mark file as QUARANTINED in database
        repo = SqlAlchemyFileRecordRepository(db_session)
        rec = await repo.get_by_id(file_id)
        assert rec is not None
        rec = rec.model_copy(
            update={
                "status": FileStatus.QUARANTINED,
                "scan_status": ScanStatus.INFECTED,
            }
        )
        await repo.save(rec)

        # Access guest link again -> 403 Forbidden
        res_access2 = await client.get(f"/api/v1/fileshare/guest/{token}")
        assert res_access2.status_code == 403
        assert res_access2.json()["detail"]["reason"] == "QUARANTINED"


@pytest.mark.asyncio
async def test_notification_dispatch_on_infected_and_error(
    db_session: AsyncSession,
    mock_storage: InMemoryStoragePort,
    mock_scanner: MockScanAdapter,
    monkeypatch,
):
    """Verify uploader and super_admin security notifications are dispatched upon malware discovery and scan failure.

    @req:PRD-DOC-001
    @req:PRD-DOC-002
    """
    published_events = []

    async def mock_publish(event):
        published_events.append(event)
        return True

    monkeypatch.setattr(
        "apps.fileshare.application.fileshare_service.publish_notification",
        mock_publish,
    )

    file_repo = SqlAlchemyFileRecordRepository(db_session)
    grant_repo = SqlAlchemyShareGrantRepository(db_session)
    guest_repo = SqlAlchemyGuestLinkRepository(db_session)

    service = FileShareService(
        file_repo=file_repo,
        grant_repo=grant_repo,
        guest_repo=guest_repo,
        storage_port=mock_storage,
        scan_port=mock_scanner,
    )

    # 1. Test infected file upload triggers 2 notifications (uploader + super_admin)
    upload_session = await service.generate_upload_url(
        study_id="STUDY-NOTIF-01",
        filename="malicious.exe",
        mime_type="application/octet-stream",
        size_bytes=100,
        uploader_id="investigator_bob",
        reason_for_change="Test upload",
    )

    eicar_payload = (
        b"X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"
    )
    await mock_storage.put_object(
        key=upload_session.object_key,
        data=eicar_payload,
        content_type="application/octet-stream",
    )

    completed = await service.complete_upload(
        file_id=upload_session.file_id,
        caller_user_id="investigator_bob",
    )
    assert completed.status == FileStatus.QUARANTINED
    assert len(published_events) == 2
    assert published_events[0].recipient_user_id == "investigator_bob"
    assert published_events[1].recipient_role == "super_admin"

    # 2. Test persistent scanner error triggers 1 notification (super_admin)
    mock_scanner.simulate_error_count = 5
    published_events.clear()

    upload_session2 = await service.generate_upload_url(
        study_id="STUDY-NOTIF-01",
        filename="error_file.pdf",
        mime_type="application/pdf",
        size_bytes=100,
        uploader_id="investigator_bob",
        reason_for_change="Test scan error",
    )
    await mock_storage.put_object(
        key=upload_session2.object_key,
        data=b"Some clean bytes",
        content_type="application/pdf",
    )

    completed2 = await service.complete_upload(
        file_id=upload_session2.file_id,
        caller_user_id="investigator_bob",
    )
    assert completed2.status == FileStatus.PENDING_SCAN
    assert completed2.scan_status == ScanStatus.SCAN_ERROR
    assert len(published_events) == 1
    assert published_events[0].recipient_role == "super_admin"
