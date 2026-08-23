"""Unit tests for Google Drive client integration and run manifest generator.

@req:PRD-SYS-105
"""

import hashlib
import json
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

from packages.storage.gdrive import (
    GoogleDriveClient,
    compute_sha256,
    generate_run_manifest,
)


@pytest.fixture
def temp_recordings_dir(tmp_path: Path) -> Path:
    """Create a temporary directory simulating video recording artifacts."""
    run_dir = tmp_path / "Cadence_Recordings_2026-08-23_17-00"
    run_dir.mkdir(parents=True, exist_ok=True)

    # Create dummy video and report files
    video1 = run_dir / "01_Study_Design_MDR.webm"
    video1.write_bytes(b"dummy webm video content for suite 1")

    video2 = run_dir / "02_Clinical_Data_Capture.webm"
    video2.write_bytes(b"dummy webm video content for suite 2")

    report = run_dir / "verification_report.html"
    report.write_text("<html><body>Verification Passed</body></html>")

    return run_dir


def test_compute_sha256(tmp_path: Path):
    """Verify SHA-256 calculation for file artifacts.

    @req:PRD-SYS-105
    """
    test_file = tmp_path / "sample.txt"
    content = b"Cadence Clinical Research Software GxP Video Artifact"
    test_file.write_bytes(content)

    expected_hash = hashlib.sha256(content).hexdigest()
    actual_hash = compute_sha256(test_file)
    assert actual_hash == expected_hash


def test_generate_run_manifest(temp_recordings_dir: Path):
    """Verify generation of run manifest containing file hashes, sizes, and metadata.

    @req:PRD-SYS-105
    """
    metadata = {
        "run_id": "RUN-20260823-1700",
        "suites_executed": 2,
        "triple_sentinel_passed": True,
    }
    manifest = generate_run_manifest(temp_recordings_dir, run_metadata=metadata)

    assert manifest["run_metadata"]["run_id"] == "RUN-20260823-1700"
    assert manifest["run_metadata"]["triple_sentinel_passed"] is True
    assert len(manifest["files"]) == 3

    filenames = [f["name"] for f in manifest["files"]]
    assert "01_Study_Design_MDR.webm" in filenames
    assert "02_Clinical_Data_Capture.webm" in filenames
    assert "verification_report.html" in filenames


def test_gdrive_client_unconfigured(monkeypatch: pytest.MonkeyPatch):
    """Verify GoogleDriveClient reports unconfigured when no credentials are provided.

    @req:PRD-SYS-105
    """
    monkeypatch.delenv("GOOGLE_APPLICATION_CREDENTIALS", raising=False)
    monkeypatch.delenv("GOOGLE_DRIVE_SERVICE_ACCOUNT_PATH", raising=False)
    client = GoogleDriveClient(credentials_path=None, service_account_info=None)
    assert client.is_configured is False

    with pytest.raises(
        RuntimeError, match="Google Drive credentials are not configured"
    ):
        client.get_service()


@patch("packages.storage.gdrive.service_account.Credentials.from_service_account_file")
@patch("packages.storage.gdrive.build")
def test_gdrive_client_validate_credentials(
    mock_build, mock_creds_file, tmp_path: Path
):
    """Verify credentials validation and user identity query against Google Drive API.

    @req:PRD-SYS-105
    """
    fake_key_file = tmp_path / "service_account.json"
    fake_key_file.write_text(
        json.dumps(
            {
                "type": "service_account",
                "client_email": "cadence-bot@gserviceaccount.com",
            }
        )
    )

    mock_service = MagicMock()
    mock_about = MagicMock()
    mock_about.get.return_value.execute.return_value = {
        "user": {
            "emailAddress": "cadence-bot@gserviceaccount.com",
            "displayName": "Cadence Bot",
        },
        "storageQuota": {"limit": "1000000000", "usage": "500000"},
    }
    mock_service.about.return_value = mock_about
    mock_build.return_value = mock_service

    client = GoogleDriveClient(credentials_path=str(fake_key_file))
    assert client.is_configured is True

    validation = client.validate_credentials()
    assert validation["email"] == "cadence-bot@gserviceaccount.com"
    assert validation["valid"] is True


@patch("packages.storage.gdrive.service_account.Credentials.from_service_account_file")
@patch("packages.storage.gdrive.build")
def test_gdrive_client_create_folder(mock_build, mock_creds_file, tmp_path: Path):
    """Verify folder creation under parent Google Drive folder.

    @req:PRD-SYS-105
    """
    fake_key_file = tmp_path / "sa.json"
    fake_key_file.write_text("{}")

    mock_service = MagicMock()
    mock_files = MagicMock()
    mock_files.create.return_value.execute.return_value = {
        "id": "folder_12345",
        "name": "Cadence_Run_Folder",
        "mimeType": "application/vnd.google-apps.folder",
    }
    mock_service.files.return_value = mock_files
    mock_build.return_value = mock_service

    client = GoogleDriveClient(credentials_path=str(fake_key_file))
    folder_id = client.create_folder(
        "Cadence_Run_Folder", parent_folder_id="parent_root_999"
    )

    assert folder_id == "folder_12345"
    mock_files.create.assert_called_once()


@patch("packages.storage.gdrive.MediaFileUpload")
@patch("packages.storage.gdrive.service_account.Credentials.from_service_account_file")
@patch("packages.storage.gdrive.build")
def test_gdrive_client_upload_file(
    mock_build, mock_creds_file, mock_media, tmp_path: Path
):
    """Verify single file upload with metadata return.

    @req:PRD-SYS-105
    """
    fake_key_file = tmp_path / "sa.json"
    fake_key_file.write_text("{}")

    test_video = tmp_path / "01_MDR.webm"
    test_video.write_bytes(b"dummy content")

    mock_service = MagicMock()
    mock_files = MagicMock()
    mock_files.create.return_value.execute.return_value = {
        "id": "file_abc_123",
        "name": "01_MDR.webm",
        "webViewLink": "https://drive.google.com/file/d/file_abc_123/view",
        "size": "13",
    }
    mock_service.files.return_value = mock_files
    mock_build.return_value = mock_service

    client = GoogleDriveClient(credentials_path=str(fake_key_file))
    res = client.upload_file(test_video, folder_id="folder_12345")

    assert res["id"] == "file_abc_123"
    assert res["name"] == "01_MDR.webm"
    assert "webViewLink" in res
